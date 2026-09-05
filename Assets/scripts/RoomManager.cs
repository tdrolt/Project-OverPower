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
        int teamID = (PhotonNetwork.LocalPlayer.ActorNumber - 1) % 3;
        Debug.Log($"Assigned Team: {teamID}");

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
