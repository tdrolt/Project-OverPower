using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using System.Linq;
using Overpower.Data;
using Overpower.Match;
using Overpower.UI;

public class BuildingCapture : MonoBehaviourPun
{
    public int buildingID;

    [Header("Capture Settings")]
    [Tooltip("World metres from the zone centre to the player's centre. Capturing, zone presence (under attack), " +
             "health regen and the shop all use it.")]
    public float captureRadius = 10f;

    [Header("Territory")]
    [Tooltip("1 = Capital, 2 = Transition, 3 = Flanking, 4 = Centre. Decides capture time, income, bounty and " +
             "regen from the Territory Config.")]
    [Range(1, 4)] public int tier = 2;

    [Tooltip("Shared per-tier numbers. Every tower should point at the same asset.")]
    public TerritoryConfig territoryConfig;

    [Header("UI")]
    [Tooltip("Colours, widths and material of the capture ring on the ground around this tower - every tower should point at the same asset, same as Territory Config.")]
    public UiTheme theme;

    // Used only if territoryConfig is missing (logged as an error in Start), so a misconfigured
    // tower keeps working instead of throwing every frame. Reproduces the numbers every tower had
    // before Territory Config existed.
    private const float FallbackCaptureSeconds = 5f;
    private const float FallbackDecaySeconds = 5f;
    private const float FallbackRecaptureCooldownSeconds = 5f;

    // Seconds for ONE player to capture this tower's tier, from the Territory Config. Capture
    // progress below is tracked in those same seconds, not an arbitrary point total, so a
    // designer reading captureProgress mid-match can tell directly how many seconds of solo
    // capturing it represents.
    private float CaptureSeconds =>
        territoryConfig != null ? territoryConfig.ForTier(tier).captureSeconds : FallbackCaptureSeconds;

    // Progress per player per second of capture time. N players capture N times faster - the GDD
    // doesn't specify multi-player capture speed, so this keeps the game's existing behaviour
    // (the old baseCaptureRate was tuned so that N players finished a capture N times sooner).
    private const float ProgressPerPlayerPerSecond = 1f;

    private float DecaySeconds =>
        territoryConfig != null ? territoryConfig.DecaySeconds : FallbackDecaySeconds;

    private float RecaptureCooldownSeconds =>
        territoryConfig != null ? territoryConfig.RecaptureCooldownSeconds : FallbackRecaptureCooldownSeconds;

    [Header("Visual Settings")]
    public Renderer flagRenderer;
    public Material neutralMaterial;
    public Material team0Material;
    public Material team1Material;
    public Material team2Material;

    [Header("Audio Settings")]
    public AudioClip capturingSound;
    public AudioClip capturedSound;
    private AudioSource audioSource;

    [Header("Building Capture ID")]
    [Tooltip("This will be set dynamically to the team id of the first eligible player.")]
    public int capturingID = -1; // -1 means 'unset'

    private int controllingTeam = -1;
    private float captureProgress = 0f;
    private bool isCaptured = false;
    private bool isDecaying = false;
    // Master: the drain is holding because its drainers' way in is under attack (see DrainRule). isDecaying stays
    // true meanwhile, so the drain carries on from where it was.
    private bool isDrainPaused = false;
    private bool isOnCooldown = false;
    private Coroutine cooldownRoutine;

    private List<PlayerTeam> playersInZone = new List<PlayerTeam>();

    // The ring on the ground marking this zone and its capture progress (2026-09-16; it replaced the bar that floated
    // over the tower). Built in Start so every tower gets one - see CaptureRingView. Null if the theme is unassigned.
    private CaptureRingView ringView;

    // The last CaptureProgress THIS client told BuildingManager to publish for this zone - only
    // meaningful while this client is master (only the master ever calls PublishProgressIfNeeded).
    // Starts Idle, matching a fresh, never-captured zone, so a genuinely idle tower never publishes
    // at match start. See BuildingManager.OnMasterClientSwitched for why a master promotion cannot
    // rely on comparing against this alone.
    private CaptureProgress lastPublishedProgress = CaptureProgress.Idle;

    // The ViewID of THIS client's own player while it stands in the zone and was let in by the
    // territory rule (0 = not here). Only the master counts who is in a zone; this lets the player
    // be reported again to a new master (OnMasterClientChanged).
    private int localPlayerViewIdInZone;

    void Start()
    {
        if (territoryConfig == null)
        {
            Debug.LogError($"[BuildingCapture] Tower {buildingID} has no Territory Config assigned - " +
                            "falling back to the old fixed capture numbers (5s/1 per player/5s/5s).", this);
        }

        if (theme == null)
            Debug.LogError($"[BuildingCapture] Tower {buildingID} has no UI Theme assigned - no capture ring will be shown.", this);
        else if (theme.captureRingMaterial == null)
            Debug.LogError($"[BuildingCapture] Tower {buildingID}: UiTheme's Capture Ring Material is not assigned - no capture ring will be shown.", this);
        else
            ringView = CaptureRingView.Create(transform, captureRadius, theme);

        ConfigureCollider();
        InitializeAudio();
        if (BuildingManager.Instance.CathedralBuildingIDs.ContainsKey(buildingID))
        {
            int owner = BuildingManager.Instance.CathedralBuildingIDs[buildingID];
            capturingID = owner;
            controllingTeam = owner;
            isCaptured = true;
            captureProgress = CaptureSeconds;
        }
        else
            ResetFlag();

        // Registered after the starting values above, because registering also applies the
        // room's territory snapshot if this client has already read it - and that must win over
        // the scene's starting values, not be overwritten by them.
        BuildingManager.Instance.RegisterCapture(buildingID, this);

        // The room's snapshot can be applied before this component existed to be notified, so
        // read the current replicated state once here rather than relying only on being told.
        if (BuildingManager.Instance.TowerDictionary.ContainsKey(buildingID))
        {
            TowerData current = BuildingManager.Instance.TowerDictionary[buildingID];
            ApplyOwnerVisual(current.isCaptured, current.controllingTeam);
        }

        Debug.Log($"[BuildingCapture] Building ready with capturingID {capturingID}. Waiting for rightful team to show up.");
    }

    void ConfigureCollider()
    {
        var collider = GetComponent<SphereCollider>();
        if (collider)
        {
            // Capture Radius is in world metres from the zone centre to the player's centre, the distance zone
            // presence, health regen and the shop measure (BuildingManager.TryGetZoneAt). A trigger fires as soon as
            // it touches the edge of the player's body, so it is one body radius smaller: without that, capturing
            // reached about half a metre further than the rest (measured 2026-09-16: captured at 10.5 m, not 10.7 m).
            // Its radius is also in the tower's own units, which scale with the tower (0.8 on these towers).
            float bodyRadius = BuildingManager.Instance != null ? BuildingManager.Instance.PlayerBodyRadius : 0f;
            collider.radius = Mathf.Max(0f, captureRadius - bodyRadius) / Mathf.Max(0.0001f, transform.lossyScale.x);
        }
        else
        {
            Debug.LogWarning("[BuildingCapture] No SphereCollider found on the building!");
        }
    }

    void InitializeAudio()
    {
        audioSource = GetComponent<AudioSource>();
        if (!audioSource)
            Debug.LogWarning("[BuildingCapture] Missing AudioSource component!");
    }

    void ResetFlag()
    {
        if (flagRenderer && neutralMaterial)
        {
            flagRenderer.material = neutralMaterial;
        }
        else
        {
            Debug.LogWarning("[BuildingCapture] Missing flagRenderer or neutralMaterial!");
        }
    }

    void Update()
    {
        // Runs on EVERY client, master or not - the ring is something everyone watches, not
        // something only the master simulates. Reads whatever BuildingManager last decoded from
        // the room (possibly still this client's own write, echoing back a moment later - see
        // BuildingManager's class comment on the echo window), same as the flag/ownership visuals.
        RefreshRingView();

        if (!PhotonNetwork.IsMasterClient) return;

        // A player who disconnected while standing in the ring leaves a destroyed reference
        // behind: OnTriggerExit cannot fire for an object that no longer exists. Every consumer
        // below reads p.teamID, so one stale entry throws a MissingReferenceException every frame
        // and the capture system stops working for the rest of the match. Unity's == treats a
        // destroyed object as null, so this catches both the destroyed and the disconnected case.
        // It is a leave like any other: if the capturing team is now gone, the capture ends, or
        // another team standing here could never start one without stepping out and back in.
        if (playersInZone.RemoveAll(p => p == null) > 0)
            EndCaptureIfCapturersLeft();

        // A player who dies in the ring never leaves it either: death switches their collider off,
        // which fires no OnTriggerExit. Measured 2026-09-16, two clients: a killed attacker stayed
        // listed, drained the zone to neutral while dead, then captured it while standing in their
        // own capital after respawning. So dying counts as leaving. A player who respawns inside a
        // zone is listed again, because switching their collider back on fires OnTriggerEnter.
        for (int i = playersInZone.Count - 1; i >= 0; i--)
        {
            PlayerTeam player = playersInZone[i];
            if (player.TryGetComponent(out PlayerLifecycle lifecycle) && !lifecycle.IsAlive)
            {
                RemoveFromZone(player);
                Debug.Log($"[BuildingCapture] tower {buildingID}: a team {player.teamID} player died here - no longer counted.");
            }
        }

        if (isCaptured)
            HandleCapturedState(); // handles recapture decay if an enemy is present
        else if (!isOnCooldown)
            CalculateCaptureProgress();

        // Runs after the state above settles for this frame, so it always publishes THIS frame's
        // real state - including the "just neutralised, now on cooldown" and "just completed, now
        // idle" transitions, which the old early-returns above would otherwise skip on the very
        // frame that matters.
        PublishProgressIfNeeded();
    }

    /// <summary>Every client, every frame: draws this zone's ring from replicated state only (capture progress, the
    /// owner, under attack), so a late joiner sees exactly what everyone else does.</summary>
    private void RefreshRingView()
    {
        BuildingManager manager = BuildingManager.Instance;
        if (ringView == null || manager == null)
            return;

        int owner = manager.Current != null ? manager.Current.OwnerOf(buildingID) : TerritoryMap.Neutral;
        bool underAttack = ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsUnderAttack(buildingID);
        CaptureRingState state = CaptureRingState.From(manager.CaptureProgressOf(buildingID), owner, underAttack,
                                                       PhotonNetwork.ServerTimestamp);
        ringView.Refresh(state, CameraTracking.Instance != null ? CameraTracking.Instance.Yaw : 0f);
    }

    /// <summary>Master only. Works out this zone's CaptureProgress from the same fields
    /// CalculateCaptureProgress/HandleCapturedState just updated this frame, and tells
    /// BuildingManager only when it differs from what this client last told it - see
    /// CaptureProgress.NeedsRepublishComparedTo. A capture in progress: team = the capturing team,
    /// progress01/rate scaled by CaptureSeconds (one-player-seconds, same units captureProgress is
    /// already tracked in). A decay in progress: team = the ENEMY doing the draining, rate =
    /// -1/DecaySeconds (matches UpdateDecay's own maths - see its comment). A capture or drain on
    /// hold with something banked (contested, its link under attack, a paused drain):
    /// CaptureProgress.Held, rate 0. Nothing in progress (idle, on cooldown, captured with nobody
    /// contesting it): Idle.</summary>
    private void PublishProgressIfNeeded()
    {
        int nowMs = PhotonNetwork.ServerTimestamp;
        if (nowMs == 0)
            return; // Server clock not synced yet (fetched once, asynchronously, right after
                     // connecting - PhotonNetwork.ServerTimestamp's own doc). Publishing a progress
                     // stamped at 0 here would make every client's later CaptureProgress.Evaluate()
                     // extrapolate from the wrong "since" instant for as long as that stamp stands -
                     // the same failure mode FAIL #15 found for a deployable's Age (see
                     // two-client-harness.md §12), just for a capture bar instead of a mine timer.
                     // Skip this one frame; the very next frame (the clock lands within about a
                     // frame of connecting) publishes normally.

        CaptureProgress current = ComputeCurrentProgress(nowMs);
        if (!current.NeedsRepublishComparedTo(lastPublishedProgress))
            return;

        lastPublishedProgress = current;
        BuildingManager.Instance.PublishCaptureProgress(buildingID, current);
    }

    /// <summary>Gathers this frame's inputs and asks the pure CaptureProgressPublishRule what to publish (review
    /// fix, 2026-09-17: the decision used to live here inline, untestable - reverting either Held branch to Idle
    /// still passed every test in the project). eligibleCount/enemyPresent/mayCaptureNow are only worth computing
    /// in the same case the old inline version did: a neutral capture in progress, not on cooldown and not
    /// already abandoned (EndCaptureIfCapturersLeft resets capturingID to -1 the same frame the zone empties, so
    /// this exactly mirrors the guard the inline version used to early-return Idle on).</summary>
    private CaptureProgress ComputeCurrentProgress(int nowMs)
    {
        int eligibleCount = 0;
        bool enemyPresent = false;
        bool mayCaptureNow = false;
        if (!isCaptured && !isOnCooldown && capturingID != -1 && playersInZone.Count != 0)
        {
            // Mirrors CalculateCaptureProgress's own eligibility check: only "N of my team, nobody
            // else, and still allowed to capture" actually moves the bar - otherwise
            // CalculateCaptureProgress itself is not advancing captureProgress this frame either, so the
            // bar must not claim it is. TeamMayCaptureNow gives both the same answer within a frame.
            eligibleCount = playersInZone.Count(p => p.teamID == capturingID);
            enemyPresent = playersInZone.Any(p => p.teamID != capturingID);
            mayCaptureNow = TeamMayCaptureNow(capturingID);
        }

        return CaptureProgressPublishRule.Decide(isCaptured, isDecaying, isDrainPaused, CaptureSeconds, DecaySeconds,
            isOnCooldown, capturingID, eligibleCount, enemyPresent, mayCaptureNow, captureProgress, nowMs);
    }

    /// <summary>Forces this tower to tell the room its current capture progress right now,
    /// bypassing the NeedsRepublishComparedTo gate. Called once per tower by
    /// BuildingManager.OnMasterClientSwitched when THIS client becomes the new master - see that
    /// method's own comment for why the ordinary gate cannot be trusted on a master's first
    /// frame.</summary>
    public void RepublishProgressNow()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        int nowMs = PhotonNetwork.ServerTimestamp;
        if (nowMs == 0)
            return; // Same clock guard as PublishProgressIfNeeded - a master promotion can in
                     // principle land before this client's own clock has ever synced.

        lastPublishedProgress = ComputeCurrentProgress(nowMs);
        BuildingManager.Instance.PublishCaptureProgress(buildingID, lastPublishedProgress);
    }

    // Cached so the per-frame capture check below doesn't allocate a new delegate for every tower every frame.
    private static readonly System.Func<int, bool> ZoneUnderAttack =
        zone => ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsUnderAttack(zone);

    // One answer per team per frame. "Under attack" ends on the server clock, which keeps ticking during a frame, so
    // asking twice (CalculateCaptureProgress, then ComputeCurrentProgress for the bar) could straddle the end of the
    // linger and let the bar claim a capture the tick didn't make.
    private int mayCaptureFrame = -1;
    private int mayCaptureTeam = -1;
    private bool mayCaptureAnswer;

    // Reused every frame by HandleCapturedState, so the drain check allocates nothing.
    private readonly List<int> teamsInZone = new List<int>();
    private System.Func<int, bool> mayCaptureNow;

    /// <summary>Master, every frame a team is actually capturing or draining this zone: may that team still capture
    /// it right now? OnTriggerEnter checks the plain adjacency rule once, on entry - deliberately not the threat-aware
    /// one, or a player who walked in while the link was under attack would never be counted and would have to step
    /// out and back in. This re-asks the threat-aware rule (Tudor, 2026-09-16: no capturing through an owned zone
    /// that is under attack), so a capture already in progress holds the moment its link comes under attack and
    /// carries on by itself once the link is safe again.</summary>
    private bool TeamMayCaptureNow(int team)
    {
        if (mayCaptureFrame == Time.frameCount && mayCaptureTeam == team)
            return mayCaptureAnswer;

        BuildingManager manager = BuildingManager.Instance;
        bool answer = true; // Nothing to judge by yet; the entry check already applied the plain rule.
        if (manager != null && manager.Map != null && manager.CurrentOwners != null)
            answer = manager.Map.MayCapture(team, buildingID, manager.CurrentOwners, ZoneUnderAttack);

        mayCaptureFrame = Time.frameCount;
        mayCaptureTeam = team;
        mayCaptureAnswer = answer;
        return answer;
    }

    void HandleCapturedState()
    {
        // Nobody standing here and no drain to stop: a quiet tower skips every check below.
        if (playersInZone.Count == 0 && !isDecaying)
            return;

        teamsInZone.Clear();
        foreach (PlayerTeam p in playersInZone)
            teamsInZone.Add(p.teamID);
        mayCaptureNow ??= TeamMayCaptureNow;

        // Who drains, and whether the drain starts, goes on, pauses or stops: see DrainRule.
        DrainRule.Decision drain = DrainRule.Decide(controllingTeam, teamsInZone, DefenderPresent(), isDecaying,
                                                    capturingID, mayCaptureNow);
        isDrainPaused = drain.Step == DrainRule.Step.Pause;

        switch (drain.Step)
        {
            case DrainRule.Step.Start:
                isDecaying = true;
                captureProgress = CaptureSeconds;
                capturingID = drain.Team;
                photonView.RPC("RPC_UpdateCapturingID", RpcTarget.MasterClient, capturingID);
                Debug.Log("[HandleCapturedState] Enemy detected. Starting recapture decay.");

                // Play recapture sound when an enemy starts recapturing
                PlayRecaptureSound();  // This was missing from your decay logic
                break;

            case DrainRule.Step.Continue:
                // Usually the same team. When another attacker takes the drain over, the bar names the team really
                // draining now.
                capturingID = drain.Team;
                break;

            case DrainRule.Step.Stop:
            case DrainRule.Step.None:
                isDecaying = false;
                break;
        }

        if (drain.Step == DrainRule.Step.Start || drain.Step == DrainRule.Step.Continue)
        {
            // Stop the capturing sound if decaying
            StopCapturingSound();
            UpdateDecay();

            if (captureProgress <= 0)
            {
                StopCapturingSound(); // Ensure sound stops if neutralized
                NeutralizeBuilding();
            }
        }
    }

    /// <summary>Master: is a player of the owner's team standing in this zone? playersInZone only lists players the
    /// territory rule let in on entry, and it refuses a zone your team already owns, so a defender who walked in after
    /// the capture was never listed and an enemy drained the zone right past them (measured 2026-09-16: drain rate
    /// unchanged with a defender inside, zone neutral 3.9 s later). Presence is tracked for every living player, so
    /// ask it too. The list still counts players who captured this zone and never left.</summary>
    private bool DefenderPresent()
    {
        foreach (PlayerTeam p in playersInZone)
            if (p.teamID == controllingTeam)
                return true;
        return ZonePresenceTracker.Instance != null
            && ZonePresenceTracker.Instance.IsTeamPresent(buildingID, controllingTeam);
    }


    void UpdateDecay()
    {
        float seconds = Mathf.Max(0.01f, DecaySeconds);
        captureProgress -= (CaptureSeconds / seconds) * Time.deltaTime;
    }

    void NeutralizeBuilding()
    {
        // Ownership replicates through the room's territory snapshot, which the master writes.
        // This runs on the master only (Update is master-gated). The flag follows the snapshot on
        // every client, so there is no flag call here.
        BuildingManager.Instance.SetNeutral(buildingID);

        controllingTeam = -1;
        isCaptured = false;
        isDecaying = false;
        isDrainPaused = false;
        PlayNeutralizationSound();
        cooldownRoutine = StartCoroutine(CooldownRoutine());
    }

    IEnumerator CooldownRoutine()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(RecaptureCooldownSeconds);
        isOnCooldown = false;
        cooldownRoutine = null;
    }

    void StopCooldown()
    {
        if (cooldownRoutine != null)
        {
            StopCoroutine(cooldownRoutine);
            cooldownRoutine = null;
        }
        isOnCooldown = false;
    }

    void CalculateCaptureProgress()
    {
        // Nobody standing here: nothing advances, so skip the per-frame checks below (the else
        // branch's sound stop is all that would have happened).
        if (playersInZone.Count == 0)
        {
            if (audioSource.isPlaying)
                StopCapturingSound();
            return;
        }

        if (capturingID == -1)
        {
            capturingID = playersInZone[0].teamID;
            captureProgress = 0;
        }

        var eligiblePlayers = playersInZone.Where(p => p.teamID == capturingID).ToList();
        var enemyPlayers = playersInZone.Any(p => p.teamID != capturingID);

        // TeamMayCaptureNow last: while the capturers' only way in is under attack, progress holds
        // where it is (the else branch only stops the sound) and carries on once the link is safe.
        if (eligiblePlayers.Any() && !enemyPlayers && TeamMayCaptureNow(capturingID))
        {
            int count = eligiblePlayers.Count;
            // N players contribute N progress-per-second - see ProgressPerPlayerPerSecond above.
            float contribution = count * ProgressPerPlayerPerSecond * Time.deltaTime;
            captureProgress += contribution;
            captureProgress = Mathf.Clamp(captureProgress, 0, CaptureSeconds);

            // Start the capturing sound only if progress is increasing
            if (!audioSource.isPlaying && captureProgress > 0 && captureProgress < CaptureSeconds)
            {
                PlayCapturingSound();
            }

            // Stop capturing sound and complete capture if progress reaches the threshold
            if (captureProgress >= CaptureSeconds)
            {
                StopCapturingSound();
                CompleteCapture(capturingID);
            }
        }
        else
        {


            // Stop the capturing sound if no eligible players are capturing
            if (audioSource.isPlaying)
            {
                StopCapturingSound();
            }
        }
    }

    void PlayCapturingSound()
    {
        // Play sound if it's not already playing, and only if the capture is in progress
        if (audioSource && capturingSound && !audioSource.isPlaying && captureProgress > 0 && captureProgress < CaptureSeconds)
        {
            photonView.RPC("RPC_PlayCaptureSound", RpcTarget.All);
        }
    }

    void StopCapturingSound()
    {
        // Stop the sound if it is currently playing and matches the capturing sound
        if (audioSource && audioSource.isPlaying && audioSource.clip == capturingSound)
        {
            photonView.RPC("RPC_StopCapturingSound", RpcTarget.All);
        }
    }
    void PlayCapturedSound()
    {
        photonView.RPC("RPC_PlayCapturedSound", RpcTarget.All);
    }
    void PlayNeutralizationSound()
    {
        if (audioSource && capturedSound)  // You can use a unique neutralization sound if needed
        {
            photonView.RPC("RPC_PlayNeutralizationSound", RpcTarget.All);
            Debug.Log("[PlayNeutralizationSound] Played neutralization sound.");
        }
    }
    void PlayRecaptureSound()
    {
        if (audioSource && capturedSound)  // You can use a unique neutralization sound if needed
        {
            photonView.RPC("RPC_PlayRecaptureSound", RpcTarget.All);
            Debug.Log("[playRecaptureSound] Played Recapture sound.");
        }
    }


    void CompleteCapture(int capturingTeam)
    {
        controllingTeam = capturingTeam;
        isCaptured = true;

        // The master's own fields above keep its capture logic going at once; everyone else
        // (and this client's TowerDictionary and flag) follows when the room sends the snapshot
        // back. The bounty PAYOUT (Task 2.4, BountyRule.PayoutOnCapture) is computed inside
        // SetCaptured itself, from the same write basis that write builds on - see its own comment
        // for why. Only this zone's tier numbers need passing in here.
        int tierBounty = territoryConfig != null ? territoryConfig.ForTier(tier).captureBounty : 0;
        int holdMs = territoryConfig != null ? (int)(territoryConfig.BountyHoldSeconds * 1000f) : 0;
        BuildingManager.Instance.SetCaptured(buildingID, capturingTeam, tierBounty, holdMs);

        Debug.Log($"[BuildingCapture] Building captured by team {capturingTeam}!");
        photonView.RPC("RPC_CompleteCapture", RpcTarget.All, controllingTeam);

        // Stop capturing sound and play captured sound
        StopCapturingSound();
        PlayCapturedSound();
    }

    /// Brings this tower's master-side fields in line with the replicated owner. Every client
    /// keeps them current, so whichever client becomes master next starts from the real state
    /// rather than from the scene's starting values.
    ///
    /// Does nothing when the fields already agree - which is always the case on the master that
    /// made the change - so its recapture cooldown and any decay keep running.
    public void SyncFromReplicated(int owner)
    {
        bool captured = owner >= 0;
        if (captured == isCaptured && (!captured || controllingTeam == owner))
            return;

        ResetToOwner(owner);
    }

    /// Called on every client when the master client changes. Who is standing in the zone, the
    /// capture progress, decay and cooldown were only ever tracked on the old master, so start
    /// again from the replicated owner and report this client's own player again if it is still
    /// standing here - otherwise the new master would not count it until it stepped out and back.
    /// A capture that was part-way through restarts from zero (accepted: plan Task 2.1b).
    public void OnMasterClientChanged(int owner)
    {
        playersInZone.Clear();
        ResetToOwner(owner);

        if (localPlayerViewIdInZone == 0)
            return;

        PhotonView view = PhotonView.Find(localPlayerViewIdInZone);
        PlayerTeam player = view != null ? view.GetComponent<PlayerTeam>() : null;
        // A player who died here and respawned somewhere else never left as far as the trigger
        // knows (see Update), so only report a body that really is still inside.
        if (player == null || !view.IsMine || !StillInside(player))
        {
            localPlayerViewIdInZone = 0;
            return;
        }

        // Same two calls, same order, as a normal entry in OnTriggerEnter.
        photonView.RPC("RPC_UpdateCapturingID", RpcTarget.MasterClient, player.teamID);
        photonView.RPC("RPC_AddToZone", RpcTarget.MasterClient, localPlayerViewIdInZone);
    }

    /// Whether the player's body still overlaps this tower's capture trigger - the same test the
    /// trigger itself makes. A dead body's collider is off, so it never does.
    private bool StillInside(PlayerTeam player)
    {
        var zone = GetComponent<SphereCollider>();
        var body = player.GetComponent<CapsuleCollider>();
        return zone != null && body != null && body.enabled
            && Physics.ComputePenetration(zone, zone.transform.position, zone.transform.rotation,
                                          body, body.transform.position, body.transform.rotation, out _, out _);
    }

    private void ResetToOwner(int owner)
    {
        bool captured = owner >= 0;
        controllingTeam = captured ? owner : -1;
        isCaptured = captured;
        capturingID = captured ? owner : -1;
        captureProgress = captured ? CaptureSeconds : 0f;
        isDecaying = false;
        isDrainPaused = false;
        StopCooldown();
    }

    // Sound only. The flag is no longer set here: it follows replicated ownership via
    // BuildingManager, so a late joiner gets the right colour without needing this call.
    [PunRPC]
    void RPC_CompleteCapture(int teamID)
    {
        if (audioSource && capturedSound)
            audioSource.PlayOneShot(capturedSound);
    }

    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponent<PlayerTeam>();
        if (!player) return;

        // Spawn points sit inside capitals, so a player can overlap a capture zone in the brief
        // window before their team property has arrived. Registering them while their team reads
        // as unknown would make them look like an enemy in their own capital and start a decay.
        // They register normally on their next entry.
        if (!player.HasTeam)
        {
            Debug.Log($"[TEAM] tower {buildingID}: ignored a player whose team is not known yet.");
            return;
        }

        BuildingManager manager = BuildingManager.Instance;
        if (manager == null || manager.Map == null) return;

        // Until this client has read the room's territory snapshot nobody here knows who owns
        // what, so refuse rather than guess. It only happens in the moment after joining; the
        // player registers normally on their next entry.
        if (manager.Current == null) return;

        // The one territory rule (TerritoryMap, tested in edit mode): not a zone you already own,
        // and next to one you do - except your own capital, which is always capturable. Reads the
        // replicated owners rather than controllingTeam, which is only correct on the master.
        if (!manager.Map.MayCapture(player.teamID, buildingID, manager.Current.OwnersByZone()))
            return;

        if (capturingID == -1)
            capturingID = player.teamID;

        // No immediate reset if an enemy enters; recapture decay is handled in HandleCapturedState.
        photonView.RPC("RPC_UpdateCapturingID", RpcTarget.MasterClient, player.teamID);

        if (player.photonView.IsMine)
        {
            Debug.Log($"[BuildingCapture] Team {player.teamID} entered tower {buildingID} (capturingID {capturingID}).");
            localPlayerViewIdInZone = player.photonView.ViewID;
            photonView.RPC("RPC_AddToZone", RpcTarget.MasterClient, player.photonView.ViewID);
        }
    }

    void OnTriggerExit(Collider other)
    {
        var player = other.GetComponent<PlayerTeam>();
        if (player && player.photonView.IsMine)
        {
            if (localPlayerViewIdInZone == player.photonView.ViewID)
                localPlayerViewIdInZone = 0;
            photonView.RPC("RPC_RemoveFromZone", RpcTarget.MasterClient, player.photonView.ViewID);
            /*if (player.teamID == capturingID)
            {
                Debug.Log($"[OnTriggerExit] Player from team {player.teamID} left zone (matched capturingID).");
            }
            else
            {
                Debug.Log($"[OnTriggerExit] Player from team {player.teamID} left zone (ignored, as capturingID is {capturingID}).");
            }*/
        }
    }

    [PunRPC]
    void RPC_UpdateCapturingID(int teamID)
    {
        if (capturingID == -1)
        {
            capturingID = teamID;
            Debug.Log($"[RPC_UpdateCapturingID] CapturingID was unset. Now set to player's teamID: {capturingID}");
        }
    }

    [PunRPC]
    void RPC_AddToZone(int viewID)
    {
        Debug.Log($"[RPC_AddToZone] Inside Function");
        var pv = PhotonView.Find(viewID);
        if (pv && pv.GetComponent<PlayerTeam>() is PlayerTeam pt)
        {
            if (!playersInZone.Contains(pt))
            {
                playersInZone.Add(pt);
                Debug.Log($"[RPC_AddToZone] Added player (Team {pt.teamID}) to zone.");
                if (!isCaptured && audioSource && !audioSource.isPlaying)
                {
                    photonView.RPC("RPC_PlayCaptureSound", RpcTarget.All);
                }
            }
            else
            {
                Debug.LogWarning("[RPC_AddToZone] Player already in zone!");
            }
        }
        else
        {
            Debug.LogWarning("[RPC_AddToZone] PlayerTeam component not found!");
        }
    }

    [PunRPC]
    void RPC_RemoveFromZone(int viewID)
    {
        var pt = PhotonView.Find(viewID)?.GetComponent<PlayerTeam>();
        if (pt && playersInZone.Contains(pt))
        {
            RemoveFromZone(pt);
            Debug.Log($"[RPC_RemoveFromZone] Removed player (Team {pt.teamID}) from zone.");
        }

        // Nothing to report otherwise: every exit is sent here, including players the territory
        // rule never let register (walking through a zone you may not capture yet), so "not in
        // the zone" is the normal case for them, not a fault.
    }

    /// Master: a listed player has left the zone - walked out (RPC_RemoveFromZone) or died (Update).
    private void RemoveFromZone(PlayerTeam pt)
    {
        playersInZone.Remove(pt);
        EndCaptureIfCapturersLeft();
    }

    /// Master, after anyone leaves the zone (walked out, died or disconnected).
    private void EndCaptureIfCapturersLeft()
    {
        // Only a neutral capture ends here. An owned zone's drain is left to HandleCapturedState: resetting it
        // on a leave wiped a running drain in one frame (see DrainRule.LeavingEndsCapture).
        if (DrainRule.LeavingEndsCapture(isCaptured, playersInZone.Any(p => p.teamID == capturingID)))
        {
            capturingID = -1;
            captureProgress = 0;
        }
    }

    [PunRPC]
    void RPC_PlayCaptureSound()
    {
        if (audioSource && capturingSound && !audioSource.isPlaying)
        {
            audioSource.clip = capturingSound;
            audioSource.loop = true;
            audioSource.Play();
            Debug.Log("[RPC_PlayCaptureSound] Playing capturing sound.");
        }
    }

    [PunRPC]
    void RPC_StopCapturingSound()
    {
        if (audioSource && audioSource.isPlaying && audioSource.clip == capturingSound)
        {
            audioSource.Stop();
            audioSource.loop = false; // Ensure the loop is disabled
            Debug.Log("[RPC_StopCapturingSound] Stopped capturing sound.");
        }
    }

    [PunRPC]
    void RPC_PlayCapturedSound()
    {
        // Play the captured sound on all clients
        if (audioSource && capturedSound)
        {
            audioSource.PlayOneShot(capturedSound);
            Debug.Log("[RPC_PlayCapturedSound] Played captured sound.");
        }
    }

    [PunRPC]
    void RPC_PlayRecaptureSound()
    {
        if (audioSource && capturingSound)
        {
            audioSource.clip = capturingSound;
            audioSource.loop = true;
            audioSource.Play();
            Debug.Log("[RPC_PlayRecaptureSound] Playing recapture sound.");
        }
    }
    [PunRPC]
    void RPC_PlayNeutralizationSound()
    {
        if (audioSource && capturedSound)
        {
            audioSource.PlayOneShot(capturedSound);
            Debug.Log("[RPC_PlayNeutralizationSound] Played neutralization sound.");
        }
    }


    /// Sets the flag to match replicated ownership. Called on every client when BuildingManager
    /// applies the room's territory snapshot, so the flag always follows the state rather than
    /// arriving as its own message that a late joiner never receives.
    /// Takes 'captured' separately because the dictionary keeps the previous owner's team id
    /// after a neutralise.
    public void ApplyOwnerVisual(bool captured, int teamID)
    {
        if (!flagRenderer)
            return;

        flagRenderer.material = GetTeamMaterial(captured ? teamID : -1);
    }

    Material GetTeamMaterial(int teamID)
    {
        return teamID switch
        {
            0 => team0Material,
            1 => team1Material,
            2 => team2Material,
            _ => neutralMaterial
        };
    }
    public bool IsCapturedByTeam(int teamID)
    {
        return isCaptured && controllingTeam == teamID;
    }

    /// This tower's own ground-truth fraction (captureProgress / CaptureSeconds) - only meaningful
    /// on whichever client is currently master, the only one that simulates it. Diagnostic only,
    /// for Task 2.1d's own verification: lets a two-client check compare a remote client's
    /// extrapolated capture ring fill directly against the number it is supposed to track,
    /// instead of reading the private captureProgress field through reflection.
    public float CaptureProgressFraction => CaptureSeconds > 0f ? Mathf.Clamp01(captureProgress / CaptureSeconds) : 0f;

    /// <summary>Task T4: how many players are currently listed inside this zone (any team) - the
    /// `capture` telemetry event's own "players" field, read through BuildingManager.PlayersInZone.
    /// Meaningful on the master only (playersInZone is only ever populated there); reads as
    /// whatever count a remote copy's own never-updated list happens to hold otherwise (always 0,
    /// since only RPC_AddToZone/RemoveFromZone touch it and those run master-side).</summary>
    public int PlayersInZoneCount => playersInZone.Count;

}
