using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.UI;
using Overpower.Weapons;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// One player's whole alive/dead/respawn cycle: reacting to a lethal hit, deciding whether that
/// death is temporary or permanent, timing the respawn wait, putting the player back on their
/// spawn point, and replicating "is this player alive" to every other client.
///
/// Split out of Multiplayer.cs (Task 0.11b) along with MatchUI (the win/lose/waiting panels) and
/// PlayerNameTag (the floating name and its team colour). Those three were the last third of a
/// 982-line class that owned everything about a player; Multiplayer.cs no longer exists.
///
/// Alive state lives in a Photon Custom Property rather than being announced by an RPC, because it
/// is state, not an event: a player joining mid-match needs to know who is currently dead, and an
/// unbuffered RPC cannot tell them. See CODING-STANDARDS.md section 5, rule 2.
///
/// Deliberately NOT IPunObservable. The player's PhotonView uses AutoFindAll, which searches
/// children too, so a second observable anywhere on this object would silently start sending an
/// extra serialization block every network tick. PlayerNetSync is the sole observable - see its
/// class comment.
/// </summary>
public class PlayerLifecycle : MonoBehaviour, IInRoomCallbacks
{
    /// The Custom Property key holding alive state - see the class comment for why it is a
    /// property and not an RPC.
    public const string AliveKey = "alive";

    /// <summary>The Custom Property key marking this player "out for the last stand" (Task 2.7
    /// review) - dead with the capital already lost, waiting for a teammate to take it back
    /// (MatchPhaseRules.IsLastStandDeath). Distinct from AliveKey: a player on an ordinary respawn
    /// countdown is not alive either, but their capital was never lost, so they must not count
    /// toward their team's last stand.</summary>
    public const string LastStandKey = "lastStand";

    [Header("Respawn")]
    [SerializeField, Tooltip("Match tuning asset. The base respawn wait, the per-death increase " +
             "and the cap all come from here, so all three respawn numbers live in one place with " +
             "everything else a designer tunes.")]
    private GameplayConfig gameplayConfig;

    [SerializeField, Tooltip("The character model, hidden while this player is dead and shown " +
             "again on respawn. Must be the parent of the visible meshes, not the player root - " +
             "disabling the root would switch this whole component off with it.")]
    private GameObject playerMesh;

    [SerializeField, Tooltip("Colours and text for the capital-under-attack respawn note/toast (Tudor, " +
             "2026-09-16). The same theme asset PlayerHud reads for the HUD.")]
    private UiTheme theme;

    private PhotonView photonView;
    private Rigidbody rigidbody;
    private CapsuleCollider capsuleCollider;

    // Health, armor and the damage funnel live on PlayerHealth; this class reacts to its Died
    // event instead of computing health itself. See PlayerHealth.cs.
    private PlayerHealth playerHealth;

    // Movement, remote-player interpolation and the kill-height safety net live on PlayerMotor;
    // this class only freezes it while dead and reacts to falling out of the map. See PlayerMotor.cs.
    private PlayerMotor playerMotor;

    // THE mover both respawn paths use to place the player - see TeleportToSpawnPoint below for
    // why a bare transform.position write is not safe here.
    private PlayerDisplacement playerDisplacement;

    // Shooting keeps its own Update, so ApplyAliveState switches the component itself off rather
    // than relying on gated input alone. Searched for in children as well as on the root: the
    // component this replaced (PlayerShooting) lived on a child, and a GetComponent on the root
    // returning null is how its reference sat broken for a long time.
    private WeaponFiring weaponFiring;

    // The death and respawn panels belong to MatchUI; this class tells it what happened rather
    // than holding panel references of its own. See MatchUI.cs.
    private MatchUI matchUI;

    // The HUD toast shown after respawning at T2 because the capital was under attack. Fetched the
    // same way matchUI is - a sibling component on this same player root.
    private PlayerHud playerHud;

    private bool death = false;
    private bool respawnStarted = false;
    private int deathCount = 0;

    // Caches the last value shown on the respawn note so UpdateRespawnNote only touches MatchUI's
    // text when the under-attack state actually flips, not every frame it is polled.
    private bool respawnNoteShowing = false;

    // Mirrors the replicated alive state so input and physics can be gated on it locally.
    private bool isAlive = true;

    /// <summary>False from the moment a lethal hit lands until the respawn completes. Gate any
    /// ability, dash or input on this: without that gate a player could act during the respawn
    /// wait and, in the dash's case, reappear where they died instead of at their base.</summary>
    public bool IsAlive => isAlive;

    /// <summary>Raised on every client whenever this player's alive state changes, with the new
    /// value. Anything holding a coroutine that moves the player must subscribe and cancel on
    /// false - see the note in ApplyAliveState.</summary>
    public event System.Action<bool> AliveChanged;

    /// <summary>Task T3 (telemetry): whether the respawn that most recently ran (RespawnPlayer)
    /// landed this player at the capital-under-attack spawn instead of the normal one - the same
    /// bool ChooseSpawnPoint already decides, just remembered past that method's own return so
    /// PlayerTelemetry's `respawn` line (raised from AliveChanged(true), after this field is set)
    /// can read it. Meaningless before the first respawn; false until then.</summary>
    public bool LastRespawnWasUnderAttackSpawn { get; private set; }

    void Start()
    {
        // A silent null here would make every respawn use the hardcoded fallbacks in
        // NextRespawnDelay with no way to tell from the Inspector that the asset was never wired up.
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerLifecycle] {name}: GameplayConfig is not assigned - respawn " +
                            "timing will use hardcoded fallbacks.");
        if (theme == null)
            Debug.LogError($"[PlayerLifecycle] {name}: UiTheme is not assigned - the capital-under-" +
                            "attack respawn note and toast will be skipped.");

        photonView = GetComponent<PhotonView>();
        rigidbody = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        weaponFiring = GetComponentInChildren<WeaponFiring>(true);
        matchUI = GetComponent<MatchUI>();
        playerHud = GetComponent<PlayerHud>();
        if (playerHud == null)
            Debug.LogError($"[PlayerLifecycle] {name}: no PlayerHud on this player - the capital-under-" +
                            "attack respawn toast will be skipped.");

        playerHealth = GetComponent<PlayerHealth>();
        playerHealth.Died += HandlePlayerHealthDied;
        playerMotor = GetComponent<PlayerMotor>();
        playerMotor.FellBelowKillHeight += HandleFellBelowKillHeight;
        playerMotor.LeftArena += HandleLeftArena;
        playerDisplacement = GetComponent<PlayerDisplacement>();

        // How an actor number is turned back into a PhotonView across the codebase - MatchDirector
        // (Task 2.7) uses this to find each client's own player and react locally to a phase or
        // elimination change, the same lookup ZipBoltView/MinimapView/PlayerTelemetry and others
        // already rely on for their own actor number.
        PlayerLookup.Register(photonView.OwnerActorNr, photonView);

        // Task 2.7, found live verifying this task: MatchDirector.OnJoinedRoom is a room-level Photon
        // callback that can run BEFORE this network-instantiated player object exists - reliably so
        // for a late joiner - so its very first attempt to show an already-decided match result can
        // find no local view yet and silently skip it. Catching up here, once this player object
        // (and the PlayerLookup registration just above) definitely exists, closes that gap.
        if (photonView.IsMine)
            MatchDirector.Instance?.CatchUpLocalPlayer();

        // Once per player per match. A team of -1 here means the Custom Property had not arrived
        // yet, which is the thing to look for if teams or friendly fire ever behave oddly.
        PlayerTeam pt = GetComponent<PlayerTeam>();
        Debug.Log($"[TEAM] {photonView.Owner?.NickName} team={(pt != null ? pt.teamID : PlayerTeam.NoTeam)} isMine={photonView.IsMine}");

        ApplyAliveStateFromProperties();

        if (photonView.IsMine)
        {
            // Transitional home: pointing the camera at the local player is spawn-time wiring
            // rather than lifecycle, but of the three components this file was split into it is
            // the only one that runs for the owner at spawn. A future camera-rig component should
            // claim it.
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                CameraTracking cameraFollow = mainCamera.GetComponent<CameraTracking>();
                if (cameraFollow != null)
                {
                    cameraFollow.target = transform;
                }
            }
        }
        else
        {
            rigidbody.isKinematic = false;
        }
    }

    void FixedUpdate()
    {
        // Continuously check for cathedral capture status
        CheckForCathedralCapture();
    }

    void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
    void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

    void OnDestroy()
    {
        if (playerHealth != null)
            playerHealth.Died -= HandlePlayerHealthDied;
        if (playerMotor != null)
        {
            playerMotor.FellBelowKillHeight -= HandleFellBelowKillHeight;
            playerMotor.LeftArena -= HandleLeftArena;
        }
    }

    /// Reacts to PlayerHealth reporting a lethal hit, rather than polling health every frame.
    /// PlayerHealth already latches Died to fire only once; this keeps this class's own `death`
    /// flag (read by the respawn coroutine and CheckForCathedralCapture) in step with it.
    private void HandlePlayerHealthDied(DamageInfo info)
    {
        if (!death)
            PlayerDied();

        death = true;
    }

    /// PlayerMotor only detects falling below the kill height and raises this - it does not know
    /// about isAlive, so this preserves the guard the inline check used to have (a dead player's
    /// Rigidbody is kinematic and should not normally be moving at all, but this costs nothing).
    private void HandleFellBelowKillHeight()
    {
        if (isAlive)
            ReturnToSpawn();
    }

    /// Movement step 4: PlayerMotor found this player's centre outside the arena outline - through a boundary wall, or
    /// by some way out nobody has found yet - and hands over the last spot they stood safely inside. Deliberately NOT a
    /// death, exactly like falling out of the world: leaving the arena is a level problem, not a play outcome. Whatever
    /// move is running is cancelled first, so it can't carry the body on from outside and so a knockback can't make
    /// TeleportTo refuse.
    private void HandleLeftArena(Vector3 lastSafePosition)
    {
        if (!isAlive || playerDisplacement == null)
            return;

        Vector3 outside = rigidbody != null ? rigidbody.position : transform.position;
        playerDisplacement.Cancel();
        if (playerDisplacement.TeleportTo(lastSafePosition))
            Debug.Log($"[VIS] left the arena at {outside}, returned to {lastSafePosition}");
    }

    void PlayerDied()
    {
        if (!photonView.IsMine)
            return;

        int teamID = (int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey];
        int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;

        if (!TryGetOwnCathedral(teamID, out int baseBuildingID, out TowerData cathedralTower))
            return;

        bool capitalHeld = cathedralTower.isCaptured && cathedralTower.controllingTeam == teamID;

        if (MatchPhaseRules.IsLastStandDeath(capitalHeld))
        {
            // DELIBERATE: dying with your capital already lost is a last-stand death - no respawn
            // countdown, a wait for a teammate to take the capital back instead (GDD p.20). This
            // branch was once misdiagnosed as a networking bug ("players sometimes go invisible") and
            // nearly removed. It is the design. It reads TowerDictionary, which is no longer written
            // by any RPC - it is BuildingManager's in-memory mirror of the room's replicated
            // TerritorySnapshot (BuildingManager.Apply; see that class's own comment), updated the
            // moment this client's own copy of the snapshot changes. A client whose copy of the
            // snapshot is still stale could take this branch early - that staleness is the thing to
            // fix if this ever fires when it should not, not the branch itself.
            Debug.LogWarning($"[VIS] LAST-STAND DEATH  team={teamID} base={baseBuildingID} " +
                             $"isCaptured={cathedralTower.isCaptured} controllingTeam={cathedralTower.controllingTeam}");

            SetAlive(false);
            SetLastStandOut(true);
            matchUI?.ShowWaitingPanel();
            photonView.RPC("RPC_HandleDeathMaster", RpcTarget.MasterClient, teamID, actorNumber);
            return;
        }

        if (capitalHeld && !respawnStarted)
        {
            respawnStarted = true;

            SetAlive(false);
            Debug.Log("[PlayerDied] Player Respawn Entered");
            matchUI?.SetRespawnPanelVisible(true);

            float delay = NextRespawnDelay();
            Debug.Log($"[VIS] death {deathCount}, respawning in {delay}s");

            StartCoroutine(RespawnPlayer(delay, teamID, actorNumber));
        }

        Debug.Log($"{photonView.Owner?.NickName} respawned at team {teamID} spawn point.");
    }

    /// The second way back into the match: your team lost its capital, you are sitting on the
    /// waiting panel, and a teammate recaptures it. The waiting panel's own visibility is the flag
    /// for "I am stuck waiting" - kept that way rather than adding a second piece of state that
    /// could disagree with what the player can see on screen.
    void CheckForCathedralCapture()
    {
        // Only care if we're local and waiting to be respawned.
        if (!photonView.IsMine || matchUI == null || !matchUI.IsWaitingForRespawn || !death)
            return;

        int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;

        // Get team info
        object teamIDObj;
        int teamID = -1;
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(PlayerTeam.TeamKey, out teamIDObj) && teamIDObj != null)
        {
            teamID = (int)teamIDObj;
        }
        else
        {
            Debug.LogWarning("teamID not yet set in CustomProperties.");
            return;
        }

        if (!TryGetOwnCathedral(teamID, out _, out TowerData cathedralTower))
            return;

        if (cathedralTower.isCaptured && cathedralTower.controllingTeam == teamID && !respawnStarted)
        {
            Debug.Log("[PlayerDied] Player Respawn Entered");
            matchUI?.SetRespawnPanelVisible(true);
            matchUI?.HideWaitingPanel();
            respawnStarted = true;

            // Used to hardcode 5f here and never touch deathCount, so a capital-recapture respawn
            // never scaled with repeated deaths the way a normal death does (Task 0.11a defect 3).
            // NextRespawnDelay is the one place both paths compute this now.
            float delay = NextRespawnDelay();
            Debug.Log($"[VIS] cathedral-recapture death {deathCount}, respawning in {delay}s");
            StartCoroutine(RespawnPlayer(delay, teamID, actorNumber));
        }
        else
        {
            Debug.Log("[PlayerDied] Player NOT Respawn Entered");
        }
    }

    /// Both death paths (and CheckForCathedralCapture below) need this team's capital. Task 2.7:
    /// reads it from MatchDirector.CapitalOf rather than scanning BuildingManager.CathedralBuildingIDs
    /// directly - MatchDirector is the one place that answer would change if capital adoption (a
    /// last-stand team keeping an enemy capital it captures, GDD p.20, cut for this task [C]) is ever
    /// built. Returns false and logs if the team has no capital at all, which is a map or
    /// BuildingManager setup problem, not something a player can cause.
    private bool TryGetOwnCathedral(int teamID, out int baseBuildingID, out TowerData cathedralTower)
    {
        baseBuildingID = MatchDirector.Instance != null ? MatchDirector.Instance.CapitalOf(teamID) : -1;
        cathedralTower = default;

        if (baseBuildingID < 0 || BuildingManager.Instance == null
            || !BuildingManager.Instance.TowerDictionary.TryGetValue(baseBuildingID, out cathedralTower))
        {
            Debug.LogError($"[PlayerLifecycle] No base building found for team {teamID}.");
            return false;
        }

        return true;
    }

    /// The one place the respawn wait is computed, called from both death paths (a normal death in
    /// PlayerDied and the capital-recapture death in CheckForCathedralCapture) so they cannot
    /// quietly diverge again the way they had before Task 0.11a: the recapture path used to
    /// hardcode 5f and skip deathCount entirely. Each death costs a bit more than the last, up to
    /// a cap, so repeated deaths carry a growing price without benching anyone for an unreasonable
    /// stretch - all three numbers live on GameplayConfig, not here.
    private float NextRespawnDelay()
    {
        deathCount++;

        float baseSeconds = gameplayConfig != null ? gameplayConfig.RespawnBaseSeconds : 5f;
        float perDeathSeconds = gameplayConfig != null ? gameplayConfig.RespawnPerDeathSeconds : 1f;
        float maxSeconds = gameplayConfig != null ? gameplayConfig.RespawnMaxSeconds : 10f;

        return Mathf.Min(baseSeconds + perDeathSeconds * (deathCount - 1), maxSeconds);
    }

    private IEnumerator RespawnPlayer(float delay, int teamID, int actorNumber)
    {
        // A per-frame wait rather than a single WaitForSeconds(delay), so the capital-under-attack
        // note (Tudor, 2026-09-16) can update live on the respawn panel while this wait runs. See
        // UpdateRespawnNote.
        float elapsed = 0f;
        while (elapsed < delay)
        {
            UpdateRespawnNote(teamID);
            yield return null;
            elapsed += Time.deltaTime;
        }

        Debug.Log($"{photonView.Owner?.NickName} has been revived after recapture!");

        // The decision is made now, when the timer ends, not when the player died - the attack may
        // be over by then (Tudor, 2026-09-16). Cleared here regardless of the outcome: whichever
        // spawn is chosen, the preview note no longer applies once the respawn actually happens.
        matchUI?.SetRespawnNote("");
        respawnNoteShowing = false;

        // Null-guarded inside MatchUI: an NRE here would kill the coroutine after the wait but
        // BEFORE SetAlive(true) below, leaving the player hidden on every client forever.
        matchUI?.HideWaitingPanel();

        RoomManager roomManager = FindObjectOfType<RoomManager>();
        bool atUnderAttackSpawn = false;
        Transform spawn = roomManager != null ? ChooseSpawnPoint(roomManager, teamID, out atUnderAttackSpawn) : null;
        if (spawn != null)
            TeleportToSpawnPoint(spawn.position, spawn.rotation);

        // Set before SetAlive(true) below raises AliveChanged - PlayerTelemetry's `respawn` line
        // reads this from that same event.
        LastRespawnWasUnderAttackSpawn = atUnderAttackSpawn;

        playerHealth.ResetForRespawn();

        // Layer, mesh, collider and speed are all restored by SetAlive(true) - in particular the
        // speed freeze is LIFTED by removing a multiplier, never by assigning a speed. A respawn
        // that wrote a speed value here is exactly how "you move faster after respawning" happened.
        SetAlive(true);

        // Whichever path got this player here (an ordinary respawn never set it true in the first
        // place - this is then a harmless repeat write - or a last-stand recapture), they are back in
        // the fight and no longer count toward their team's last stand.
        SetLastStandOut(false);

        // No fallback string: theme's own null already logged an error in Start, and playerHud's a
        // second one - showing wrong or missing-theme text here would just be a second symptom.
        if (atUnderAttackSpawn && theme != null)
            playerHud?.ShowToast(theme.capitalUnderAttackRespawnToast);

        // Logged because "respawning where you died" is a fix that cannot be verified from the
        // editor. Reads rigidbody.position, not transform.position: this class used to log
        // transform.position immediately after writing it, which always "looked" right even on the
        // ~1-in-10 runs where PlayerMotor.Move()'s rb.MovePosition silently reverted the write a
        // tick later (see TeleportToSpawnPoint below) - the log could never have caught its own bug.
        Debug.Log($"[VIS] respawned at {rigidbody.position} (team {teamID} " +
                  $"{(atUnderAttackSpawn ? "T2 (capital under attack)" : "capital")})");
        photonView.RPC("RPC_HandleRespawnMaster", RpcTarget.MasterClient, teamID, actorNumber);

        Debug.Log($"{photonView.Owner?.NickName} fully respawned at base after cathedral recapture.");

        death = false;
        respawnStarted = false;

        matchUI?.SetRespawnPanelVisible(false);
    }

    /// Puts a player who fell out of the world back on their team's spawn point. Deliberately NOT a
    /// death: falling is a level problem, not a play outcome, so it must not feed the respawn timer
    /// or the elimination count.
    public void ReturnToSpawn() =>
        MoveToSpawnPoint($"fell below y={playerMotor.KillHeight}");

    /// <summary>MatchDirector's own second caller (Task 2.7 review): on the three-to-two team
    /// transition, every living player's own client sends them home to their team's spawn point -
    /// which today is also where "return to your capital" lands (RoomManager.teamSpawnPoints) - the
    /// same move ReturnToSpawn makes, just with a log line that does not claim they fell.</summary>
    public void ReturnToSpawnForPhaseChange() =>
        MoveToSpawnPoint("sent home for the two-team phase change");

    private void MoveToSpawnPoint(string logReason)
    {
        PlayerTeam pt = GetComponent<PlayerTeam>();
        RoomManager roomManager = FindObjectOfType<RoomManager>();

        if (pt == null || !pt.HasTeam || roomManager == null
            || roomManager.teamSpawnPoints == null
            || pt.teamID >= roomManager.teamSpawnPoints.Length)
        {
            return;
        }

        TeleportToSpawnPoint(roomManager.teamSpawnPoints[pt.teamID].position,
                              roomManager.teamSpawnPoints[pt.teamID].rotation);

        Debug.Log($"[VIS] {logReason}, returned to spawn at {rigidbody.position}");
    }

    /// Tudor, 2026-09-16: while your capital is under attack (an enemy standing in it, or who just left - see
    /// ZonePresenceTracker) you come back at your capital's Tier 2 zone instead, whoever owns it, rather than
    /// straight into the fight. Decided when the timer ends, not when you died, because the attack may be over by
    /// then.
    private Transform ChooseSpawnPoint(RoomManager roomManager, int teamID, out bool atUnderAttackSpawn)
    {
        atUnderAttackSpawn = false;
        Transform normal = roomManager.teamSpawnPoints != null && roomManager.teamSpawnPoints.Length > teamID
            ? roomManager.teamSpawnPoints[teamID] : null;

        BuildingManager manager = BuildingManager.Instance;
        ZonePresenceTracker presence = ZonePresenceTracker.Instance;
        if (manager == null || manager.Map == null || presence == null)
            return normal;

        int capital = manager.Map.CapitalOf(teamID);
        // CapitalOf is the STATIC capital zone id; IsUnderAttack judges the CURRENT owner of that zone id,
        // which is not necessarily this team any more (B3 review, 2026-09-16: the capital can flip - a real
        // capture, not merely an attack - while this player is on the respawn wait). Trust the presence check
        // only while this team still owns it, or a team that just lost its capital outright could still be
        // routed to a T2 spawn on the strength of an attack against a zone that is no longer theirs.
        if (capital < 0 || manager.Current == null || manager.Current.OwnerOf(capital) != teamID || !presence.IsUnderAttack(capital))
            return normal;

        Transform[] underAttack = roomManager.capitalUnderAttackSpawnPoints;
        if (underAttack == null || underAttack.Length <= teamID || underAttack[teamID] == null)
        {
            Debug.LogWarning($"[PlayerLifecycle] team {teamID}'s capital is under attack but RoomManager has no Capital Under Attack Spawn Point for it - respawning at the capital.");
            return normal;
        }

        atUnderAttackSpawn = true;
        return underAttack[teamID];
    }

    /// <summary>Refreshes the "your capital is under attack" line on the respawn panel (Tudor, 2026-09-16),
    /// polled every frame of RespawnPlayer's own wait (respawnPanel is up: an ordinary death, capital still
    /// owned but possibly under attack). NOT also called from CheckForCathedralCapture's waitingPanel poll
    /// (B3 review, 2026-09-16, fix 1): MatchUI parents the note under respawnPanel, which is inactive during
    /// that lost-capital wait, so writing it there was invisible and only cost a stale comment. Only writes
    /// MatchUI's text when the bool actually flips, and only trusts IsUnderAttack while this team still owns
    /// the capital (see ChooseSpawnPoint's own comment on the same race).</summary>
    private void UpdateRespawnNote(int teamID)
    {
        if (matchUI == null || theme == null)
            return;

        BuildingManager manager = BuildingManager.Instance;
        ZonePresenceTracker presence = ZonePresenceTracker.Instance;
        int capital = manager != null && manager.Map != null ? manager.Map.CapitalOf(teamID) : -1;
        bool underAttack = capital >= 0 && manager.Current != null && manager.Current.OwnerOf(capital) == teamID
            && presence != null && presence.IsUnderAttack(capital);

        if (underAttack == respawnNoteShowing)
            return;

        respawnNoteShowing = underAttack;
        matchUI.SetRespawnNote(underAttack ? theme.capitalUnderAttackRespawnNote : "");
    }

    /// <summary>
    /// The only way either respawn path (a normal respawn or falling below the kill height) is
    /// allowed to move this player. A bare transform.position write here is not safe: PlayerMotor
    /// .Move() calls rb.MovePosition(rb.position + ...) every FixedUpdate the player owns
    /// themselves, and rb.position is the physics engine's own cached position, not a mirror of
    /// transform.position - Unity only syncs the two on its own schedule. Writing transform.position
    /// directly leaves rb.position stale until that sync catches up, and if Move() reads rb.position
    /// before it does, it re-asserts the STALE (pre-teleport) position as the Rigidbody's new target
    /// for that physics step, permanently overwriting the intended move. Measured: 1 of 10 single-
    /// client respawns reproduced this exact way (deathCount 1, HEAD 88cc260) - the player stayed at
    /// the death spot at +0.5s and +2.0s despite the "[VIS] respawned at spawn" log line. See
    /// two-client-harness.md item 15, which found the identical mechanism on an already-alive player.
    ///
    /// PlayerDisplacement.TeleportTo writes rb.position directly (no transform-sync race possible)
    /// and zeroes residual velocity - it is documented as the one mover that actually sticks. Works
    /// regardless of whether the Rigidbody is currently kinematic (mid-death) or dynamic (mid-fall):
    /// the position setter does not care about the kinematic flag.
    /// </summary>
    void TeleportToSpawnPoint(Vector3 position, Quaternion rotation)
    {
        if (playerDisplacement == null || !playerDisplacement.TeleportTo(position))
        {
            // Defensive fallback only - should not happen for the owner. TeleportTo can refuse
            // only while a Forced (knockback) move is running, and death already cancels any move
            // in flight via AliveChanged before either caller of this method runs.
            if (rigidbody != null)
            {
                rigidbody.position = position;
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
            }
            else
            {
                transform.position = position;
            }
        }

        transform.rotation = rotation;
        if (rigidbody != null)
            rigidbody.rotation = rotation;
    }

    void SetLayerRecursively(GameObject o, int layer)
    {
        o.layer = layer;
        foreach (Transform child in o.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    // ---- alive / dead as replicated state -------------------------------------------------

    /// Owner-only. Applies the change locally straight away so dying feels instant, then
    /// publishes it so every other client -- including anyone who joins later -- agrees.
    void SetAlive(bool alive)
    {
        if (!photonView.IsMine)
            return;

        ApplyAliveState(alive);
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { AliveKey, alive } });
    }

    /// Owner-only, published the same way SetAlive is just above (Task 2.7 review) - the "out for the
    /// last stand" fact MatchDirector's own team recompute reads, set true exactly on a last-stand
    /// death and cleared the moment this player is on their way back into the match (RespawnPlayer),
    /// whichever path got them there.
    void SetLastStandOut(bool outForLastStand)
    {
        if (!photonView.IsMine)
            return;

        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { LastStandKey, outForLastStand } });
    }

    /// Everything that used to live in RPC_HandleDeath and RPC_ShowPlayer, in one place so hide
    /// and show cannot drift apart. The old pair did not: RPC_HandleDeath moved the hierarchy to
    /// the DeadPlayer layer on every client, but only the owner ever moved it back.
    void ApplyAliveState(bool alive)
    {
        isAlive = alive;

        SetLayerRecursively(gameObject, LayerMask.NameToLayer(alive ? "Default" : "DeadPlayer"));

        if (playerMesh != null)
            playerMesh.SetActive(alive);

        // A corpse used to keep its collider, so it still blocked shots and bodies until respawn.
        // Kinematic while dead as well, otherwise removing the collider just drops it through the
        // floor for the length of the respawn wait.
        if (capsuleCollider != null)
            capsuleCollider.enabled = alive;

        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.isKinematic = !alive;
            if (photonView.IsMine)
            {
                // Keyed so this can never step on some other system's own multiplier (a sprint
                // ability, a slow debuff), and removed rather than overwritten on respawn. Used to
                // assign a raw speed field directly, which PlayerMotor stopped reading once it
                // moved to this stack - that silently turned "freeze on death" into a no-op and a
                // corpse could still slide around (Task 0.11a defect 1).
                if (alive)
                    playerMotor.RemoveSpeedMultiplier(this);
                else
                    playerMotor.AddSpeedMultiplier(this, 0f);
            }
        }

        // Shooting runs its own Update, so gating input in this class does not stop it.
        if (weaponFiring != null)
            weaponFiring.enabled = alive;

        // A dash already in flight kept moving the body after death and could land it somewhere
        // other than the spawn point, so the running coroutine was cancelled here. The four
        // Space-bound dash/AoE scripts were deleted in Task 0.11b and their replacement does not
        // exist yet: AliveChanged is the hook the new abilities must use to cancel anything in
        // flight, and IsAlive is the gate that stops one starting while dead.
        AliveChanged?.Invoke(alive);

        Debug.Log($"[VIS] alive={alive}  owner={photonView.Owner?.NickName}  isMine={photonView.IsMine}");
    }

    /// Reads the current value, for a client that arrived after the change was published.
    void ApplyAliveStateFromProperties()
    {
        if (photonView.Owner == null)
            return;

        if (photonView.Owner.CustomProperties.TryGetValue(AliveKey, out object raw) && raw is bool alive)
            ApplyAliveState(alive);
    }

    public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        // OnEnable registers this callback before Start assigns photonView, so a property update
        // arriving in that window would dereference null.
        if (photonView == null || photonView.Owner == null)
            return;

        if (targetPlayer != photonView.Owner)
            return;

        // The owner already applied this in SetAlive before publishing, so reacting again would
        // just repeat the work and log every visibility change twice for the local player.
        if (photonView.IsMine)
            return;

        if (changedProps.TryGetValue(AliveKey, out object raw) && raw is bool alive)
            ApplyAliveState(alive);
    }

    // Unused IInRoomCallbacks members.
    public void OnPlayerEnteredRoom(Player newPlayer) { }
    public void OnPlayerLeftRoom(Player otherPlayer) { }
    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
    public void OnMasterClientSwitched(Player newMasterClient) { }

    // ---- master-client elimination bookkeeping (retired, Task 2.7) -------------------------
    // Both RPCs below used to run ONLY on the master client, which kept the one authoritative tally
    // of who was dead, in the three static fields this task deleted (teamDeadCount, processedDeaths,
    // deadTeams) - a tally that lived on one machine's heap did not survive that machine losing
    // master, or a second match starting in the same session. Elimination and the match phase are
    // recomputed instead by MatchDirector, from replicated state (team rosters, the "alive" Player
    // Property SetAlive already publishes, and capital ownership) - see MatchDirector.MasterRecompute.
    // Both methods are kept only because PUN dispatches an RPC by its index into the committed RpcList
    // in PhotonServerSettings.asset: removing or renaming either one would mis-dispatch every RPC
    // listed after it, on any client that already shipped.

    [PunRPC]
    public void RPC_HandleRespawnMaster(int teamID, int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        Debug.Log($"[PlayerLifecycle] (Master) RPC_HandleRespawnMaster retired (Task 2.7) - actor {actorNumber} " +
                  $"team {teamID}; MatchDirector already reacted to this player's own \"alive\" Player Property.");
    }

    [PunRPC]
    public void RPC_HandleDeathMaster(int teamID, int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        Debug.Log($"[PlayerLifecycle] (Master) RPC_HandleDeathMaster retired (Task 2.7) - actor {actorNumber} " +
                  $"team {teamID}; asking MatchDirector to recompute in case this RPC beats the \"lastStand\" " +
                  "Player Property SetLastStandOut(true) already published across the wire.");
        MatchDirector.Instance?.RequestRecompute();
    }
}
