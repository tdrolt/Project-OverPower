using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class RoomManager : MonoBehaviourPunCallbacks
{
    [Header("Team Settings")]
    public GameObject[] teamPlayerPrefabs; // Index 0:Team0, 1:Team1, 2:Team2
    public Transform[] teamSpawnPoints;    // Index 0:Team0, 1:Team1, 2:Team2

    void Start()
    {
        // Default is 10 Hz, which makes the remote position a staircase updating once per
        // 100 ms. 20 Hz halves that interval and is the single cheapest smoothness win.
        // Must stay <= SendRate (default 30).
        PhotonNetwork.SerializationRate = 20;

        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master");
        // Do not join lobby here anymore
    }

    // ✅ Called from UI when "Join Game" is pressed
    public void JoinGame()
    {
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("Joined Lobby");
        PhotonNetwork.JoinRandomRoom();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log("No room found, creating one.");
        string roomName = "Room_" + Random.Range(1000, 9999);
        RoomOptions options = new RoomOptions();
        options.MaxPlayers = 9;
        PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined Room: {PhotonNetwork.CurrentRoom.Name}");
        AssignTeamAndSpawnPlayer();
    }

    void AssignTeamAndSpawnPlayer()
    {
        int teamID = PickSmallestTeam();
        Debug.Log($"[TEAM] assigned team {teamID} on join");

        if (!ValidateTeamResources(teamID)) return;

        GameObject player = PhotonNetwork.Instantiate(
            teamPlayerPrefabs[teamID].name,
            teamSpawnPoints[teamID].position,
            Quaternion.identity
        );

        SetupPlayerTeamComponent(player, teamID);
    }

    bool ValidateTeamResources(int teamID)
    {
        if (teamPlayerPrefabs.Length < 3 || teamSpawnPoints.Length < 3)
        {
            Debug.LogError("Missing team prefabs or spawn points!");
            return false;
        }

        if (teamPlayerPrefabs[teamID] == null || teamSpawnPoints[teamID] == null)
        {
            Debug.LogError($"Missing resources for team {teamID}!");
            return false;
        }
        return true;
    }

    /// Teams used to be (ActorNumber - 1) % 3. Photon never reuses actor numbers, so one player
    /// reconnecting got a fresh number and the split skewed permanently -- that is how a team
    /// ended up with four players while others had spare slots.
    ///
    /// Counting who is actually here handles reconnects, because a player who left stops being
    /// counted. Known limitation: two people joining in the same instant can both read the same
    /// counts and pick the same team, leaving it one over. Photon's check-and-swap on Room
    /// Properties would close that, but it is not worth the complexity for a nine-player
    /// prototype where people join over Discord.
    int PickSmallestTeam()
    {
        int[] counts = new int[3];

        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p == PhotonNetwork.LocalPlayer)
                continue;

            if (p.CustomProperties.TryGetValue(PlayerTeam.TeamKey, out object raw)
                && raw is int team && team >= 0 && team < counts.Length)
            {
                counts[team]++;
            }
        }

        int smallest = 0;
        for (int i = 1; i < counts.Length; i++)
        {
            if (counts[i] < counts[smallest])
                smallest = i;
        }

        Debug.Log($"[TEAM] current split {counts[0]}/{counts[1]}/{counts[2]} -> joining team {smallest}");
        return smallest;
    }

    void SetupPlayerTeamComponent(GameObject player, int teamID)
    {
        PlayerTeam pt = player.GetComponent<PlayerTeam>();
        if (pt != null)
        {
            // One write, one source of truth. PlayerTeam.teamID now reads this property, so the
            // direct field assignment and the buffered RPC that used to sit here are gone.
            UpdateNetworkProperties(teamID);
        }
        else
        {
            Debug.LogWarning("Player prefab missing PlayerTeam component!");
        }
    }

    void UpdateNetworkProperties(int teamID)
    {
        Hashtable teamProperty = new Hashtable();
        teamProperty.Add("teamID", teamID);
        PhotonNetwork.LocalPlayer.SetCustomProperties(teamProperty);
    }
}
