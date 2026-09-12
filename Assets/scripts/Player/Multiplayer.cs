using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Overpower.Combat;
using Overpower.Data;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class Multiplayer : MonoBehaviour, IInRoomCallbacks
{
    /// Whether this player is currently alive. Lives in Photon Player Custom Properties rather
    /// than being announced by an RPC, because it is state, not an event: a player joining
    /// mid-match needs to know who is currently dead, and an unbuffered RPC cannot tell them.
    /// See CODING-STANDARDS.md section 5, rule 2.
    public const string AliveKey = "alive";

    private Rigidbody rigidbody;

    public float fireRate = 0.75f;
    public GameObject bulletPrefab;
    public Transform bulletPosition;
    public GameObject bulletFiringEffect;    
    private float nextFire;

    public Text playerNameText; // UI Text for player name display

    public AudioClip playerShootingAudio;

    // Reference to the player mesh assigned in the Inspector.
    public GameObject playerMesh;

    // Reference to the "Waiting Panel" UI (assign via Inspector)
    public GameObject waitingPanel;

    public GameObject youWonPanel;

    public GameObject respawnPanel;

    // Reference to the "You Lost" Panel UI (assign via Inspector)
    public GameObject youLostPanel;

    private PhotonView photonView;

    // Movement, remote-player interpolation and the kill-height safety net now live on
    // PlayerMotor; this class only feeds it network targets and reacts to falling out of the
    // map. See PlayerMotor.cs.
    private PlayerMotor playerMotor;

    // References to ability scripts
    private PlayerShooting playerShooting;
    private PlayerDash playerDash;
    private AoEAbility aoeAbility;
    private PlayerDashWithBuff playerDashWithBuff;
    private PlayerDashWithProjectile playerDashWithProjectile;
    private CapsuleCollider capsuleCollider;
    // Health, armor and the damage funnel now live on PlayerHealth; this class reacts to its
    // Died event instead of computing health itself. See PlayerHealth.cs.
    private PlayerHealth playerHealth;

    private bool death = false;
    private bool respawnStarted = false;

    [Header("Respawn")]
    [Tooltip("Match tuning asset. The base respawn wait, the per-death increase and the cap all " +
             "come from here now, so all three respawn numbers live in one place with everything " +
             "else a designer tunes.")]
    [SerializeField] private GameplayConfig gameplayConfig;
    private int deathCount = 0;

    // Mirrors the replicated alive state so input and physics can be gated on it locally.
    private bool isAlive = true;
    // The dash currently running, so death can cancel it.
    private Coroutine activeDash;
    // Static dictionary to keep track of dead players per team.
    private static Dictionary<int, int> teamDeadCount = new Dictionary<int, int>();
    private static HashSet<int> processedDeaths = new HashSet<int>();
    private static List<int> deadTeams = new List<int>();

    void Start()
    {
        rigidbody = GetComponent<Rigidbody>();

        // A silent null here would make every respawn use the hardcoded fallbacks below with no
        // way to tell from the Inspector that the asset was never wired up.
        if (gameplayConfig == null)
            Debug.LogError($"[Multiplayer] {name}: GameplayConfig is not assigned - respawn " +
                            "timing will use hardcoded fallbacks.");

        photonView = GetComponent<PhotonView>();
        playerShooting = GetComponentInChildren<PlayerShooting>(true);
        playerDash = GetComponent<PlayerDash>();
        playerDashWithBuff = GetComponent<PlayerDashWithBuff>();
        playerDashWithProjectile = GetComponent<PlayerDashWithProjectile>();
        aoeAbility = GetComponent<AoEAbility>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        playerHealth = GetComponent<PlayerHealth>();
        playerHealth.Died += HandlePlayerHealthDied;
        playerMotor = GetComponent<PlayerMotor>();
        playerMotor.FellBelowKillHeight += HandleFellBelowKillHeight;

        // Set player name based on Photon owner
        playerNameText.text = photonView.Owner.NickName;

        PlayerLookup.Register(photonView.OwnerActorNr, photonView);

        // Determine local player's team from Photon custom properties
        int localTeam = -1;
        if (PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("teamID"))
        {
            localTeam = (int)PhotonNetwork.LocalPlayer.CustomProperties["teamID"];
        }

        // Initialize dead count for this team if it hasn't been set
        if (!teamDeadCount.ContainsKey(localTeam))
        {
            teamDeadCount[localTeam] = 0;
        }

        PlayerTeam pt = GetComponent<PlayerTeam>();
        UpdateNameTagColour();
        GetComponent<PlayerTeamAppearance>()?.Apply();   // no-op if the team has not arrived yet

        // Removed: a Debug.LogError fired here on every spawn purely to make the console appear
        // in development builds. DebugOverlay (F1) does that job now, and this line polluted it,
        // since the overlay captures every error.

        // Once per player per match. A team of -1 here means the Custom Property had not arrived
        // yet, which is the thing to look for if teams or friendly fire ever behave oddly.
        Debug.Log($"[TEAM] {photonView.Owner?.NickName} team={(pt != null ? pt.teamID : PlayerTeam.NoTeam)} isMine={photonView.IsMine}");

        ApplyAliveStateFromProperties();

        if (photonView.IsMine)
        {
            // Let the camera follow your player
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


    void Update()
    {
        if (!photonView.IsMine)
            return;

        // Rotation-to-cursor now runs on PlayerAim's own Update - nothing to call here.

        // No casting while dead. Without this a player could dash during the respawn wait and
        // reappear where they died instead of at their base.
        if (!isAlive)
            return;

        // Process abilities (keys updated as needed)
        if (Input.GetKeyDown(KeyCode.Space) && playerDash != null && playerDash.enabled && playerDash.CanDash())
        {
            Vector3 inputDirection = MovementInput();
            activeDash = StartCoroutine(playerDash.Dash(inputDirection));
        }
        if (Input.GetKeyDown(KeyCode.Space) && playerDashWithBuff != null && playerDashWithBuff.enabled && playerDashWithBuff.CanDash())
        {
            Vector3 inputDirection = MovementInput();
            activeDash = StartCoroutine(playerDashWithBuff.Dash(inputDirection));
        }
        if (Input.GetKeyDown(KeyCode.Space) && playerDashWithProjectile != null && playerDashWithProjectile.enabled && playerDashWithProjectile.CanDash())
        {
            Vector3 inputDirection = MovementInput();
            activeDash = StartCoroutine(playerDashWithProjectile.Dash(inputDirection));
        }
        if (Input.GetKeyDown(KeyCode.Space) && aoeAbility != null && aoeAbility.enabled)
        {
            aoeAbility.TriggerAoE();
        }

        // Shooting handled in PlayerShooting script (using left mouse)
    }

    void FixedUpdate()
    {
        // PlayerMotor now does the actual moving, remote-player interpolation and falling check;
        // this just tells it whether a dash currently owns the Rigidbody for the frame, exactly
        // as this check used to gate the old direct Move() call.
        if (photonView.IsMine)
            playerMotor.ExternalMotionControl = playerDash != null && playerDash.IsDashing();

        // Continuously check for cathedral capture status
        CheckForCathedralCapture();
    }

    /// Forwards to PlayerMotor, which now owns the actual camera-relative input calculation - kept
    /// under this name so the dash calls below (still Task 0.11+ territory) do not need to change.
    Vector3 MovementInput() => playerMotor.MovementInput();

    void CheckForCathedralCapture()
    {
        // Only care if we're local and waiting to be respawned.
        if (!photonView.IsMine || waitingPanel == null || !waitingPanel.activeSelf || !death)
            return;

        int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;

        // Get team info
        object teamIDObj;
        int teamID = -1;
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("teamID", out teamIDObj) && teamIDObj != null)
        {
            teamID = (int)teamIDObj;            
        }
        else
        {
            Debug.LogWarning("teamID not yet set in CustomProperties.");
            return; 
        }

        int baseBuildingID = -1;
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
            Debug.LogError($"[Multiplayer] No base building found for team {teamID}.");
            return;
        }

        TowerData cathedralTower = BuildingManager.Instance.TowerDictionary[baseBuildingID];

        if (cathedralTower.isCaptured && cathedralTower.controllingTeam == teamID && !respawnStarted)
        {
            Debug.Log("[PlayerDied] Player Respawn Entered");
            respawnPanel?.SetActive(true);
            waitingPanel?.SetActive(false);
            respawnStarted = true;

            // Used to hardcode 5f here and never touch deathCount, so a capital-recapture respawn
            // never scaled with repeated deaths the way a normal death does (Task 0.11a defect 3).
            // NextRespawnDelay is the one place both paths compute this now.
            float delay = NextRespawnDelay();
            Debug.Log($"[VIS] cathedral-recapture death {deathCount}, respawning in {delay}s");
            StartCoroutine(RespawnPlayer(delay, teamID, actorNumber));
        } else
        {
            Debug.Log("[PlayerDied] Player NOT Respawn Entered");
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

    void PlayerDied()
    {
        if (!photonView.IsMine)
            return;

        int teamID = (int)PhotonNetwork.LocalPlayer.CustomProperties["teamID"];
        int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;        

        int baseBuildingID = -1;
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
            Debug.LogError($"[Multiplayer] No base building found for team {teamID}.");
            return;
        }

        TowerData cathedralTower = BuildingManager.Instance.TowerDictionary[baseBuildingID];

        if (!cathedralTower.isCaptured || cathedralTower.controllingTeam != teamID)
        {
            // NOTE: this branch hides the player permanently -- no respawn, no RPC_ShowPlayer.
            // It reads TowerDictionary, which is replicated with RpcTarget.All (not AllBuffered),
            // so a client with a stale tower state can take this branch when it should not. That
            // is a live suspect for "sometimes I cannot see enemies anymore".
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
            respawnPanel?.SetActive(true);

            float delay = NextRespawnDelay();
            Debug.Log($"[VIS] death {deathCount}, respawning in {delay}s");

            StartCoroutine(RespawnPlayer(delay, teamID, actorNumber));
        }

        Debug.Log($"{playerNameText.text} respawned at team {teamID} spawn point.");
    }

    /// The one place the respawn wait is computed, called from both death paths (a normal death in
    /// PlayerDied and the capital-recapture death in CheckForCathedralCapture) so they cannot
    /// quietly diverge again the way they had before Task 0.11a: the recapture path used to
    /// hardcode 5f and skip deathCount entirely. Each death costs a bit more than the last, up to
    /// a cap, so repeated deaths carry a growing price without benching anyone for an unreasonable
    /// stretch - all three numbers now live on GameplayConfig, not here.
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

        Debug.Log($"{playerNameText.text} has been revived after recapture!");


        // Null-guarded: an NRE here would kill the coroutine after the 5s wait but BEFORE
        // RPC_ShowPlayer below, leaving the player hidden on every client forever.
        waitingPanel?.SetActive(false);

        RoomManager roomManager = FindObjectOfType<RoomManager>();
        if (roomManager != null && roomManager.teamSpawnPoints.Length > teamID)
        {
            transform.position = roomManager.teamSpawnPoints[teamID].position;
            transform.rotation = roomManager.teamSpawnPoints[teamID].rotation;
        }

    
        playerHealth.ResetForRespawn();

        SetAlive(true);

        // Logged because 2.10 (respawning where you died) is a fix I cannot verify from the
        // editor. If a respawn ever lands somewhere other than the base, this line says so.
        Debug.Log($"[VIS] respawned at {transform.position} (team {teamID} spawn)");
        photonView.RPC("RPC_HandleRespawnMaster", RpcTarget.MasterClient, teamID, actorNumber);

        Debug.Log($"{playerNameText.text} fully respawned at base after cathedral recapture.");

        death = false;
        respawnStarted = false;

        //capsuleCollider.GetComponent<Collider>().enabled = true;

        // Layer, mesh and speed are all restored by SetAlive(true) above.

/*        int prevTeamDeadCount = teamDeadCount[teamID];
        teamDeadCount[teamID] = prevTeamDeadCount - 1;*/

        respawnPanel?.SetActive(false);
    }

    /// Shows the end-of-match result to this client, win or lose. Used by the territory win
    /// condition, which needs to tell losers as well -- the elimination path only ever announced
    /// the winner, so everyone else was left with no screen at all.
    public void ShowMatchResult(int winningTeam)
    {
        if (!photonView.IsMine)
            return;

        int myTeam = PlayerTeam.NoTeam;
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(PlayerTeam.TeamKey, out object raw)
            && raw is int value)
        {
            myTeam = value;
        }

        waitingPanel?.SetActive(false);
        respawnPanel?.SetActive(false);

        if (myTeam == winningTeam)
            youWonPanel?.SetActive(true);
        else
            youLostPanel?.SetActive(true);

        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            // Freezes the player for the rest of the match. Used to assign a raw speed field on
            // this class directly, which PlayerMotor stopped reading once it moved to its own
            // keyed multiplier stack - that made the assignment a dead write, and a match-over
            // player could still slide around (Task 0.11a defect 1). There is no matching
            // RemoveSpeedMultiplier because the match is over for this player; nothing here ever
            // un-freezes them.
            playerMotor.AddSpeedMultiplier(this, 0f);
        }
    }

    [PunRPC]
    public void RPC_ShowYouWonPanel(int teamID)
    {
        int myTeam = (int)PhotonNetwork.LocalPlayer.CustomProperties["teamID"];


        Debug.Log("[Multiplayer] Show Winning Panel Entered");

        if (myTeam == teamID)
        {
            youWonPanel.SetActive(true);
        }
    }


    [PunRPC]
    void RPC_HideWaitingPanelAll(int teamID)
    {
        int localTeamID = (int)PhotonNetwork.LocalPlayer.CustomProperties["teamID"];
        if (teamID != localTeamID) return;


        Debug.Log("[Multiplayer] Hiding Waiting Panel Entered");

        if (waitingPanel != null && waitingPanel.activeSelf)
        {
            waitingPanel.SetActive(false);
            Debug.Log($"[Multiplayer] (RPC) Hiding Waiting for Team {teamID}.");
        }
    }

    [PunRPC]
    void RPC_ShowWaitingPanel(int teamID)
    {
        int localTeamID = (int)PhotonNetwork.LocalPlayer.CustomProperties["teamID"];
        if (teamID != localTeamID) return;


        Debug.Log("[Multiplayer] Show Waiting Panel Entered");

        if (waitingPanel != null && !waitingPanel.activeSelf && !youLostPanel.activeSelf)
        {
            waitingPanel.SetActive(true);
            Debug.Log($"[Multiplayer] (RPC) Showing Waiting panel for player on Team {teamID}.");
        }
    }



    [PunRPC]
    void RPC_ShowYouLostPanel(int teamID)
    {
        if ((int)PhotonNetwork.LocalPlayer.CustomProperties["teamID"] != teamID) return;

        Debug.Log("[Multiplayer] RPC_ShowYouLostPanel running");

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");

        if (waitingPanel != null)
        {
            waitingPanel.SetActive(false);
            Debug.Log("[Multiplayer] waitingPanel.SetActive(false) called");
        }

        if (youLostPanel != null && !waitingPanel.activeSelf)
        {
            youLostPanel.SetActive(true);
            Debug.Log("[Multiplayer] youLostPanel.SetActive(true) called");
        }

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");

       // StartCoroutine(DelayedShowLose());

        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            // See the matching comment in ShowMatchResult - same freeze-on-match-over fix, same
            // reason there is no corresponding RemoveSpeedMultiplier call.
            playerMotor.AddSpeedMultiplier(this, 0f);
        }
    }

    IEnumerator DelayedShowLose()
    {
        yield return new WaitForSeconds(0.1f);
        if (youLostPanel != null && !waitingPanel.activeSelf)
        {
            youLostPanel.SetActive(true);
            Debug.Log("[Multiplayer] youLostPanel.SetActive(true) called");
        }

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");

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

    void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
    void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

    void OnDestroy()
    {
        if (playerHealth != null)
            playerHealth.Died -= HandlePlayerHealthDied;
        if (playerMotor != null)
            playerMotor.FellBelowKillHeight -= HandleFellBelowKillHeight;
    }

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
        // floor for five seconds.
        if (capsuleCollider != null)
            capsuleCollider.enabled = alive;

        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.isKinematic = !alive;
            if (photonView.IsMine)
            {
                // Keyed so this can never step on some other system's own multiplier (a sprint
                // ability, a slow debuff). Used to assign a raw speed field directly, which
                // PlayerMotor stopped reading once it moved to this stack - that silently turned
                // "freeze on death" into a no-op and a corpse could still slide around (Task
                // 0.11a defect 1).
                if (alive)
                    playerMotor.RemoveSpeedMultiplier(this);
                else
                    playerMotor.AddSpeedMultiplier(this, 0f);
            }
        }

        // Shooting runs its own Update on the child mesh object, so gating input in this class
        // does not stop it. (Its reference was also broken until now: GetComponent on the root
        // returned null because PlayerShooting is not on the root.)
        if (playerShooting != null)
            playerShooting.enabled = alive;

        // A dash already in flight would keep moving the body after death, and could land it
        // somewhere other than the spawn point.
        if (!alive && activeDash != null)
        {
            StopCoroutine(activeDash);
            activeDash = null;
        }

        Debug.Log($"[VIS] alive={alive}  owner={photonView.Owner?.NickName}  isMine={photonView.IsMine}");
    }

    /// Green for a teammate, red for an enemy, white for yourself, grey while the answer is not
    /// known yet.
    ///
    /// The colour is a comparison between two Custom Properties -- this player's team and mine --
    /// and either can arrive after the object spawns. It used to be decided once in Start, so
    /// whichever value was missing at that instant produced a wrong colour that never corrected:
    /// a teammate whose team had not arrived showed red forever, and if my own team was missing
    /// too, both read as "unknown" and matched, so an enemy showed green forever.
    ///
    /// Grey rather than guessing: a briefly neutral tag is better than a confidently wrong one,
    /// and it resolves within a moment.
    void UpdateNameTagColour()
    {
        if (playerNameText == null || photonView == null)
            return;

        if (photonView.IsMine)
        {
            playerNameText.color = Color.white;
            return;
        }

        PlayerTeam team = GetComponent<PlayerTeam>();
        int localTeam = PlayerTeam.NoTeam;
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(PlayerTeam.TeamKey, out object raw)
            && raw is int value)
        {
            localTeam = value;
        }

        if (team == null || !team.HasTeam || localTeam == PlayerTeam.NoTeam)
        {
            playerNameText.color = Color.grey;
            return;
        }

        playerNameText.color = team.teamID == localTeam ? Color.green : Color.red;
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

        // Name tag colour compares THIS player's team against MY team, so it has to react to
        // either one arriving, not just this player's.
        if (changedProps.ContainsKey(PlayerTeam.TeamKey)
            && (targetPlayer == photonView.Owner || targetPlayer == PhotonNetwork.LocalPlayer))
        {
            UpdateNameTagColour();

            // The mesh colour is the same problem as the name tag colour: the team is a Custom
            // Property, so it is routinely still NoTeam when the object spawns. One prefab is
            // now shared by all three teams, so without this every player is team 0's colour.
            GetComponent<PlayerTeamAppearance>()?.Apply();
        }

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


    [PunRPC]
    public void RPC_HandleRespawnMaster(int teamID, int actorNumber)
    {
     
         if (!PhotonNetwork.IsMasterClient) return;

        int totalTeamPlayers = 0;

        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.ContainsKey("teamID") &&
                (int)p.CustomProperties["teamID"] == teamID)
            {
                totalTeamPlayers++;
            }
        }

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

        Photon.Realtime.Player deadPlayer = PhotonNetwork.CurrentRoom.GetPlayer(actorNumber);

        int totalTeamPlayers = 0;

        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.ContainsKey("teamID") &&
                (int)p.CustomProperties["teamID"] == teamID)
            {
                totalTeamPlayers++;
            }
        }

        int previousDead = teamDeadCount.ContainsKey(teamID) ? teamDeadCount[teamID] : 0;
        int newDeadCount = previousDead + 1;
        teamDeadCount[teamID] = newDeadCount;

        int remaining = totalTeamPlayers - newDeadCount;

        Debug.Log($"[Multiplayer] (Master) Team {teamID} has {remaining} player(s) remaining.");

        if (remaining <= 0)
        {
            deadTeams.Add(teamID);

            Debug.Log($"[Multiplayer] Team Dead : {teamID}");

            foreach (var p in PhotonNetwork.PlayerList
                    .Where(p => (int)p.CustomProperties["teamID"] == teamID))
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

        Debug.Log($"[Multiplayer] (Master) Dead Teams Count {deadTeams.Count}");

        HashSet<int> allTeamIDs = new HashSet<int>();

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player.CustomProperties.ContainsKey("teamID"))
                allTeamIDs.Add((int)player.CustomProperties["teamID"]);
        }


        List<int> remainingTeams = allTeamIDs.Where(tid => !deadTeams.Contains(tid)).ToList();

        if (remainingTeams.Count == 1)
        {
            int winningTeam = remainingTeams[0];

            Debug.Log($"[Multiplayer] Team {winningTeam} has WON the match!");

            photonView.RPC("RPC_ShowYouWonPanel", RpcTarget.All, winningTeam);
        }
    }


}