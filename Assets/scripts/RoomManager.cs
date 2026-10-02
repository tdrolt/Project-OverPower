using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Overpower.Data;
using Overpower.Lobby;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;
using UnityEngine.SceneManagement;
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

    [Header("Rejoin (Task 9e)")]
    [Tooltip("Read for the rejoin window (Connection > Rejoin Window Seconds): how long the room keeps a dropped player's place.")]
    [SerializeField] private GameplayConfig gameplayConfig;

    [Tooltip("Read for the Connection lost panel and the name screen's Rejoin your match button.")]
    [SerializeField] private UiTheme theme;

    [Header("Lobbies (lobby Task 2)")]
    [Tooltip("Every game mode a lobby can be created with. The interim Join button creates its room with the first available one.")]
    [SerializeField] private GameModeCatalogue modeCatalogue;

    [Tooltip("Read for how long a lobby name may be.")]
    [SerializeField] private LobbyConfig lobbyConfig;

    [Tooltip("Read by a spectator seat's view for the centre scan's numbers (its wave and the countdown), which otherwise come from a player's own body.")]
    [SerializeField] private VisionConfig visionConfig;

    /// <summary>The vision numbers (the centre scan's wave and countdown) for a client with no body: a spectator seat's view (lobby Task 7).</summary>
    public VisionConfig Vision => visionConfig;

    /// <summary>The live lobby list, and creating and joining lobbies (lobby Task 2).</summary>
    public LobbyDirectory Lobbies { get; private set; }

    /// <summary>Who sits where in the lobby this client is in (lobby Task 3).</summary>
    public LobbySeats Seats { get; private set; }

    /// <summary>The host's Start game and what every client does when it lands (lobby Task 4).</summary>
    public LobbyStart GameStart { get; private set; }

    /// <summary>What a player on a spectator seat sees: the camera, Q / E / Space and the bar (lobby Task 6). Idle until LobbyStart begins it.</summary>
    public SpectatorSeatView SeatView { get; private set; }

    /// <summary>The scene's UI theme (the spectator bar and the rejoin panel read their colours, sizes and texts from it).</summary>
    public UiTheme Theme => theme;

    /// <summary>True from pressing the interim Join button (when not yet in the lobby) until OnJoinedLobby consumes it: only then does joining the lobby go on to join a random room.</summary>
    private bool joiningRandom;

    /// <summary>The client side of coming back after a drop (Task 9e). The name screen asks it whether to offer a rejoin.</summary>
    public RejoinController Rejoin { get; private set; }

    /// <summary>Seconds the room keeps a dropped player's place: RoomOptions.PlayerTtl, and how long the name screen offers
    /// "Rejoin your match". One home: GameplayConfig.</summary>
    public float RejoinWindowSeconds => gameplayConfig != null ? gameplayConfig.RejoinWindowSeconds : 0f;

    /// <summary>The gameplay config this scene's RoomManager holds: the countdown length when the master has no body (a spectator host, lobby Task 5).</summary>
    public GameplayConfig Config => gameplayConfig;

    /// <summary>How long a rejoined player waits for PUN to hand their old body back before spawning a fresh one.</summary>
    private const float BodyReturnWaitSeconds = 1.5f;

    void Awake()
    {
        // Created in Awake, not Start: the name screen (JoinGameUI.Start) subscribes to it and Start order is not fixed.
        Rejoin = gameObject.AddComponent<RejoinController>();
        Rejoin.Init(theme, RejoinWindowSeconds);
        Lobbies = gameObject.AddComponent<LobbyDirectory>();
        Lobbies.Init(this, modeCatalogue, lobbyConfig);
        Seats = gameObject.AddComponent<LobbySeats>();
        Seats.Init(modeCatalogue, lobbyConfig);
        GameStart = gameObject.AddComponent<LobbyStart>();
        GameStart.Init(this, Seats);
        SeatView = gameObject.AddComponent<SpectatorSeatView>();
        SeatView.Init(this);
    }

    void Start()
    {
        if (gameplayConfig == null)
            Debug.LogError("[REJOIN] RoomManager has no GameplayConfig - rooms are created with no rejoin window (a dropped player is removed at once).");

        // The random id kept on this install is the Photon user id: it is what lets the server give a dropped player their own
        // place back. Set before connecting. Only its first 8 characters are ever logged.
        PhotonNetwork.AuthValues = new Photon.Realtime.AuthenticationValues(PlayerIdentity.UserId);
        Debug.Log($"[REJOIN] user id {PlayerIdRule.ForLog(PlayerIdentity.UserId)}..., rejoin window {RejoinWindowSeconds:0} s");

        // Default is 10 Hz, which makes the remote position a staircase updating once per
        // 100 ms. 20 Hz halves that interval and is the single cheapest smoothness win.
        // Must stay <= SendRate (default 30).
        PhotonNetwork.SerializationRate = 20;

        // Task 9f: after "back to name screen" the scene is rebuilt while the connection stays up on the master server: a second
        // ConnectUsingSettings then would be refused and only log noise.
        if (!PhotonNetwork.IsConnected)
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
        if (PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinRandomRoom();
        }
        else
        {
            // Only this branch waits for OnJoinedLobby; the flag is consumed there, so a join that fails some other way can
            // never later turn LobbyDirectory.EnterList() into a random join.
            joiningRandom = true;
            PhotonNetwork.JoinLobby();
        }
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("Joined Lobby");
        // Entering the lobby for the list (LobbyDirectory.EnterList) must not also join a room.
        if (joiningRandom)
        {
            joiningRandom = false;
            PhotonNetwork.JoinRandomRoom();
        }
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        // Task 9e-2: Photon refuses a normal join while this same user id still holds a dropped place in that room (CheckUserOnJoin).
        // That is our own place being held, not an absence of rooms - go back to it (or say so) instead of starting a new room.
        if (Rejoin != null && Rejoin.TryRejoinHeldPlace(returnCode))
            return;

        Debug.Log($"No room found ({returnCode}), creating one.");
        // Every room is a lobby now (lobby Task 2): created through the directory so it carries the lobby properties.
        // The interim path uses the catalogue's first available mode; Task 9 replaces it with the create screen.
        GameModeDefinition mode = null;
        if (modeCatalogue != null)
            foreach (GameModeDefinition m in modeCatalogue.Modes)
                if (m != null && m.Available) { mode = m; break; }
        if (mode == null)
        {
            Debug.LogError("[LOBBY] RoomManager has no available game mode in its catalogue - cannot create a room.");
            joiningRandom = false;
            return;
        }
        Lobbies.Create(PhotonNetwork.NickName + "'s lobby", mode);
    }

    public override void OnJoinedRoom()
    {
        joiningRandom = false;
        Debug.Log($"Joined Room: {PhotonNetwork.CurrentRoom.Name}");

        // Task 9e: the same actor coming back (ReconnectAndRejoin / RejoinRoom) is not a new player. Its team, gold and loadout
        // are still Player Properties in the room, so its seat keeps its team (nothing is picked). Its old body was removed by the master when the drop
        // was noticed (RejoinController), so a new one is spawned on its own team a moment from now
        // (WatchOwnBodyAfterRejoin); if the room still holds the buffered spawn, PUN hands that body back instead and
        // nothing is spawned. Either way PlayerLifecycle respawns it as after a death.
        if (PhotonNetwork.LocalPlayer.HasRejoined)
        {
            // The seat was given up while they were away (they dropped in the lobby and the master freed it before Start): they come back to a
            // running game with no seat, i.e. as a late joiner. LobbyStart places them; the team they held is forgotten so nothing spawns for it.
            if (GameStart != null && GameStart.GameRunningWithoutMySeat)
            {
                Debug.Log($"[REJOIN] actor {PhotonNetwork.LocalPlayer.ActorNumber} is back but no longer has a seat - joining the running game as a late joiner");
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { Teams.TeamKey, null }, { Teams.SpectatorKey, null } });
                return;
            }
            // A player who held a spectator seat comes back as a spectator: no body, no watchdog (LobbyStart starts the spectator view
            // again from the seat; the "spec" flag is still on their Player Properties).
            if (Teams.IsSpectator(PhotonNetwork.LocalPlayer))
            {
                Debug.Log($"[REJOIN] actor {PhotonNetwork.LocalPlayer.ActorNumber} is back on a spectator seat - no body");
                return;
            }
            Debug.Log($"[REJOIN] actor {PhotonNetwork.LocalPlayer.ActorNumber} is back - no new team pick, getting a body");
            // Task 9e-2 / 9e-3: the team this player held may have been left out of the match at go-live: pick again as a joiner would.
            // A team that was KNOCKED OUT while they were away is kept: they come back as its spectator (dead, no respawn - the
            // ordinary death path already waits for an eliminated team), not moved to a team that is still in.
            MatchDirector rejoinDirector = MatchDirector.Instance;
            if (rejoinDirector != null && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int heldTeam))
            {
                bool eliminated = rejoinDirector.IsEliminated(heldTeam);
                if (RejoinRules.TeamOnRejoin(rejoinDirector.TeamsFixed, eliminated, rejoinDirector.IsInMatch(heldTeam)) == RejoinTeamAction.Repick)
                    EnsureLocalTeamInMatch();
                else if (rejoinDirector.TeamsFixed && eliminated)
                    Debug.Log($"[REJOIN] team {heldTeam} was knocked out while this player was away - back as its spectator");
            }
            StartCoroutine(WatchOwnBodyAfterRejoin());
            return;
        }

        // A new player arrives seatless (lobby Task 4): no team and no body until the host starts the game (LobbyStart spawns
        // them on their seat's team). A team or spectator flag left on the local player by a previous room is cleared here.
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { Teams.TeamKey, null }, { Teams.SpectatorKey, null } });
    }

    /// <summary>Task 9e / 9e-2: the returning player's body, as a WATCHDOG. The master removes a dropped player's body (and with it the
    /// room's buffered spawn), so normally nothing comes back and this spawns a new one on the team the room holds for them, exactly as
    /// a new joiner would but without picking a team again. If the buffered spawn is still there, PUN hands the old body back - and
    /// the master may still destroy it a moment later, so a sighting is not the end: while in the room with no own body for
    /// BodyReturnWaitSeconds a body is spawned, until the first respawn has completed (the body is alive again).</summary>
    private System.Collections.IEnumerator WatchOwnBodyAfterRejoin()
    {
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;
        float withoutBody = 0f;
        float total = 0f;
        while (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer.ActorNumber == actor)
        {
            PhotonView view = PlayerLookup.GetPhotonViewFor(actor);
            if (view != null)
            {
                withoutBody = 0f;
                view = DestroyExtraOwnBodies(actor, view);
                PlayerLifecycle lifecycle = view.GetComponent<PlayerLifecycle>();
                // Task 9f: "waiting" is read from THIS body (its own wait panel), not from the player's "lastStand" property - that one
                // survives the drop and would end the watch before the new body had even started its respawn.
                MatchUI bodyUi = view.GetComponent<MatchUI>();
                bool waiting = bodyUi != null && (bodyUi.IsWaitingForRespawn || bodyUi.MatchOver); // a result panel ends the watch too
                if (RejoinRules.BodyWatchIsDone(true, lifecycle != null && lifecycle.IsAlive, waiting, total))
                    yield break; // respawned, or in the last-stand wait: nothing more to watch for
            }
            else
            {
                withoutBody += Time.unscaledDeltaTime;
                if (RejoinRules.NeedsFallbackBody(false, withoutBody, BodyReturnWaitSeconds))
                {
                    if (Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int team))
                    {
                        Debug.Log($"[REJOIN] no own body for {BodyReturnWaitSeconds:0.0} s - spawning one on team {team}");
                        SpawnPlayerOnTeam(team);
                    }
                    withoutBody = -BodyReturnWaitSeconds; // the new body registers itself on its first frame; give it time
                }
            }
            total += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>Task 9e-3 / 9f: an old cached body can arrive AFTER the watchdog already spawned a fallback one - two own bodies. Keeps
    /// the one THIS client spawned itself (the same answer whatever order the bodies arrived in; BackToNameScreenRules.BodyToKeep),
    /// network-destroys the others and points PlayerLookup at the kept one. Returns the kept body's view.</summary>
    private PhotonView DestroyExtraOwnBodies(int actor, PhotonView lookupView)
    {
        var own = new System.Collections.Generic.List<PhotonView>();
        var ids = new System.Collections.Generic.List<int>();
        foreach (PlayerLifecycle other in FindObjectsByType<PlayerLifecycle>(FindObjectsSortMode.None))
        {
            PhotonView v = other.GetComponent<PhotonView>();
            if (v != null && v.OwnerActorNr == actor && v.IsMine)
            {
                own.Add(v);
                ids.Add(v.ViewID);
            }
        }
        if (own.Count < 2)
            return lookupView;

        int keepId = BackToNameScreenRules.BodyToKeep(ids, spawnedBodyViewId, lookupView != null ? lookupView.ViewID : -1);
        PhotonView kept = lookupView;
        foreach (PhotonView v in own)
        {
            if (v.ViewID == keepId)
            {
                kept = v;
                continue;
            }
            Debug.LogWarning($"[REJOIN] a second own body (view {v.ViewID}) - removing it, keeping view {keepId}");
            PhotonNetwork.Destroy(v.gameObject);
        }
        PlayerLookup.Register(actor, kept);
        return kept;
    }

    /// <summary>The view id of the body THIS client last spawned itself (SpawnPlayerOnTeam); -1 = none.</summary>
    private int spawnedBodyViewId = -1;

    // ---- back to the lobby list (Task 9f, Tudor D22; lobby Task 8) ----

    private bool returningToNameScreen;

    /// <summary>The result screen's button (and a spectator's Leave): leave the room for good, then REBUILD the scene and land back in
    /// Photon's lobby with the list loaded (the rebuilt name screen sees LobbyReturn and goes straight to the list; the typed name is kept).
    /// The scene is
    /// reloaded (rather than only hiding panels) because everything a match leaves in it - territory, capitals, zones, packs, portals,
    /// minimap, chat, panels, scoreboard - is then a fresh copy by construction instead of a list of things to remember to clear.
    /// The connection stays up on the master server, so Join works at once. LeaveRoom(false) frees the seat, OnLeftRoom resets
    /// the match properties, and the saved match is forgotten so the name screen offers no Rejoin.</summary>
    public void ReturnToLobbyList()
    {
        if (returningToNameScreen)
            return;
        returningToNameScreen = true;

        // The name kept is the one typed, not the numbered copy ("Tudor 2") the lobby just left may have shown.
        if (Seats != null && !string.IsNullOrEmpty(Seats.TypedNickName))
            PhotonNetwork.NickName = Seats.TypedNickName;
        LobbyReturn.OpenListOnLoad = true;

        Overpower.Telemetry.MatchLogZip.Instance?.ZipNow();
        PlayerIdentity.ClearLastMatch();
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom(becomeInactive: false);
            PhotonNetwork.SendAllOutgoingCommands();
        }
        StartCoroutine(ReloadWhenBackOnMaster());
    }

    private const float ReturnTimeoutSeconds = 20f;

    private System.Collections.IEnumerator ReloadWhenBackOnMaster()
    {
        float waited = 0f;
        bool reconnecting = false;
        while (waited < ReturnTimeoutSeconds)
        {
            ReturnStep step = BackToNameScreenRules.NextStep(PhotonNetwork.NetworkClientState);
            if (step == ReturnStep.ReloadScene)
                break;
            if (step == ReturnStep.Reconnect && !reconnecting)
            {
                reconnecting = true;
                PhotonNetwork.ConnectUsingSettings();
            }
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        // The wait ran out (still in a room, or halfway through connecting): drop the connection so the rebuilt scene connects afresh.
        if (BackToNameScreenRules.MustDisconnectBeforeReload(PhotonNetwork.NetworkClientState))
        {
            Debug.LogWarning($"[NAME SCREEN] not on the master server after {ReturnTimeoutSeconds:0} s ({PhotonNetwork.NetworkClientState}) - disconnecting before the rebuild");
            PhotonNetwork.Disconnect();
            float disconnectWait = 0f;
            while (disconnectWait < 5f && PhotonNetwork.IsConnected)
            {
                disconnectWait += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // A timeout Disconnect reads as a lost connection (OnLeftRoom keeps the match properties for a rejoin), so the reset is made here
        // whatever happened above: the next match must start clean.
        ResetMatchProperties();

        // The log's last lines are written while the leave is processed (MatchTelemetry closes its file on leaving), i.e. AFTER the zip made
        // at the button press: zip once more now that the leave is complete.
        Overpower.Telemetry.MatchLogZip.Instance?.ZipNow();

        PlayerLookup.Clear();
        Debug.Log($"[NAME SCREEN] rebuilding the scene ({PhotonNetwork.NetworkClientState})");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
    /// "teamID" is reset too (null removes it): a stale team must not open the next lobby's match log (MatchPropertyReset).
    /// Kept: weapon/attachment/ultimate/mobility (LoadoutProperties) - PlayerLoadout.Start republishes
    /// the whole starting kit for every newly spawned player regardless of what is still on the local
    /// Custom Properties, so there is nothing here for a stale pick to leak into a new match. Treated
    /// the same as the nickname: a player-level preference that carries forward until the player
    /// changes it themselves, not a fact about the match just played.</summary>
    public override void OnLeftRoom()
    {
        // Task 9e: this also fires when the connection drops (or the game closes). That is not a new match: the room keeps this
        // player's Player Properties for the rejoin window and gives them back on a rejoin, so resetting them here (and
        // reading them back later) would only risk the very state a rejoin exists to keep. Only a deliberate leave resets.
        if (RejoinRules.LeaveIsADisconnect(PhotonNetwork.NetworkClientState))
        {
            Debug.Log("[REJOIN] connection dropped - match properties kept for a rejoin");
            return;
        }

        ResetMatchProperties();
    }

    /// <summary>Task 9e-2: puts the local player's match properties back to a fresh player's (MatchPropertyReset is the list). Called by a
    /// deliberate leave (OnLeftRoom) and when a rejoin is given up (Leave / OK on the rejoin panel): without it the NEXT match would
    /// inherit the old one's gold, loadout, dead flag and stats. A lost connection that may still be rejoined does not call it.</summary>
    public void ResetMatchProperties()
    {
        var props = new Hashtable();
        foreach (var pair in MatchPropertyReset.Build())
            props[pair.Key] = pair.Value;
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    /// <summary>Spawns this client's body on a team (lobby Task 4: LobbyStart calls it when the game starts; the rejoin watchdog
    /// below calls it for a body that never came back).</summary>
    public void SpawnPlayerOnTeam(int teamID)
    {
        if (!ValidateTeamResources(teamID)) return;

        GameObject player = PhotonNetwork.Instantiate(
            playerPrefab.name,
            teamSpawnPoints[teamID].position,
            Quaternion.identity,
            0,
            // The picked team travels with the spawn (Task 9b-3): a fresh process has no team property of its own yet
            // when the player's Start runs (PUN applies the local player's own properties on the server echo, after
            // this Instantiate), and an in-process rejoiner still carries its OLD team. PlayerLifecycle's join-into-a-
            // last-stand check reads this instead (PlayerLifecycle.InstantiatedTeam).
            new object[] { teamID }
        );

        PhotonView spawnedView = player.GetComponent<PhotonView>();
        spawnedBodyViewId = spawnedView != null ? spawnedView.ViewID : -1;

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

    /// <summary>Kept as a safety net after lobby Task 4: a player now always gets a team from a seat at Start, and the seats
    /// only hold teams of the mode, so a seated player is on a team that is in the match and nothing needs re-picking. Callers
    /// (the rejoin branch, MatchDirector's teams-fixed and live edges) still ask which team the local player is on: this
    /// returns it (-1 when none) and only logs when it finds the team left out of a fixed match, which the seats make impossible
    /// today. It never moves anyone (the old smallest-team re-pick is gone).</summary>
    public int EnsureLocalTeamInMatch()
    {
        MatchDirector director = MatchDirector.Instance;
        if (director == null || !Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam))
            return -1;

        if (director.TeamsFixed && !director.MayJoinTeam(myTeam))
            Debug.LogWarning($"[TEAM] team {myTeam} is not in the match although this player is seated on it - no re-pick any more");
        return myTeam;
    }
}
