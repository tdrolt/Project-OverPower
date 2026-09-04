using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun.UtilityScripts;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Multiplayer : MonoBehaviour, IPunObservable
{
    public float movementSpeed = 5f;
    // Captured at Start so death (which sets speed to 0) and respawn can restore whatever the
    // prefab actually says, instead of a second hardcoded number that disagrees with it.
    private float startingMovementSpeed;
    private Rigidbody rigidbody;

    public float fireRate = 0.75f;
    public GameObject bulletPrefab;
    public Transform bulletPosition;
    public GameObject bulletFiringEffect;    
    private float nextFire;

    [HideInInspector]
    public int health = 100;
    public Slider healthBar;
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

    // Variables for network synchronization
    private Vector3 networkPosition;
    private Quaternion networkRotation;

    // References to ability scripts
    private PlayerShooting playerShooting;
    private PlayerDash playerDash;
    private AoEAbility aoeAbility;
    private PlayerDashWithBuff playerDashWithBuff;
    private PlayerDashWithProjectile playerDashWithProjectile;
    private CapsuleCollider capsuleCollider;

    private bool death = false;
    private bool respawnStarted = false;
    // Static dictionary to keep track of dead players per team.
    private static Dictionary<int, int> teamDeadCount = new Dictionary<int, int>();
    private static HashSet<int> processedDeaths = new HashSet<int>();
    private static List<int> deadTeams = new List<int>();

    void Start()
    {
        rigidbody = GetComponent<Rigidbody>();
        startingMovementSpeed = movementSpeed;
        photonView = GetComponent<PhotonView>();
        playerShooting = GetComponent<PlayerShooting>();
        playerDash = GetComponent<PlayerDash>();
        playerDashWithBuff = GetComponent<PlayerDashWithBuff>();
        playerDashWithProjectile = GetComponent<PlayerDashWithProjectile>();
        aoeAbility = GetComponent<AoEAbility>();
        capsuleCollider = GetComponent<CapsuleCollider>();

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

        // Set color based on team relation (friendly green, enemy red)
        PlayerTeam pt = GetComponent<PlayerTeam>();
        if (pt != null && !photonView.IsMine)
        {
            if (pt.teamID == localTeam)
            {
                playerNameText.color = Color.green;
            }
            else
            {
                playerNameText.color = Color.red;
            }
        }
        else if (photonView.IsMine)
        {
            playerNameText.color = Color.white; // local player sees self as white
        }
        else
        {
            playerNameText.color = Color.white;
        }

        Debug.LogError("This message will make the console appear in Development Builds");

        // Initialize network sync values
        networkPosition = transform.position;
        networkRotation = transform.rotation;

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

        UpdateRotationFromMouse();

        // Process abilities (keys updated as needed)
        if (Input.GetKeyDown(KeyCode.Space) && playerDash != null && playerDash.enabled && playerDash.CanDash())
        {
            Vector3 inputDirection = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            StartCoroutine(playerDash.Dash(inputDirection));
        }
        if (Input.GetKeyDown(KeyCode.Space) && playerDashWithBuff != null && playerDashWithBuff.enabled && playerDashWithBuff.CanDash())
        {
            Vector3 inputDirection = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            StartCoroutine(playerDashWithBuff.Dash(inputDirection));
        }
        if (Input.GetKeyDown(KeyCode.Space) && playerDashWithProjectile != null && playerDashWithProjectile.enabled && playerDashWithProjectile.CanDash())
        {
            Vector3 inputDirection = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            StartCoroutine(playerDashWithProjectile.Dash(inputDirection));
        }
        if (Input.GetKeyDown(KeyCode.Space) && aoeAbility != null && aoeAbility.enabled)
        {
            aoeAbility.TriggerAoE();
        }

        // Shooting handled in PlayerShooting script (using left mouse)
    }

    void FixedUpdate()
    {
        if (photonView.IsMine)
        {
            if (playerDash == null || !playerDash.IsDashing())
            {
                Move();
            }
        }
        else
        {
            rigidbody.MovePosition(Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 10));
            rigidbody.MoveRotation(Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * 10));
        }

        // Continuously check for cathedral capture status
        CheckForCathedralCapture();
    }

    void UpdateRotationFromMouse()
    {
        // Only allow local player to control rotation
        if (!photonView.IsMine)
            return;

        // Cast a ray from the mouse position to the game world
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, transform.position.y, 0));
        float rayDistance;

        if (groundPlane.Raycast(ray, out rayDistance))
        {
            // Get the point where the ray hits the ground plane
            Vector3 pointToLook = ray.GetPoint(rayDistance);

            // Calculate direction from player to mouse position
            Vector3 direction = pointToLook - transform.position;
            direction.y = 0f; // Keep rotation horizontal

            if (direction != Vector3.zero)
            {
                // Rotate player to face the mouse cursor
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }
    }

    void Move()
    {
        float horizontalInput = Input.GetAxisRaw("Horizontal");
        float verticalInput = Input.GetAxisRaw("Vertical");

        Vector3 movementDir = new Vector3(horizontalInput, 0, verticalInput);
        if (movementDir.magnitude > 1)
            movementDir.Normalize();

        rigidbody.MovePosition(rigidbody.position + movementDir * movementSpeed * Time.deltaTime);
    }

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
            StartCoroutine(RespawnPlayer(5f, teamID, actorNumber));
        } else
        {
            Debug.Log("[PlayerDied] Player NOT Respawn Entered");
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(health);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            health = (int)stream.ReceiveNext();
            healthBar.value = health;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Only the player being hit decides that they were hit. Without this, every client
        // subtracted health from its own copy of the victim and the owner's next serialization
        // put it back, so the health bar visibly fought itself.
        if (!photonView.IsMine)
            return;

        if (!collision.gameObject.CompareTag("Bullet"))
            return;

        MultiplayerBulletController bullet = collision.gameObject.GetComponent<MultiplayerBulletController>();
        if (bullet == null)
            return;

        // Your own bullet cannot hurt you (dash in front of your own shot).
        if (bullet.owner != null && bullet.owner == photonView.Owner)
            return;

        TakeDamage(bullet);
    }

    void TakeDamage(MultiplayerBulletController bullet)
    {
        float finalDamage = bullet.damage;

        if (playerDashWithBuff != null && playerDashWithBuff.IsBuffActive())
        {
            finalDamage = playerDashWithBuff.ApplyDamageReduction(bullet.damage);
        }

        health -= (int)finalDamage;
        healthBar.value = health;

        if (health <= 0)
        {
            bullet.owner.AddScore(1);

            if (!death)
                PlayerDied();

            death = true;
        }
    }

    public void ApplyAoEDamage(float damage, Photon.Realtime.Player caster)
    {
        health -= (int)damage;
        healthBar.value = health;
        Debug.Log($"[Multiplayer] {playerNameText.text} took {damage} AoE damage. Remaining health: {health}");

        if (health <= 0)
        {
         
            if (caster != null && caster != photonView.Owner)
            {
                caster.AddScore(1);
                Debug.Log($"[Multiplayer] {playerNameText.text} died. {caster.NickName} scores!");
            }

            if (!death)
                PlayerDied();

            death = true;
        }
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
            Debug.LogWarning($"[Multiplayer] Team {teamID} lost their base. Sending death RPC...");

            photonView.RPC("RPC_HandleDeath", RpcTarget.All, teamID);
            photonView.RPC("RPC_HandleDeathMaster", RpcTarget.MasterClient, teamID, actorNumber);       
            return;
        }

        if (cathedralTower.isCaptured && cathedralTower.controllingTeam == teamID && !respawnStarted)
        {
            respawnStarted = true;

            photonView.RPC("RPC_HandleDeath", RpcTarget.All, teamID);
            Debug.Log("[PlayerDied] Player Respawn Entered");
            respawnPanel?.SetActive(true);
            StartCoroutine(RespawnPlayer(5f, teamID, actorNumber));
        }

        Debug.Log($"{playerNameText.text} respawned at team {teamID} spawn point.");
    }

    private IEnumerator RespawnPlayer(float delay, int teamID, int actorNumber)
    {
        yield return new WaitForSeconds(delay);

        Debug.Log($"{playerNameText.text} has been revived after recapture!");


        waitingPanel.SetActive(false);

        RoomManager roomManager = FindObjectOfType<RoomManager>();
        if (roomManager != null && roomManager.teamSpawnPoints.Length > teamID)
        {
            transform.position = roomManager.teamSpawnPoints[teamID].position;
            transform.rotation = roomManager.teamSpawnPoints[teamID].rotation;
        }

    
        health = 100;
        healthBar.value = health;

        photonView.RPC("RPC_ShowPlayer", RpcTarget.All);
        photonView.RPC("RPC_HandleRespawnMaster", RpcTarget.MasterClient, teamID, actorNumber);

        Debug.Log($"{playerNameText.text} fully respawned at base after cathedral recapture.");

        death = false;
        respawnStarted = false;

        //capsuleCollider.GetComponent<Collider>().enabled = true;

        int defaultLayer = LayerMask.NameToLayer("Default");
        SetLayerRecursively(gameObject, defaultLayer);

        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            movementSpeed = startingMovementSpeed;
        }

/*        int prevTeamDeadCount = teamDeadCount[teamID];
        teamDeadCount[teamID] = prevTeamDeadCount - 1;*/

        respawnPanel?.SetActive(false);
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
            movementSpeed = 0f;
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

    void SetLayerRecursively(GameObject o, int layer)
    {
        o.layer = layer;
        foreach (Transform child in o.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    // 3) Updated death handler
    [PunRPC]
    void RPC_HandleDeath(int teamID)
    {
        // 1) Hide the player's mesh
        //capsuleCollider.GetComponent<Collider>().enabled = false;
        //capsuleCollider.GetComponent<Collider>().enabled = false;

        int deadLayer = LayerMask.NameToLayer("DeadPlayer");
        SetLayerRecursively(gameObject, deadLayer);

        if (playerMesh != null)
            playerMesh.SetActive(false);


        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            movementSpeed = 0f;
        }
    }

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

    [PunRPC]
    void RPC_HidePlayer()
    {
        if (playerMesh != null)
            playerMesh.SetActive(false);
    }

    [PunRPC]
    void RPC_ShowPlayer()
    {
        if (playerMesh != null)
            playerMesh.SetActive(true);
    }
}