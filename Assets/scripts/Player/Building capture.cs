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
    public float captureRadius = 10f;

    [Header("Territory")]
    [Tooltip("1 = Capital, 2 = Transition, 3 = Flanking, 4 = Centre. Decides capture time, income, bounty and " +
             "regen from the Territory Config.")]
    [Range(1, 4)] public int tier = 2;

    [Tooltip("Shared per-tier numbers. Every tower should point at the same asset.")]
    public TerritoryConfig territoryConfig;

    [Header("UI")]
    [Tooltip("Colours and world-space bar sprite for the capture progress bar shown above this " +
             "tower (Task 2.1d) - every tower should point at the same asset, same as Territory Config.")]
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
    private bool isOnCooldown = false;
    private Coroutine cooldownRoutine;

    private List<PlayerTeam> playersInZone = new List<PlayerTeam>();

    // The world-space bar over this tower (Task 2.1d), built in code in Start so every tower gets
    // one - see CaptureProgressView's own class comment. Null if theme is unassigned.
    private CaptureProgressView progressView;

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
            Debug.LogError($"[BuildingCapture] Tower {buildingID} has no UI Theme assigned - no capture progress bar will be shown.", this);
        else
            progressView = CaptureProgressView.Create(transform, theme);

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
            collider.radius = captureRadius;
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
        // Runs on EVERY client, master or not - the bar is something everyone watches, not
        // something only the master simulates. Reads whatever BuildingManager last decoded from
        // the room (possibly still this client's own write, echoing back a moment later - see
        // BuildingManager's class comment on the echo window), same as the flag/ownership visuals.
        RefreshProgressView();

        if (!PhotonNetwork.IsMasterClient) return;

        // A player who died or disconnected while standing in the ring leaves a destroyed
        // reference behind: OnTriggerExit cannot fire for an object that no longer exists.
        // Every consumer below reads p.teamID, so one stale entry throws a
        // MissingReferenceException every frame and the capture system stops working for the
        // rest of the match. Unity's == treats a destroyed object as null, so this catches both
        // the destroyed and the disconnected case.
        playersInZone.RemoveAll(p => p == null);

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

    private void RefreshProgressView()
    {
        if (progressView == null || BuildingManager.Instance == null)
            return;

        progressView.Refresh(BuildingManager.Instance.CaptureProgressOf(buildingID));
    }

    /// <summary>Master only. Works out this zone's CaptureProgress from the same fields
    /// CalculateCaptureProgress/HandleCapturedState just updated this frame, and tells
    /// BuildingManager only when it differs from what this client last told it - see
    /// CaptureProgress.NeedsRepublishComparedTo. A capture in progress: team = the capturing team,
    /// progress01/rate scaled by CaptureSeconds (one-player-seconds, same units captureProgress is
    /// already tracked in). A decay in progress: team = the ENEMY doing the draining, rate =
    /// -1/DecaySeconds (matches UpdateDecay's own maths - see its comment). Anything else (idle,
    /// on cooldown, captured with nobody contesting it): Idle, which hides the bar.</summary>
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

    private CaptureProgress ComputeCurrentProgress(int nowMs)
    {
        float captureSeconds = CaptureSeconds;

        if (isCaptured)
        {
            if (!isDecaying || captureSeconds <= 0f)
                return CaptureProgress.Idle;

            float decayProgress01 = captureProgress / captureSeconds;
            float decayRate = DecaySeconds > 0f ? -1f / DecaySeconds : 0f;
            return new CaptureProgress(capturingID, decayProgress01, decayRate, nowMs);
        }

        if (isOnCooldown || capturingID == -1 || captureSeconds <= 0f)
            return CaptureProgress.Idle;

        // Mirrors CalculateCaptureProgress's own eligibility check: only "N of my team, nobody
        // else" actually moves the bar - anyone else present means CalculateCaptureProgress itself
        // is not advancing captureProgress this frame either, so the bar must not claim it is.
        var eligiblePlayers = playersInZone.Where(p => p.teamID == capturingID).ToList();
        bool enemyPresent = playersInZone.Any(p => p.teamID != capturingID);
        if (!eligiblePlayers.Any() || enemyPresent)
            return CaptureProgress.Idle;

        float progress01 = captureProgress / captureSeconds;
        float rate = eligiblePlayers.Count / captureSeconds;
        return new CaptureProgress(capturingID, progress01, rate, nowMs);
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


    // NEW: Modified to handle recapture decay if enemy enters
    void HandleCapturedState()
    {
        bool enemyPresent = playersInZone.Any(p => p.teamID != controllingTeam);
        bool teamMemberPresent = playersInZone.Any(p => p.teamID == controllingTeam);

        if (enemyPresent && !teamMemberPresent)
        {
            if (!isDecaying)
            {
                isDecaying = true;
                captureProgress = CaptureSeconds;
                capturingID = playersInZone.First(p => p.teamID != controllingTeam).teamID;
                photonView.RPC("RPC_UpdateCapturingID", RpcTarget.MasterClient, capturingID);
                Debug.Log("[HandleCapturedState] Enemy detected. Starting recapture decay.");

                // Play recapture sound when an enemy starts recapturing
                PlayRecaptureSound();  // This was missing from your decay logic
            }

            // Stop the capturing sound if decaying
            StopCapturingSound();
        }
        else
        {
            isDecaying = false;
        }

        if (isDecaying)
        {
            UpdateDecay();

            if (captureProgress <= 0)
            {
                StopCapturingSound(); // Ensure sound stops if neutralized
                NeutralizeBuilding();
            }
        }
    }


    void StartDecay()
    {
        isDecaying = true;
        captureProgress = CaptureSeconds;
        // Decay started � progress resets to threshold.
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
        if (capturingID == -1 && playersInZone.Count != 0)
        {
            capturingID = playersInZone[0].teamID;
            captureProgress = 0;
        }

        var eligiblePlayers = playersInZone.Where(p => p.teamID == capturingID).ToList();
        var enemyPlayers = playersInZone.Any(p => p.teamID != capturingID);

        if (eligiblePlayers.Any() && !enemyPlayers)
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
        if (player == null || !view.IsMine)
        {
            localPlayerViewIdInZone = 0;
            return;
        }

        // Same two calls, same order, as a normal entry in OnTriggerEnter.
        photonView.RPC("RPC_UpdateCapturingID", RpcTarget.MasterClient, player.teamID);
        photonView.RPC("RPC_AddToZone", RpcTarget.MasterClient, localPlayerViewIdInZone);
    }

    private void ResetToOwner(int owner)
    {
        bool captured = owner >= 0;
        controllingTeam = captured ? owner : -1;
        isCaptured = captured;
        capturingID = captured ? owner : -1;
        captureProgress = captured ? CaptureSeconds : 0f;
        isDecaying = false;
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
            playersInZone.Remove(pt);

            if (!playersInZone.Any(p => p.teamID == capturingID))
            {
                capturingID = -1;
                captureProgress = 0;
            }

            Debug.Log($"[RPC_RemoveFromZone] Removed player (Team {pt.teamID}) from zone.");
        }

        // Nothing to report otherwise: every exit is sent here, including players the territory
        // rule never let register (walking through a zone you may not capture yet), so "not in
        // the zone" is the normal case for them, not a fault.
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
    /// extrapolated CaptureProgressView fill directly against the number it is supposed to track,
    /// instead of reading the private captureProgress field through reflection.
    public float CaptureProgressFraction => CaptureSeconds > 0f ? Mathf.Clamp01(captureProgress / CaptureSeconds) : 0f;

}
