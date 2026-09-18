using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Overpower.Match;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class RoomManager : MonoBehaviourPunCallbacks
{
    [Header("Team Settings")]
    // ONE prefab for every team. There used to be three -- teamPlayerPrefabs[teamID] -- which
    // were identical apart from one material, and which silently drifted apart: their dash values
    // disagreed per team, so every playtest before 2026-09-08 ran on an asymmetric game. The team
    // colour is applied at runtime by PlayerTeamAppearance instead.
    public GameObject playerPrefab;
    public Transform[] teamSpawnPoints;    // Index 0:Team0, 1:Team1, 2:Team2

    [Tooltip("Where each team respawns while its capital is under attack (index = team, same order as Team Spawn " +
             "Points). Each sits in that capital's Tier 2 zone, whoever owns it. Placed by the arena tool: move the Team " +
             "2 one and press Rebuild thirds on Enviorment/Arena.")]
    public Transform[] capitalUnderAttackSpawnPoints;

    public const int TeamSize = 3;         // hard cap per team; 3 teams x 3 = the room's 9
    private const int NoFreeTeam = -1;

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
        options.MaxPlayers = (byte)(TeamSize * 3);   // 3 teams at the hard cap
        PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined Room: {PhotonNetwork.CurrentRoom.Name}");
        AssignTeamAndSpawnPlayer();
    }

    /// <summary>Review round 2: Photon carries the local player's own Custom Properties into the
    /// NEXT room they join - the game's own Quit button disconnects and exits, so play never reaches
    /// this, but the harness leaves and rejoins a lot inside one running process. Reset here, on the
    /// LOCAL player, everything that belongs to the match just left rather than to this player across
    /// matches, so a fresh room never starts with a dead, last-standing player carrying gold and
    /// armor levels they never earned in it.
    ///
    /// Reset: "alive" (PlayerLifecycle) - a last-stand death otherwise spawns the next match dead;
    /// "lastStand" (PlayerLifecycle) - otherwise counts as already out; "gold" (GoldWallet) and the
    /// two armor upgrade levels (LoadoutProperties) - all three are this match's economy, bought with
    /// gold that match paid out, same as gold itself.
    /// Kept: "teamID" - PickSmallestTeam overwrites it on the very next join anyway, nothing to reset.
    /// Kept: weapon/equipment/ultimate/mobility (LoadoutProperties) - a loadout PICK, not a fact about
    /// the match just played; treated the same as the nickname, a player-level preference that
    /// carries forward until the player changes it themselves.</summary>
    public override void OnLeftRoom()
    {
        var props = new Hashtable
        {
            { PlayerLifecycle.AliveKey, true },
            { PlayerLifecycle.LastStandKey, false },
            { GoldWallet.GoldKey, 0 },
            { LoadoutProperties.ArmorAbsorbLevelKey, 0 },
            { LoadoutProperties.ArmorRechargeLevelKey, 0 },
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    void AssignTeamAndSpawnPlayer()
    {
        int teamID = PickSmallestTeam();

        // Belt and braces alongside MaxPlayers. Photon's room cap stops a tenth client joining,
        // but this also catches the case where the counts are momentarily wrong -- better to turn
        // one player away with a clear reason than to let a team quietly reach four.
        if (teamID == NoFreeTeam)
        {
            Debug.LogWarning("[TEAM] no free slot in any team, leaving the room");
            PhotonNetwork.LeaveRoom();
            return;
        }

        Debug.Log($"[TEAM] assigned team {teamID} on join");

        if (!ValidateTeamResources(teamID)) return;

        GameObject player = PhotonNetwork.Instantiate(
            playerPrefab.name,
            teamSpawnPoints[teamID].position,
            Quaternion.identity
        );

        SetupPlayerTeamComponent(player, teamID);
    }

    bool ValidateTeamResources(int teamID)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("RoomManager.playerPrefab is not assigned!");
            return false;
        }

        if (teamSpawnPoints.Length < 3 || teamSpawnPoints[teamID] == null)
        {
            Debug.LogError($"Missing spawn point for team {teamID}!");
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

        // Task 2.7 review: a team that is out of the match never gets a new player, even if it is
        // sitting at 0 - MatchDirector.IsEliminated is the room's own answer, read live so a team
        // eliminated mid-session is skipped for every join after it.
        MatchDirector director = MatchDirector.Instance;

        int smallest = NoFreeTeam;
        for (int i = 0; i < counts.Length; i++)
        {
            if (director != null && director.IsEliminated(i))
                continue;
            if (smallest == NoFreeTeam || counts[i] < counts[smallest])
                smallest = i;
        }

        if (smallest == NoFreeTeam)
        {
            Debug.LogWarning("[TEAM] every team is eliminated -- refusing to spawn");
            return NoFreeTeam;
        }

        if (counts[smallest] >= TeamSize)
        {
            Debug.LogWarning($"[TEAM] every team is full ({counts[0]}/{counts[1]}/{counts[2]}, cap {TeamSize}) -- refusing to spawn");
            return NoFreeTeam;
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
