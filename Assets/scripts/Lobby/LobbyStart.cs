using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;
using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Lobby
{
    /// <summary>
    /// The host pressing Start game, and what every client does when the lobby starts (lobby Task 4). Start places everyone
    /// still in No role (the seat rules' AutoFill), locks the seats by moving the stage from the lobby to the warm-up, all
    /// in one compare-and-swap write; then every client sees the stage change (the lS 0 to 1 edge) and its own seat decides
    /// what it becomes: a team seat spawns a body on that team, a spectator seat gets no body. It sits next to LobbySeats on
    /// the RoomManager's GameObject (added by RoomManager.Awake, so the scene file does not change). The lobby screen
    /// (lobby Task 10) reads MayStartGame and calls StartGame.
    /// </summary>
    public sealed class LobbyStart : MonoBehaviourPunCallbacks
    {
        /// <summary>How long a Start that keeps being refused (someone sat down in the same instant) is retried before giving up.</summary>
        private const float GiveUpSeconds = 3f;

        /// <summary>How long we wait for the room's answer to a sent Start before sending it again.</summary>
        private const float ResendSeconds = 0.5f;

        private RoomManager roomManager;
        private LobbySeats seats;

        private int lastStage;
        private bool seatsChangedSinceSend;
        private Coroutine starting;

        public void Init(RoomManager manager, LobbySeats lobbySeats)
        {
            roomManager = manager;
            seats = lobbySeats;
            seats.SeatsChanged += () => seatsChangedSinceSend = true;
        }

        /// <summary>The host may press Start now: this client is the master, the stage is still the lobby and every team of the
        /// mode would have a player once No role players are placed.</summary>
        public bool MayStartGame =>
            seats != null && seats.HasLayout && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient
            && StageOfRoom() == LobbySeatRules.LobbyBeforeStart
            && LobbySeatRules.MayStartGame(seats.Layout, seats.Seats, seats.NoRoleActors);

        /// <summary>The room's game has started and this player holds no seat in it (a late joiner, or a returning player whose seat was freed).
        /// Only said once the room's layout is read: before that the seat cannot be told apart from one not yet readable.</summary>
        public bool GameRunningWithoutMySeat =>
            LobbySeatRules.GameRunningWithoutSeat(PhotonNetwork.InRoom, StageOfRoom(), seats.HasLayout, seats.SeatInRoom());

        /// <summary>Starts the game (master only). Nothing is sent while MayStartGame is false. The write is refused by the room
        /// when a seat changed in the same instant; it is then sent again with the fresh seats, for up to a few seconds.</summary>
        public void StartGame()
        {
            if (!MayStartGame)
            {
                Debug.Log("[LOBBY] Start game refused: not the host, already started, or a team would stay empty");
                return;
            }
            if (starting != null) return;
            starting = StartCoroutine(StartUntilItLands());
        }

        private IEnumerator StartUntilItLands()
        {
            float began = Time.unscaledTime;
            float sentAt = -1f;
            bool timedOut = false;
            while (MayStartGame)
            {
                if (Time.unscaledTime - began > GiveUpSeconds)
                {
                    timedOut = true;
                    break;
                }
                if (sentAt < 0f || seatsChangedSinceSend || Time.unscaledTime - sentAt >= ResendSeconds)
                {
                    seatsChangedSinceSend = false;
                    SeatWrite write = LobbySeatRules.StartWrite(seats.Layout, seats.Seats, seats.NoRoleActors);
                    bool sent = PhotonNetwork.CurrentRoom.SetCustomProperties(ToHashtable(write.Props), ToHashtable(write.Expected));
                    sentAt = Time.unscaledTime;
                    Debug.Log("[LOBBY] Start game " + (sent ? "sent" : "not sent"));
                }
                yield return null;
            }
            starting = null;
            // Leaving with the stage still the lobby means the start did not land: say why (the loop also ends when it did land).
            if (StageOfRoom() == LobbySeatRules.LobbyBeforeStart)
                Debug.LogWarning("[LOBBY] Start game stopped with the lobby still open: " + WhyStartStopped(timedOut));
        }

        private string WhyStartStopped(bool timedOut)
        {
            if (timedOut) return $"the room kept refusing it for {GiveUpSeconds:0} s";
            if (!PhotonNetwork.InRoom) return "this player left the room";
            if (!PhotonNetwork.IsMasterClient) return "mastership moved to another player";
            return "a team of the mode has no player any more";
        }

        private static Hashtable ToHashtable(Dictionary<string, object> values)
        {
            var table = new Hashtable();
            foreach (var pair in values) table[pair.Key] = pair.Value;
            return table;
        }

        private static int StageOfRoom()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return 0;
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            return props.ContainsKey(LobbyKeys.Stage) && props[LobbyKeys.Stage] is int stage ? stage : 0;
        }

        // ---- the lS 0 to 1 edge, on every client ----

        public override void OnJoinedRoom()
        {
            lastStage = StageOfRoom();
            if (lastStage < 1) return;

            // The game is already running. No seat: a late joiner, who is given one (lobby Task 7).
            string seat = seats.SeatInRoom();
            if (seat == null)
            {
                StartLateJoin();
                return;
            }
            // A player coming back to a held place (HasRejoined) with a team seat and a team is looked after by RoomManager's rejoin
            // branch (its body comes back or is respawned by WatchOwnBodyAfterRejoin). The others still need the seat's reaction: a
            // spectator seat starts the spectator view again, and a team seat that never spawned (the player dropped in the lobby and
            // missed the lS 0 to 1 edge) gets its body now.
            if (PhotonNetwork.LocalPlayer.HasRejoined
                && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out _) && !Teams.IsSpectator(PhotonNetwork.LocalPlayer))
                return;
            ReactToStart();
        }

        public override void OnLeftRoom()
        {
            lastStage = 0;
            if (roomManager != null) roomManager.SeatView?.End();
            if (starting != null)
            {
                StopCoroutine(starting);
                Debug.LogWarning("[LOBBY] Start game stopped with the lobby still open: " + WhyStartStopped(false));
            }
            starting = null;
            if (lateJoining != null) StopCoroutine(lateJoining);
            lateJoining = null;
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (!changed.ContainsKey(LobbyKeys.Stage)) return;
            int stage = StageOfRoom();
            int before = lastStage;
            lastStage = stage;
            if (before < 1 && stage >= 1) ReactToStart();
        }

        // ---- joining a running lobby (lobby Task 7) ----

        /// <summary>How many seats a late joiner tries before giving up (each try is refused only when another joiner took the same seat first).</summary>
        private const int LateJoinAttempts = 6;

        /// <summary>How long a late joiner waits for the room's answer to one seat write before looking again.</summary>
        private const float LateJoinWaitSeconds = 1.5f;

        private Coroutine lateJoining;

        private void StartLateJoin()
        {
            if (lateJoining != null) return;
            lateJoining = StartCoroutine(PlaceLateJoinerInRoom());
        }

        /// <summary>The joiner has no seat in a lobby whose game is running: the emptiest team that is in the match, else a spectator seat.
        /// The write expects the seat empty and the stage as seen, so it is refused when someone got there first (look again, a few times).
        /// Nothing free: leave the room, with the reason for the list. Once the seat is theirs it acts like the start edge (team: body; spectator seat: view).</summary>
        private IEnumerator PlaceLateJoinerInRoom()
        {
            for (int attempt = 0; attempt < LateJoinAttempts; attempt++)
            {
                if (!PhotonNetwork.InRoom || !seats.HasLayout)
                    break;
                if (seats.SeatInRoom() != null)
                {
                    lateJoining = null;
                    ReactToStart();
                    yield break;
                }

                int stage = StageOfRoom();
                if (stage < LobbySeatRules.LobbyWarmup)
                    break; // not started after all: an ordinary seatless player in the lobby
                Dictionary<string, int> fresh = LobbySeats.SeatsFrom(PhotonNetwork.CurrentRoom.CustomProperties, seats.Layout);
                var roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
                int[] fixedTeams = roomProps.TryGetValue(MatchDirector.TeamsInMatchKey, out object raw) && raw is int[] arr ? arr : null;
                // A team knocked out stays in mTeams (it is listed in mElim): nobody may be seated on it.
                int[] eliminated = roomProps.TryGetValue(MatchDirector.EliminatedKey, out object rawElim) && rawElim is int[] elim ? elim : null;
                string seat = LobbySeatRules.PlaceLateJoiner(seats.Layout, fresh, LobbySeatRules.TeamsForLateJoin(seats.Layout, fixedTeams, eliminated));
                if (seat == null)
                {
                    GiveUpLateJoin(LateJoinText(roomManager.Theme, noSeatInTime: false));
                    yield break;
                }

                SeatWrite write = LobbySeatRules.LateJoinWrite(PhotonNetwork.LocalPlayer.ActorNumber, seat, stage);
                bool sent = PhotonNetwork.CurrentRoom.SetCustomProperties(ToHashtable(write.Props), ToHashtable(write.Expected));
                Debug.Log($"[LOBBY] late join: asking for seat {seat} (try {attempt + 1}, stage {stage}) - " + (sent ? "sent" : "not sent"));

                // Wait for the room's answer: our seat appears, or the seats or stage move on (our write was refused), or it times out.
                float waited = 0f;
                while (waited < LateJoinWaitSeconds && PhotonNetwork.InRoom && seats.SeatInRoom() == null
                       && StageOfRoom() == stage && SameSeats(fresh, LobbySeats.SeatsFrom(PhotonNetwork.CurrentRoom.CustomProperties, seats.Layout)))
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            // The last try's answer may still be on its way: look once more, for as long as one try waits, before giving up.
            float lastWait = 0f;
            while (lastWait < LateJoinWaitSeconds && PhotonNetwork.InRoom && seats.SeatInRoom() == null && StageOfRoom() >= LobbySeatRules.LobbyWarmup)
            {
                lastWait += Time.unscaledDeltaTime;
                yield return null;
            }
            if (PhotonNetwork.InRoom && seats.SeatInRoom() == null && StageOfRoom() >= LobbySeatRules.LobbyWarmup)
            {
                GiveUpLateJoin(LateJoinText(roomManager.Theme, noSeatInTime: true));
                yield break;
            }
            lateJoining = null;
            if (PhotonNetwork.InRoom && seats.SeatInRoom() != null)
                ReactToStart();
        }

        private static bool SameSeats(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var pair in a)
                if (!b.TryGetValue(pair.Key, out int other) || other != pair.Value) return false;
            return true;
        }

        internal const string FallbackLateJoinFullText = "Lobby full";
        internal const string FallbackLateJoinNoSeatText = "Could not get a seat";

        /// <summary>The words shown when a late join gives up: the theme's (Lobby full, or Could not get a seat), the built-in ones with an error
        /// in the log when the theme is missing.</summary>
        internal static string LateJoinText(UiTheme theme, bool noSeatInTime)
        {
            if (theme == null)
            {
                Debug.LogError("[LOBBY] no UiTheme on the RoomManager - the late-join failure is shown in the built-in words");
                return noSeatInTime ? FallbackLateJoinNoSeatText : FallbackLateJoinFullText;
            }
            return noSeatInTime ? theme.lobbyLateJoinNoSeatText : theme.lobbyLateJoinFullText;
        }

        private void GiveUpLateJoin(string reason)
        {
            lateJoining = null;
            Debug.LogWarning($"[LOBBY] late join failed: {reason} - leaving the room");
            PhotonNetwork.LeaveRoom(becomeInactive: false);
            roomManager.Lobbies.ReportJoinRefused(reason);
        }

        /// <summary>The game has started and this client was just told: its seat decides what it becomes.</summary>
        private void ReactToStart()
        {
            string seat = seats.SeatInRoom();
            if (seat == null)
            {
                // Someone who was in the room without a seat when the stage moved (they entered during the start write): like a late joiner.
                StartLateJoin();
                return;
            }
            if (LobbySeatRules.TryTeamOfSeat(seat, out int team))
            {
                if (PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber) != null)
                {
                    Debug.Log($"[LOBBY] seat {seat}: already has a body - not spawning another");
                    return;
                }
                Debug.Log($"[LOBBY] seat {seat}: spawning on team {team}");
                roomManager.SpawnPlayerOnTeam(team);
            }
            else if (LobbySeatRules.IsSpectatorSeat(seat))
            {
                Debug.Log($"[LOBBY] spectating (seat {seat}) - no body");
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { Teams.SpectatorKey, true } });
                roomManager.SeatView?.Begin();
            }
        }
    }
}
