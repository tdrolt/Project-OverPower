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
    /// "lastStand" (PlayerLifecycle) - otherwise counts as already out; the two armor upgrade levels
    /// (LoadoutProperties) - PlayerLoadout.Start republishes them too, same as every other loadout pick
    /// below, so this is reset here only so a player turned away before spawning doesn't carry them.
    ///
    /// "gold" (GoldWallet) is REMOVED (a null value), not written to 0 (2.7b leftover). GoldWallet.Start
    /// reads an EXISTING gold key as the balance - a written 0 would beat a future non-zero
    /// TerritoryConfig.StartingGold, the one home for what a fresh player starts with. Photon strips a
    /// null-valued key from the local player's Custom Properties, so the next room's GoldWallet.Start
    /// finds no key at all and falls through to StartingGold, exactly like a first-time joiner.
    ///
    /// Kept: "teamID" - PickSmallestTeam overwrites it on the very next join anyway, nothing to reset.
    /// Kept: weapon/equipment/ultimate/mobility (LoadoutProperties) - PlayerLoadout.Start republishes
    /// the whole starting kit for every newly spawned player regardless of what is still on the local
    /// Custom Properties, so there is nothing here for a stale pick to leak into a new match. Treated
    /// the same as the nickname: a player-level preference that carries forward until the player
    /// changes it themselves, not a fact about the match just played.</summary>
    public override void OnLeftRoom()
    {
        var props = new Hashtable
        {
            { PlayerLifecycle.AliveKey, true },
            { PlayerLifecycle.LastStandKey, false },
            { PlayerLifecycle.LastStandAtKey, null },
            { GoldWallet.GoldKey, null },
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

        // 2.7b step 5 (MatchStartRules.MayJoin, Decision 4/17): before the teams are fixed, any team may be
        // joined; from the countdown on, only a team in the match and not knocked out - which also covers the
        // Task 2.7 review's original case (a team out of the match, sitting at 0, never gets a new player) since
        // an eliminated team is never in MayJoinTeam's "in match and not eliminated" answer either. Read live, so
        // a team fixed out or eliminated mid-session is skipped for every join after it.
        MatchDirector director = MatchDirector.Instance;

        int smallest = NoFreeTeam;
        for (int i = 0; i < counts.Length; i++)
        {
            if (director != null && !director.MayJoinTeam(i))
                continue;
            if (smallest == NoFreeTeam || counts[i] < counts[smallest])
                smallest = i;
        }

        if (smallest == NoFreeTeam)
        {
            Debug.LogWarning("[TEAM] no team may be joined right now (every team eliminated, or the match started without a free one) -- refusing to spawn");
            return NoFreeTeam;
        }

        if (counts[smallest] >= TeamSize)
        {
            // Review fix: this used to say "every team is full", which read wrong once a host-started match can
            // leave a team out of mTeams entirely - that team can sit at 0/3 and still never be smallest, because
            // MayJoinTeam skipped it above. The raw counts below may include a team that isn't full at all, just
            // not one you may join.
            Debug.LogWarning($"[TEAM] no room on a team you may join ({counts[0]}/{counts[1]}/{counts[2]}, cap {TeamSize}) -- every joinable team is full, or the rest are left out of the match -- refusing to spawn");
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

    /// <summary>2.7b step 5 (Decision 17, R3): closes the joiner race on the joining side - a player the server
    /// placed on a team before the countdown write reached them, but whose team is not in mTeams, re-picks the
    /// moment they see the teams fixed (MatchDirector.ReactToRoomState's teams-fixed edge) or the match go live
    /// (the live edge, for a player who joined mid-countdown - not firstRead there, so this covers them too).
    /// Idempotent: a player already on a real team, or before the teams are fixed at all, just returns the
    /// current team - so MatchDirector's live edge can call this unconditionally to learn which team
    /// ResetForMatchStart should use.</summary>
    public int EnsureLocalTeamInMatch()
    {
        MatchDirector director = MatchDirector.Instance;
        if (director == null || !Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam))
            return -1;

        if (!director.TeamsFixed || director.MayJoinTeam(myTeam))
            return myTeam;

        int picked = PickSmallestTeam();
        if (picked == NoFreeTeam)
        {
            Debug.LogWarning("[TEAM] joined the left-out team and no other team has room to re-pick into -- staying put");
            return myTeam;
        }

        Debug.Log($"[TEAM] re-picked {myTeam} -> {picked} (joined the left-out team as the host started)");
        UpdateNetworkProperties(picked);
        return picked;
    }
}
