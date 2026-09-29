using System;
using Overpower.Match;
using Overpower.UI;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Overpower.Net
{
    /// <summary>
    /// Task 9e (Tudor D21): getting a dropped player back into the same match as the same player.
    ///
    /// The room keeps a dropped player's slot for the rejoin window (RoomOptions.PlayerTtl, from GameplayConfig), so their
    /// team, gold and loadout (Player Properties) and their networked body are all still there. This class is the client
    /// side of coming back:
    ///  - while playing it remembers the room in a small file next to the game (PlayerIdentity), refreshed every few
    ///    seconds, so a crash or a closed game still leaves "which room, and when I was last in it";
    ///  - a disconnect nobody asked for shows the "Connection lost - Rejoin" panel; Rejoin reconnects
    ///    (PhotonNetwork.ReconnectAndRejoin, else a fresh connect and RejoinRoom by name);
    ///  - after a restart the name screen asks <see cref="RejoinOffered"/> and offers "Rejoin your match" while the window is
    ///    open (RejoinRules.IsOffered); pressing it runs <see cref="RejoinFromNameScreen"/>;
    ///  - when the room is gone (over, or the window ran out) the player gets a clear message and goes back to the name
    ///    screen for a normal join.
    ///
    /// Added to the RoomManager's object by RoomManager.Awake. It does not spawn anything: the master removes a dropped player's
    /// body (see below), RoomManager spawns the returning player a new one, and PlayerLifecycle respawns it as after a death.
    /// </summary>
    public sealed class RejoinController : MonoBehaviourPunCallbacks
    {
        /// <summary>How often the saved match is refreshed while playing. A crash leaves the timestamp at most this far behind.</summary>
        public const float SaveIntervalSeconds = 10f;

        public enum Stage { Idle, Lost, Working, Failed }

        private UiTheme theme;
        private float windowSeconds;
        private ConnectionLostPanel panel;

        private Stage stage = Stage.Idle;
        private bool wasInRoom;
        private string lastRoomName;
        private string pendingRoomAtMaster;
        private float nextSaveAt;

        /// <summary>Raised when the player is sent back to the name screen (the failure message's OK, or Leave): whoever owns
        /// the name screen shows it again.</summary>
        public event Action ReturnToNameScreen;

        public Stage CurrentStage => stage;

        /// <summary>Called once by RoomManager, before the first connection.</summary>
        public void Init(UiTheme uiTheme, float rejoinWindowSeconds)
        {
            theme = uiTheme;
            windowSeconds = rejoinWindowSeconds;

            panel = gameObject.AddComponent<ConnectionLostPanel>();
            panel.Build(theme);
            panel.RejoinClicked += OnRejoinClicked;
            panel.LeaveClicked += OnLeaveClicked;
            panel.OkClicked += OnLeaveClicked;
        }

        /// <summary>True while the name screen should offer "Rejoin your match": nothing else is happening, and the saved
        /// match is one this id may still return to (RejoinRules.IsOffered). Reads a small file - poll it about once a second,
        /// not every frame.</summary>
        public bool RejoinOffered()
        {
            if (stage != Stage.Idle || PhotonNetwork.InRoom)
                return false;
            return RejoinRules.IsOffered(PlayerIdentity.LoadLastMatch(), PlayerIdentity.UserId, PlayerIdentity.NowMs(), Mathf.RoundToInt(windowSeconds));
        }

        // ---- a dropped player's body ----
        //
        // PUN keeps a dropped ("inactive") player's body and hands its control to the MASTER until they return or their
        // place runs out - so on the master its photonView.IsMine turns true and every IsMine-gated script (health, abilities,
        // portals, packs) would run for a standing statue as if it were the master's own player. Nobody should be standing
        // there anyway. So the master removes the body the moment the drop is known: every client loses it, and the server
        // drops its buffered spawn, so a rejoin gets NO old body back - the returning player spawns a new one
        // (RoomManager.SpawnFreshBodyIfNoneReturns) and respawns as after a death.

        private readonly System.Collections.Generic.List<int> bodiesToRemove = new System.Collections.Generic.List<int>();

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            // Removed next frame: PUN hands the body to the master in its own callback, which may run after this one.
            if (otherPlayer != null && !PresenceRules.IsPresent(otherPlayer.IsInactive) && PhotonNetwork.IsMasterClient
                && !bodiesToRemove.Contains(otherPlayer.ActorNumber))
                bodiesToRemove.Add(otherPlayer.ActorNumber);
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            if (!PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
                return;
            // A drop the previous master never got to clean up.
            foreach (Player p in PhotonNetwork.CurrentRoom.Players.Values)
                if (!PresenceRules.IsPresent(p.IsInactive) && !bodiesToRemove.Contains(p.ActorNumber))
                    bodiesToRemove.Add(p.ActorNumber);
        }

        private void RemoveDroppedBodies()
        {
            for (int i = bodiesToRemove.Count - 1; i >= 0; i--)
            {
                int actor = bodiesToRemove[i];
                bodiesToRemove.RemoveAt(i);
                if (!PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
                    continue;
                Player owner = PhotonNetwork.CurrentRoom.GetPlayer(actor);
                if (owner == null || !owner.IsInactive)
                    continue; // already back, or gone for good (PUN removes its objects itself)

                PhotonView view = PlayerLookup.GetPhotonViewFor(actor);
                if (view == null)
                    continue;
                Debug.Log($"[REJOIN] actor {actor} dropped - the master removes its body; it spawns a new one when it rejoins");
                PhotonNetwork.Destroy(view.gameObject);
            }
        }

        // ---- remembering the match ----

        private void Update()
        {
            if (bodiesToRemove.Count > 0)
                RemoveDroppedBodies();
            RetryIfDue();

            if (!PhotonNetwork.InRoom || Time.unscaledTime < nextSaveAt)
                return;
            nextSaveAt = Time.unscaledTime + SaveIntervalSeconds;

            // A finished match has nothing to come back to: forget it instead of offering it after the next restart.
            MatchDirector director = MatchDirector.Instance;
            if (director != null && director.Phase == MatchPhase.Over)
            {
                PlayerIdentity.ClearLastMatch();
                return;
            }
            PlayerIdentity.SaveLastMatch(PhotonNetwork.CurrentRoom.Name, PhotonNetwork.NickName);
        }

        public override void OnJoinedRoom()
        {
            wasInRoom = true;
            lastRoomName = PhotonNetwork.CurrentRoom.Name;
            pendingRoomAtMaster = null;
            nextSaveAt = Time.unscaledTime + SaveIntervalSeconds;
            PlayerIdentity.SaveLastMatch(lastRoomName, PhotonNetwork.NickName);

            bool rejoined = PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.HasRejoined;
            Debug.Log($"[REJOIN] in room {lastRoomName} (rejoined={rejoined}) as {PlayerIdRule.ForLog(PlayerIdentity.UserId)}");

            stage = Stage.Idle;
            panel.Hide();
        }

        public override void OnLeftRoom()
        {
            // Fires for a lost connection too - that is not a decision to leave, and the saved match must survive it.
            if (RejoinRules.LeaveIsADisconnect(PhotonNetwork.NetworkClientState))
                return;

            wasInRoom = false;
            PlayerIdentity.ClearLastMatch();
        }

        // ---- the connection dropped ----

        public override void OnDisconnected(DisconnectCause cause)
        {
            if (stage == Stage.Working)
            {
                // The reconnect itself failed (no network yet, a timeout): back to the panel, Rejoin can be pressed again.
                Debug.LogWarning($"[REJOIN] the reconnect failed ({cause})");
                pendingRoomAtMaster = null;
                stage = Stage.Lost;
                panel.ShowLost();
                return;
            }

            bool loss = RejoinRules.IsConnectionLoss(cause, wasInRoom);
            if (loss)
            {
                Debug.LogWarning($"[REJOIN] connection lost ({cause}) in room {lastRoomName} - showing the rejoin panel");
                stage = Stage.Lost;
                panel.ShowLost();
            }
            wasInRoom = false;
        }

        private void OnRejoinClicked()
        {
            if (string.IsNullOrEmpty(lastRoomName))
            {
                RejoinRecord record = PlayerIdentity.LoadLastMatch();
                lastRoomName = record != null ? record.RoomName : null;
            }
            BeginRejoin(lastRoomName);
        }

        /// <summary>The name screen's "Rejoin your match": the saved room, under the saved name.</summary>
        public void RejoinFromNameScreen()
        {
            RejoinRecord record = PlayerIdentity.LoadLastMatch();
            if (record == null || !RejoinRules.IsOffered(record, PlayerIdentity.UserId, PlayerIdentity.NowMs(), Mathf.RoundToInt(windowSeconds)))
            {
                Debug.LogWarning("[REJOIN] no rejoin on offer any more");
                return;
            }
            if (!string.IsNullOrEmpty(record.Nick))
                PhotonNetwork.NickName = record.Nick;
            lastRoomName = record.RoomName;
            BeginRejoin(record.RoomName);
        }

        private void BeginRejoin(string roomName)
        {
            if (string.IsNullOrEmpty(roomName))
            {
                Fail("no saved room");
                return;
            }

            stage = Stage.Working;
            retries = 0;
            pendingRetryRoom = null;
            panel.ShowWorking();
            Debug.Log($"[REJOIN] rejoining {roomName} as {PlayerRejoinLog()}");

            NetworkClientStateSnapshot(out bool disconnected, out bool ready);
            if (ready)
            {
                if (!PhotonNetwork.RejoinRoom(roomName))
                    Fail("RejoinRoom refused");
                return;
            }

            pendingRoomAtMaster = roomName;
            if (disconnected)
            {
                // The quick way back (the same game server and token) first; without a game server to return to (a fresh process, a
                // stale token) connect to the master and rejoin by name.
                if (PhotonNetwork.ReconnectAndRejoin())
                {
                    pendingRoomAtMaster = null;
                    return;
                }
                PhotonNetwork.ConnectUsingSettings();
            }
            // else: connecting already (RoomManager.Start) - OnConnectedToMaster below carries on.
        }

        private static string PlayerRejoinLog() => PlayerIdRule.ForLog(PlayerIdentity.UserId);

        private static void NetworkClientStateSnapshot(out bool disconnected, out bool ready)
        {
            disconnected = PhotonNetwork.NetworkClientState == ClientState.Disconnected || PhotonNetwork.NetworkClientState == ClientState.PeerCreated;
            ready = PhotonNetwork.IsConnectedAndReady && PhotonNetwork.Server == ServerConnection.MasterServer;
        }

        public override void OnConnectedToMaster()
        {
            if (stage != Stage.Working || string.IsNullOrEmpty(pendingRoomAtMaster))
                return;

            string room = pendingRoomAtMaster;
            pendingRoomAtMaster = null;
            if (!PhotonNetwork.RejoinRoom(room))
                Fail("RejoinRoom refused");
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            if (stage != Stage.Working)
                return;
            Debug.LogWarning($"[REJOIN] rejoin refused ({returnCode}): {message}");

            // Restarting within seconds of a crash: the server has not noticed the old connection is dead yet and still calls this
            // player active (JoinFailedFoundActiveJoiner). It times the old one out on its own - keep trying for a while.
            if (RejoinRules.ShouldRetryRejoin(returnCode, retries, MaxRetries))
            {
                retries++;
                retryAt = Time.unscaledTime + RetrySeconds;
                pendingRetryRoom = string.IsNullOrEmpty(lastRoomName) ? null : lastRoomName;
                return;
            }
            Fail(message);
        }

        private const int MaxRetries = 15;
        private const float RetrySeconds = 2f;
        private int retries;
        private float retryAt = -1f;
        private string pendingRetryRoom;

        private void RetryIfDue()
        {
            if (pendingRetryRoom == null || Time.unscaledTime < retryAt)
                return;
            string room = pendingRetryRoom;
            pendingRetryRoom = null;
            if (stage != Stage.Working)
                return;
            Debug.Log($"[REJOIN] retrying {room} ({retries}/{MaxRetries})");
            if (!PhotonNetwork.IsConnectedAndReady || !PhotonNetwork.RejoinRoom(room))
                Fail("retry could not be sent");
        }

        private void Fail(string why)
        {
            Debug.LogWarning($"[REJOIN] cannot return to the match: {why}");
            pendingRoomAtMaster = null;
            pendingRetryRoom = null;
            PlayerIdentity.ClearLastMatch();
            stage = Stage.Failed;
            panel.ShowFailed();
        }

        /// <summary>Leave, or OK on the failure message: give up the place and go back to the name screen for a normal join.</summary>
        private void OnLeaveClicked()
        {
            pendingRoomAtMaster = null;
            wasInRoom = false;
            PlayerIdentity.ClearLastMatch();
            stage = Stage.Idle;
            panel.Hide();

            if (PhotonNetwork.NetworkClientState == ClientState.Disconnected || PhotonNetwork.NetworkClientState == ClientState.PeerCreated)
                PhotonNetwork.ConnectUsingSettings(); // the name screen's Join waits for the master (JoinGameUI.joinRequestedEarly)
            ReturnToNameScreen?.Invoke();
        }
    }
}
