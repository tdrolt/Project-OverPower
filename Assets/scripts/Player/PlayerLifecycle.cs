using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
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

    [Header("Respawn")]
    [SerializeField, Tooltip("Match tuning asset. The base respawn wait, the per-death increase " +
             "and the cap all come from here, so all three respawn numbers live in one place with " +
             "everything else a designer tunes.")]
    private GameplayConfig gameplayConfig;

    [SerializeField, Tooltip("The character model, hidden while this player is dead and shown " +
             "again on respawn. Must be the parent of the visible meshes, not the player root - " +
             "disabling the root would switch this whole component off with it.")]
    private GameObject playerMesh;

    private PhotonView photonView;
    private Rigidbody rigidbody;
    private CapsuleCollider capsuleCollider;

    // Health, armor and the damage funnel live on PlayerHealth; this class reacts to its Died
    // event instead of computing health itself. See PlayerHealth.cs.
    private PlayerHealth playerHealth;

    // Movement, remote-player interpolation and the kill-height safety net live on PlayerMotor;
    // this class only freezes it while dead and reacts to falling out of the map. See PlayerMotor.cs.
    private PlayerMotor playerMotor;

    // Shooting runs its own Update on a child object, so gating input in this class does not stop
    // it - ApplyAliveState has to switch the component itself off. (Its reference was also broken
    // for a long time: GetComponent on the root returns null because PlayerShooting is not on the
    // root.)
    private PlayerShooting playerShooting;

    // The death and respawn panels belong to MatchUI; this class tells it what happened rather
    // than holding panel references of its own. See MatchUI.cs.
    private MatchUI matchUI;

    private bool death = false;
    private bool respawnStarted = false;
    private int deathCount = 0;

    // Mirrors the replicated alive state so input and physics can be gated on it locally.
    private bool isAlive = true;

    // Master-client-only bookkeeping of how many players on each team are currently dead, so the
    // master can tell when a whole team is out and the match is over. Static because it is one
    // tally per match, not per player, and every player object on the master client feeds the
    // same tally.
    private static Dictionary<int, int> teamDeadCount = new Dictionary<int, int>();
    private static HashSet<int> processedDeaths = new HashSet<int>();
    private static List<int> deadTeams = new List<int>();

    /// <summary>False from the moment a lethal hit lands until the respawn completes. Gate any
    /// ability, dash or input on this: without that gate a player could act during the respawn
    /// wait and, in the dash's case, reappear where they died instead of at their base.</summary>
    public bool IsAlive => isAlive;

    /// <summary>Raised on every client whenever this player's alive state changes, with the new
    /// value. Anything holding a coroutine that moves the player must subscribe and cancel on
    /// false - see the note in ApplyAliveState.</summary>
    public event System.Action<bool> AliveChanged;

    void Start()
    {
        // A silent null here would make every respawn use the hardcoded fallbacks in
        // NextRespawnDelay with no way to tell from the Inspector that the asset was never wired up.
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerLifecycle] {name}: GameplayConfig is not assigned - respawn " +
                            "timing will use hardcoded fallbacks.");

        photonView = GetComponent<PhotonView>();
        rigidbody = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        playerShooting = GetComponentInChildren<PlayerShooting>(true);
        matchUI = GetComponent<MatchUI>();

        playerHealth = GetComponent<PlayerHealth>();
        playerHealth.Died += HandlePlayerHealthDied;
        playerMotor = GetComponent<PlayerMotor>();
        playerMotor.FellBelowKillHeight += HandleFellBelowKillHeight;

        // How the master client finds a specific victim's PhotonView in RPC_HandleDeathMaster
        // below, and how BuildingManager finds the local player to show a match result.
        PlayerLookup.Register(photonView.OwnerActorNr, photonView);

        // Determine local player's team from Photon custom properties
        int localTeam = -1;
        if (PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey(PlayerTeam.TeamKey))
        {
            localTeam = (int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey];
        }

        // Initialize dead count for this team if it hasn't been set
        if (!teamDeadCount.ContainsKey(localTeam))
        {
            teamDeadCount[localTeam] = 0;
        }

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
            playerMotor.FellBelowKillHeight -= HandleFellBelowKillHeight;
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

    void PlayerDied()
    {
        if (!photonView.IsMine)
            return;

        int teamID = (int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey];
        int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;

        if (!TryGetOwnCathedral(teamID, out int baseBuildingID, out TowerData cathedralTower))
            return;

        if (!cathedralTower.isCaptured || cathedralTower.controllingTeam != teamID)
        {
            // DELIBERATE: losing your capital eliminates your team permanently - no respawn, no
            // re-show. This branch was once misdiagnosed as a networking bug ("players sometimes
            // go invisible") and nearly removed. It is the design. It does read TowerDictionary,
            // which is replicated with RpcTarget.All (not AllBuffered), so a client with a stale
            // tower state could take this branch early - that staleness is the thing to fix if
            // this ever fires when it should not, not the branch itself.
            Debug.LogWarning($"[VIS] PERMANENT DEATH  team={teamID} base={baseBuildingID} " +
                             $"isCaptured={cathedralTower.isCaptured} controllingTeam={cathedralTower.controllingTeam}");

            SetAlive(false);
            photonView.RPC("RPC_HandleDeathMaster", RpcTarget.MasterClient, teamID, actorNumber);
            return;
        }

        if (cathedralTower.isCaptured && cathedralTower.controllingTeam == teamID && !respawnStarted)
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

    /// Both death paths need this team's capital and both used to look it up with their own copy of
    /// the same loop. Returns false and logs if the team has no capital at all, which is a map or
    /// BuildingManager setup problem, not something a player can cause.
    private bool TryGetOwnCathedral(int teamID, out int baseBuildingID, out TowerData cathedralTower)
    {
        baseBuildingID = -1;
        cathedralTower = default;

        foreach (KeyValuePair<int, int> kvp in BuildingManager.Instance.CathedralBuildingIDs)
        {
            if (kvp.Value == teamID)
            {
                baseBuildingID = kvp.Key;
                break;
            }
        }

        if (baseBuildingID == -1)
        {
            Debug.LogError($"[PlayerLifecycle] No base building found for team {teamID}.");
            return false;
        }

        cathedralTower = BuildingManager.Instance.TowerDictionary[baseBuildingID];
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
        yield return new WaitForSeconds(delay);

        Debug.Log($"{photonView.Owner?.NickName} has been revived after recapture!");

        // Null-guarded inside MatchUI: an NRE here would kill the coroutine after the wait but
        // BEFORE SetAlive(true) below, leaving the player hidden on every client forever.
        matchUI?.HideWaitingPanel();

        RoomManager roomManager = FindObjectOfType<RoomManager>();
        if (roomManager != null && roomManager.teamSpawnPoints.Length > teamID)
        {
            transform.position = roomManager.teamSpawnPoints[teamID].position;
            transform.rotation = roomManager.teamSpawnPoints[teamID].rotation;
        }

        playerHealth.ResetForRespawn();

        // Layer, mesh, collider and speed are all restored by SetAlive(true) - in particular the
        // speed freeze is LIFTED by removing a multiplier, never by assigning a speed. A respawn
        // that wrote a speed value here is exactly how "you move faster after respawning" happened.
        SetAlive(true);

        // Logged because "respawning where you died" is a fix that cannot be verified from the
        // editor. If a respawn ever lands somewhere other than the base, this line says so.
        Debug.Log($"[VIS] respawned at {transform.position} (team {teamID} spawn)");
        photonView.RPC("RPC_HandleRespawnMaster", RpcTarget.MasterClient, teamID, actorNumber);

        Debug.Log($"{photonView.Owner?.NickName} fully respawned at base after cathedral recapture.");

        death = false;
        respawnStarted = false;

        matchUI?.SetRespawnPanelVisible(false);
    }

    /// Puts a player who fell out of the world back on their spawn point. Deliberately NOT a
    /// death: falling is a level problem, not a play outcome, so it should not feed the respawn
    /// timer or the elimination count.
    void ReturnToSpawn()
    {
        PlayerTeam pt = GetComponent<PlayerTeam>();
        RoomManager roomManager = FindObjectOfType<RoomManager>();

        if (pt == null || !pt.HasTeam || roomManager == null
            || roomManager.teamSpawnPoints == null
            || pt.teamID >= roomManager.teamSpawnPoints.Length)
        {
            return;
        }

        transform.position = roomManager.teamSpawnPoints[pt.teamID].position;
        transform.rotation = roomManager.teamSpawnPoints[pt.teamID].rotation;

        if (rigidbody != null)
            rigidbody.linearVelocity = Vector3.zero;

        Debug.Log($"[VIS] fell below y={playerMotor.KillHeight}, returned to spawn");
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

        // Shooting runs its own Update on the child mesh object, so gating input in this class
        // does not stop it.
        if (playerShooting != null)
            playerShooting.enabled = alive;

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

    // ---- master-client elimination bookkeeping --------------------------------------------
    // Both RPCs below run ONLY on the master client, which owns the one authoritative tally of
    // who is dead. Names are fixed: PUN sends an index into the RpcList in PhotonServerSettings,
    // so renaming either one mis-dispatches on every client that already shipped.

    [PunRPC]
    public void RPC_HandleRespawnMaster(int teamID, int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        int prev = teamDeadCount.TryGetValue(teamID, out var val) ? val : 0;
        int now = Mathf.Max(0, prev - 1);
        teamDeadCount[teamID] = now;

        processedDeaths.Remove(actorNumber);

        if (now == 0)
            deadTeams.Remove(teamID);
    }

    [PunRPC]
    public void RPC_HandleDeathMaster(int teamID, int actorNumber)
    {
        if (!processedDeaths.Add(actorNumber))
        {
            Debug.Log($"[Master] Actor {actorNumber} already processed.");
            return;
        }

        int totalTeamPlayers = 0;

        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.ContainsKey(PlayerTeam.TeamKey) &&
                (int)p.CustomProperties[PlayerTeam.TeamKey] == teamID)
            {
                totalTeamPlayers++;
            }
        }

        int previousDead = teamDeadCount.ContainsKey(teamID) ? teamDeadCount[teamID] : 0;
        int newDeadCount = previousDead + 1;
        teamDeadCount[teamID] = newDeadCount;

        int remaining = totalTeamPlayers - newDeadCount;

        Debug.Log($"[PlayerLifecycle] (Master) Team {teamID} has {remaining} player(s) remaining.");

        if (remaining <= 0)
        {
            deadTeams.Add(teamID);

            Debug.Log($"[PlayerLifecycle] Team Dead : {teamID}");

            // Each victim is told on their own PhotonView, not this one: the panels live on the
            // dead player's own object, and "local" inside an RPC body means the receiver.
            foreach (var p in PhotonNetwork.PlayerList
                    .Where(p => (int)p.CustomProperties[PlayerTeam.TeamKey] == teamID))
            {
                PhotonView victimView = PlayerLookup.GetPhotonViewFor(p.ActorNumber);
                if (victimView != null)
                {
                    victimView.RPC("RPC_ShowYouLostPanel", p, teamID);
                }
            }
        }
        else
        {
            var victimView = PlayerLookup.GetPhotonViewFor(actorNumber);
            if (victimView != null)
                victimView.RPC("RPC_ShowWaitingPanel",
                               PhotonNetwork.CurrentRoom.GetPlayer(actorNumber),
                               teamID);
        }

        Debug.Log($"[PlayerLifecycle] (Master) Dead Teams Count {deadTeams.Count}");

        HashSet<int> allTeamIDs = new HashSet<int>();

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player.CustomProperties.ContainsKey(PlayerTeam.TeamKey))
                allTeamIDs.Add((int)player.CustomProperties[PlayerTeam.TeamKey]);
        }

        List<int> remainingTeams = allTeamIDs.Where(tid => !deadTeams.Contains(tid)).ToList();

        if (remainingTeams.Count == 1)
        {
            int winningTeam = remainingTeams[0];

            Debug.Log($"[PlayerLifecycle] Team {winningTeam} has WON the match!");

            photonView.RPC("RPC_ShowYouWonPanel", RpcTarget.All, winningTeam);
        }
    }
}
