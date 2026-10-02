using System;
using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Overpower.Data;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Lobby
{
    /// <summary>
    /// Who sits where inside a lobby, kept in the room's Custom Properties (one property per seat, holding the actor number
    /// of whoever sits there). Players take a seat, move or leave it with TryTake / LeaveSeat; every change is a Photon
    /// compare-and-swap, so two players clicking the same seat in the same instant get exactly one winner. The master
    /// keeps the room tidy: before Start a player who leaves or drops frees their seat at once, and the seat fill counts
    /// (lF) the lobby list shows are kept current. It sits next to LobbyDirectory on the RoomManager's GameObject (added by
    /// RoomManager.Awake, so the scene file does not change). The lobby screen (lobby Task 10) reads Seats, MySeat and
    /// NoRoleActors and listens to SeatsChanged.
    /// </summary>
    public sealed class LobbySeats : MonoBehaviourPunCallbacks
    {
        private GameModeCatalogue catalogue;
        private LobbyConfig config;

        private SeatLayout layout;
        private GameModeDefinition mode;
        private bool hasLayout;
        private Dictionary<string, int> seats = new Dictionary<string, int>();
        private readonly List<int> noRole = new List<int>();

        /// <summary>The nickname the player typed, kept while the room shows a numbered copy of it ("Tudor 2"); null when
        /// the name was not changed.</summary>
        private string typedNickName;

        /// <summary>Seat writes we sent that have not been echoed yet (seat key -> our actor): the first update of that seat
        /// key after our send tells whether the room took it or another player got there first.</summary>
        private readonly Dictionary<string, int> awaitingEcho = new Dictionary<string, int>();

        /// <summary>The seat this actor holds according to the room's properties right now (not the cached copy), or null. For
        /// code that reacts inside a Photon callback, where this component may not have processed the same update yet.</summary>
        public string SeatInRoom()
        {
            if (!hasLayout) ReadLayout();
            if (!hasLayout || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.LocalPlayer == null) return null;
            Dictionary<string, int> fresh = SeatsFrom(PhotonNetwork.CurrentRoom.CustomProperties, layout);
            return LobbySeatRules.SeatOf(PhotonNetwork.LocalPlayer.ActorNumber, layout, fresh);
        }

        /// <summary>The name the player typed: what to save for the next lobby, not the numbered copy ("Tudor 2") this lobby may
        /// show.</summary>
        public string TypedNickName => typedNickName ?? PhotonNetwork.NickName;

        /// <summary>The seats of the lobby the player is in, empty seats left out.</summary>
        public IReadOnlyDictionary<string, int> Seats => seats;

        /// <summary>The shape of the current lobby's seats (valid only while HasLayout).</summary>
        public SeatLayout Layout => layout;

        public bool HasLayout => hasLayout;

        /// <summary>The game mode of the current lobby (null until the room's mode is read; valid while HasLayout). The lobby room screen draws its
        /// teams and names from it.</summary>
        public GameModeDefinition Mode => hasLayout ? mode : null;

        /// <summary>Raised when any seat changed, someone entered or left (the No role list changed) and on joining.</summary>
        public event Action SeatsChanged;

        public void Init(GameModeCatalogue modes, LobbyConfig lobbyConfig)
        {
            catalogue = modes;
            config = lobbyConfig;
        }

        /// <summary>The seat key this client holds, or null for No role.</summary>
        public string MySeat =>
            hasLayout && PhotonNetwork.LocalPlayer != null ? LobbySeatRules.SeatOf(PhotonNetwork.LocalPlayer.ActorNumber, layout, seats) : null;

        /// <summary>Actor numbers of the present players with no seat, ascending.</summary>
        public IReadOnlyList<int> NoRoleActors => noRole;

        /// <summary>
        /// Asks for a seat (or to move there). True means the write was SENT, not that the seat is ours: the room accepts the
        /// first of two competing writes and drops the other, so the answer is read from the room (MySeat / SeatsChanged).
        /// </summary>
        public bool TryTake(string seatKey)
        {
            if (!CanWrite()) return false;
            var write = LobbySeatRules.TakeSeat(PhotonNetwork.LocalPlayer.ActorNumber, seatKey, layout, seats);
            bool sent = Send(write);
            Debug.Log($"[SEATS] take {seatKey}: " + (sent ? "sent" : "not sent (taken, not a seat, or already mine)"));
            if (sent) awaitingEcho[seatKey] = PhotonNetwork.LocalPlayer.ActorNumber;
            return sent;
        }

        /// <summary>Leaves the seat for No role. True means the write was sent (see TryTake).</summary>
        public bool LeaveSeat()
        {
            if (!CanWrite()) return false;
            return Send(LobbySeatRules.LeaveSeat(PhotonNetwork.LocalPlayer.ActorNumber, layout, seats));
        }

        /// <summary>Seats change only before Start: this refuses locally once the room's stage is no longer the lobby (the write
        /// itself also expects it, so the room refuses a stale one).</summary>
        private bool CanWrite() =>
            hasLayout && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.LocalPlayer != null && StageOfRoom() == LobbySeatRules.LobbyBeforeStart;

        private static int StageOfRoom()
        {
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            return props.ContainsKey(LobbyKeys.Stage) && props[LobbyKeys.Stage] is int stage ? stage : 0;
        }

        private static bool Send(SeatWrite write)
        {
            if (write.IsNone) return false;
            return PhotonNetwork.CurrentRoom.SetCustomProperties(ToHashtable(write.Props), ToHashtable(write.Expected));
        }

        private static Hashtable ToHashtable(Dictionary<string, object> values)
        {
            var table = new Hashtable();
            foreach (var pair in values) table[pair.Key] = pair.Value;
            return table;
        }

        /// <summary>The first update of a seat we asked for: if it shows another holder the room refused our write.</summary>
        private void LogRefusedWrites(Hashtable changed)
        {
            if (awaitingEcho.Count == 0) return;
            List<string> done = null;
            foreach (var pair in awaitingEcho)
            {
                if (!changed.ContainsKey(pair.Key)) continue;
                (done ?? (done = new List<string>())).Add(pair.Key);
                object raw = changed[pair.Key];
                int holder = raw is int i ? i : raw is short sh ? sh : raw is byte b ? b : 0;
                if (holder != pair.Value)
                    Debug.Log($"[SEATS] the room refused the write for {pair.Key}: " + (holder > 0 ? $"actor {holder} got it first" : "the seat is empty"));
            }
            if (done != null) foreach (string key in done) awaitingEcho.Remove(key);
        }

        /// <summary>The seats as the room's properties hold them: exactly the layout's seat keys, each with an actor number
        /// above zero; anything else (other properties, wrong types, zero or less) is left out.</summary>
        public static Dictionary<string, int> SeatsFrom(IDictionary props, SeatLayout layout)
        {
            var result = new Dictionary<string, int>();
            if (props == null) return result;
            foreach (string key in LobbySeatRules.AllSeatKeys(layout))
            {
                if (!props.Contains(key)) continue;
                object raw = props[key];
                int actor = raw is int i ? i : raw is short s ? s : raw is byte b ? b : 0;
                if (actor > 0) result[key] = actor;
            }
            return result;
        }

        // ---- the room ----

        public override void OnJoinedRoom()
        {
            ApplyUniqueName();
            ReadLayout();
            Refresh(true);
            MasterDuties();
        }

        public override void OnLeftRoom() => Reset();

        public override void OnDisconnected(DisconnectCause cause) => Reset();

        private void Reset()
        {
            RestoreTypedName();
            hasLayout = false;
            seats = new Dictionary<string, int>();
            noRole.Clear();
            awaitingEcho.Clear();
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (!hasLayout) ReadLayout();
            if (!hasLayout) return;
            LogRefusedWrites(changed);
            bool seatChanged = false;
            foreach (string key in LobbySeatRules.AllSeatKeys(layout))
                if (changed.ContainsKey(key)) { seatChanged = true; break; }
            // The stage can change too (Start, lobby Task 4): the master's duties depend on it.
            bool stageChanged = changed.ContainsKey(LobbyKeys.Stage);
            if (seatChanged) Refresh(true);
            if (seatChanged || stageChanged) MasterDuties();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer) => Refresh(true);

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            Refresh(true);
            MasterDuties();
        }

        public override void OnMasterClientSwitched(Player newMasterClient) => MasterDuties();

        private void ReadLayout()
        {
            hasLayout = false;
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            if (catalogue == null || !props.ContainsKey(LobbyKeys.Mode) || !(props[LobbyKeys.Mode] is int modeId)) return;
            mode = catalogue.ById(modeId);
            if (mode == null)
            {
                Debug.LogWarning($"[SEATS] the lobby's game mode {modeId} is not in the catalogue - no seats");
                return;
            }
            layout = SeatLayoutFactory.From(mode);
            hasLayout = true;
        }

        private void Refresh(bool raise)
        {
            if (!hasLayout || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
            seats = SeatsFrom(PhotonNetwork.CurrentRoom.CustomProperties, layout);
            noRole.Clear();
            foreach (Player p in PhotonNetwork.PlayerList)
                if (!p.IsInactive && LobbySeatRules.SeatOf(p.ActorNumber, layout, seats) == null)
                    noRole.Add(p.ActorNumber);
            noRole.Sort();
            if (raise) SeatsChanged?.Invoke();
        }

        // ---- the master keeps the room tidy ----

        private void MasterDuties()
        {
            if (!hasLayout || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
            FreeSeatsOfAbsentPlayers();
            WriteFill();
        }

        /// <summary>A player who left the room for good (quit through the menu, or the rejoin window ran out) loses the seat at once,
        /// at any stage; one who only dropped (inactive, may come back) loses it before Start and keeps it from Start on, for the
        /// rejoin window (LobbySeatRules.SweepWrites). Each write expects the seat to still be that player's.</summary>
        private void FreeSeatsOfAbsentPlayers()
        {
            var presence = new Dictionary<int, SeatHolderPresence>();
            foreach (var pair in seats)
            {
                if (presence.ContainsKey(pair.Value)) continue;
                presence[pair.Value] = !PhotonNetwork.CurrentRoom.Players.TryGetValue(pair.Value, out Player holder)
                    ? SeatHolderPresence.LeftForGood
                    : holder.IsInactive ? SeatHolderPresence.Inactive : SeatHolderPresence.Present;
            }
            foreach (SeatWrite write in LobbySeatRules.SweepWrites(seats, presence, StageOfRoom()))
            {
                // A player leaving and the mastership moving to us arrive in the same frame and both run this sweep: the identical write goes once.
                if (!sweepGate.ShouldSend(write.Signature(), Time.unscaledTime)) continue;
                Debug.Log($"[SEATS] master frees {write.Props.Count} seat(s) of players who left or dropped: {string.Join(", ", write.Props.Keys)}"
                    + (write.Expected.ContainsKey(LobbyKeys.Stage) ? " (a drop before Start)" : " (left for good)"));
                Send(write);
            }
        }

        private readonly RepeatWriteGate sweepGate = new RepeatWriteGate(1f);

        /// <summary>Keeps lF (the fill counts on the lobby list) equal to what the seats say.</summary>
        private void WriteFill()
        {
            string text = LobbySeatRules.FillText(layout, seats);
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            if (props.ContainsKey(LobbyKeys.Fill) && props[LobbyKeys.Fill] is string current && current == text) return;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { LobbyKeys.Fill, text } });
        }

        // ---- names ----

        /// <summary>A second player with a name already in the lobby is shown as "Name 2". The name the player typed stays
        /// saved for the next lobby: it is put back on leaving the room.</summary>
        private void ApplyUniqueName()
        {
            string current = PhotonNetwork.NickName ?? "";
            var others = new List<string>();
            foreach (Player p in PhotonNetwork.PlayerListOthers)
                if (!p.IsInactive) others.Add(p.NickName);
            string format = config != null ? config.DuplicateNameFormat : LobbyConfig.DefaultDuplicateNameFormat;
            string unique = PlayerNameRules.UniqueName(current, others, format);
            if (unique == current) return;
            if (typedNickName == null) typedNickName = current;
            Debug.Log($"[SEATS] the name {current} is taken in this lobby - shown as {unique}");
            PhotonNetwork.NickName = unique;
        }

        private void RestoreTypedName()
        {
            if (typedNickName == null) return;
            PhotonNetwork.NickName = typedNickName;
            typedNickName = null;
        }
    }
}
