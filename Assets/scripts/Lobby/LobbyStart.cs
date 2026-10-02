using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Overpower.Net;
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
            while (MayStartGame)
            {
                if (Time.unscaledTime - began > GiveUpSeconds)
                {
                    Debug.LogWarning($"[LOBBY] Start game gave up after {GiveUpSeconds:0} s: the room kept refusing it");
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
            // A player coming back to a held place (HasRejoined) is looked after by RoomManager's rejoin branch.
            if (lastStage >= 1 && !PhotonNetwork.LocalPlayer.HasRejoined)
                ReactToStart();
        }

        public override void OnLeftRoom()
        {
            lastStage = 0;
            if (roomManager != null) roomManager.SeatView?.End();
            if (starting != null) StopCoroutine(starting);
            starting = null;
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (!changed.ContainsKey(LobbyKeys.Stage)) return;
            int stage = StageOfRoom();
            int before = lastStage;
            lastStage = stage;
            if (before < 1 && stage >= 1) ReactToStart();
        }

        /// <summary>The game has started and this client was just told: its seat decides what it becomes.</summary>
        private void ReactToStart()
        {
            string seat = seats.SeatInRoom();
            if (seat == null)
            {
                Debug.Log("[LOBBY] the game started and this player has no seat (a late joiner, lobby Task 7) - nothing to do yet");
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
