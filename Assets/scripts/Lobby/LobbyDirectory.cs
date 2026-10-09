using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Photon.Pun;
using Overpower.UI;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Lobby
{
    /// <summary>
    /// Lobbies as Photon rooms: keeps the live list of rooms from Photon's lobby, creates a lobby (a room carrying its name,
    /// mode, stage and host as Room Properties) and joins one by name. It sits on the RoomManager's GameObject and is added
    /// by RoomManager.Awake, so the scene file does not change. The list screen reads Entries and ListChanged.
    /// </summary>
    public sealed class LobbyDirectory : MonoBehaviourPunCallbacks
    {
        /// <summary>How often a create is retried with a fresh room name when Photon says the name is taken.</summary>
        public const int MaxCreateRetries = 3;

        private RoomManager roomManager;
        private GameModeCatalogue catalogue;
        private LobbyConfig config;

        private readonly Dictionary<string, LobbyEntry> cache = new Dictionary<string, LobbyEntry>();
        private IReadOnlyList<LobbyEntry> sorted = new List<LobbyEntry>();
        private bool sortedStale;

        private bool pendingJoin;
        private string pendingName;
        private GameModeDefinition pendingMode;
        private int createRetries;

        /// <summary>The lobbies in the order the list shows them: joinable, then in match, then full; newest first in each.</summary>
        public IReadOnlyList<LobbyEntry> Entries
        {
            get
            {
                if (sortedStale)
                {
                    sorted = LobbyListCache.Sorted(cache.Values);
                    sortedStale = false;
                }
                return sorted;
            }
        }

        /// <summary>Raised whenever the list changed (a room appeared, changed or went) or was cleared.</summary>
        public event Action ListChanged;

        /// <summary>Raised when a join was refused for a reason the player can read (full, closed, gone).</summary>
        public event Action<string> JoinFailed;

        /// <summary>Raised when a lobby could not be created.</summary>
        public event Action<string> CreateFailed;

        public void Init(RoomManager manager, GameModeCatalogue modes, LobbyConfig lobbyConfig)
        {
            roomManager = manager;
            catalogue = modes;
            config = lobbyConfig;
            if (config == null)
                Debug.LogError("[LOBBY] RoomManager has no LobbyConfig assigned - lobby names are cut at " + LobbyConfig.DefaultLobbyNameMaxLength + " characters and duplicate names use the format \"" + LobbyConfig.DefaultDuplicateNameFormat + "\".");
        }

        /// <summary>Starts receiving the room list: joins Photon's default lobby when connected and not already in a room or
        /// lobby. Safe to call again.</summary>
        public void EnterList()
        {
            if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.InRoom || PhotonNetwork.InLobby) return;
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }

        public bool Create(string displayName, GameModeDefinition mode)
        {
            pendingName = displayName;
            pendingMode = mode;
            createRetries = 0;
            if (CreatePending()) return true;
            pendingMode = null; // a refused call leaves nothing pending
            return false;
        }

        internal bool HasPendingCreate => pendingMode != null; // a test hook

        public void Join(string roomName)
        {
            pendingJoin = PhotonNetwork.JoinRoom(roomName); // a refused call leaves no flag behind
        }

        private bool CreatePending()
        {
            int maxLen = config != null ? config.LobbyNameMaxLength : LobbyConfig.DefaultLobbyNameMaxLength;
            string name = (pendingName ?? "").Trim();
            if (name.Length > maxLen) name = name.Substring(0, maxLen);

            var props = new Hashtable
            {
                { LobbyKeys.Name, name },
                { LobbyKeys.Mode, pendingMode.Id },
                { LobbyKeys.Stage, 0 },
                { LobbyKeys.Host, PhotonNetwork.NickName ?? "" },
                { LobbyKeys.Created, PhotonNetwork.ServerTimestamp },
            };
            // Absent reads as three teams (MatchStartRules.LobbyModeOf), so only the two-team mode writes the key.
            if (pendingMode.LobbyModeValue == MatchStartRules.TwoTeams)
                props[MatchDirector.LobbyModeKey] = pendingMode.LobbyModeValue;

            var options = new RoomOptions
            {
                MaxPlayers = (byte)LobbySeatRules.TotalSeats(SeatLayoutFactory.From(pendingMode)),
                // Same rejoin window as every room: part of the room's options, so every build in a room must match.
                PlayerTtl = RejoinRules.PlayerTtlMs(roomManager != null ? roomManager.RejoinWindowSeconds : 0f),
                IsVisible = true,
                IsOpen = true,
                CustomRoomProperties = props,
                CustomRoomPropertiesForLobby = LobbyKeys.ForLobby,
            };
            string roomName = "L_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Debug.Log($"[LOBBY] creating {roomName} \"{name}\" mode {pendingMode.Id}, {options.MaxPlayers} places");
            return PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default);
        }

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            if (pendingMode == null) return;
            if (returnCode == ErrorCode.GameIdAlreadyExists && createRetries < MaxCreateRetries)
            {
                createRetries++;
                if (CreatePending()) return; // a refused retry falls through to the failure below
            }
            Debug.LogWarning($"[LOBBY] could not create a lobby ({returnCode}): {message}");
            pendingMode = null;
            CreateFailed?.Invoke(message);
        }

        public override void OnCreatedRoom()
        {
            pendingMode = null;
        }

        /// <summary>A player inside a room (or just back from one) must never see an old list: Photon only reports the changes
        /// from the moment of entering the lobby, so what was cached before is stale.</summary>
        public override void OnJoinedRoom()
        {
            pendingJoin = false;
            Clear();
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            // RejoinController is added to the RoomManager before this component, so it has already seen this failure: while a
            // held-place rejoin runs (or its retries fail) the player is not "joining a lobby", and nothing is reported here.
            bool wasJoining = pendingJoin;
            pendingJoin = false;
            bool rejoinBusy = roomManager != null && roomManager.Rejoin != null && roomManager.Rejoin.CurrentStage != RejoinController.Stage.Idle;
            if (!wasJoining || rejoinBusy) return;

            Debug.Log($"[LOBBY] join refused ({returnCode}): {message}");
            JoinFailed?.Invoke(JoinFailureText(returnCode, roomManager != null ? roomManager.Theme : null));
        }

        /// <summary>A join that went through but then had to be undone (a running lobby with no free seat): reported to the
        /// list like a refused join.</summary>
        public void ReportJoinRefused(string reason) => JoinFailed?.Invoke(reason);

        // What the list says when the theme is missing (a scene without its UiTheme): the same words the theme starts with.
        internal const string FallbackFullText = "That lobby is full.";
        internal const string FallbackClosedText = "That lobby has closed.";
        internal const string FallbackGoneText = "That lobby no longer exists.";
        internal const string FallbackFailedText = "Could not join that lobby.";

        /// <summary>The short line that tells why a join was refused, in the theme's words. A missing theme logs an error and gives the
        /// built-in words instead, so the player still reads why.</summary>
        public static string JoinFailureText(short returnCode, UiTheme theme)
        {
            if (theme == null)
            {
                Debug.LogError("[LOBBY] no UiTheme on the RoomManager - the join failure is shown in the built-in words");
                if (returnCode == ErrorCode.GameFull) return FallbackFullText;
                if (returnCode == ErrorCode.GameClosed) return FallbackClosedText;
                if (returnCode == ErrorCode.GameDoesNotExist) return FallbackGoneText;
                return FallbackFailedText;
            }
            if (returnCode == ErrorCode.GameFull) return theme.lobbyJoinFullText;
            if (returnCode == ErrorCode.GameClosed) return theme.lobbyJoinClosedText;
            if (returnCode == ErrorCode.GameDoesNotExist) return theme.lobbyJoinGoneText;
            return theme.lobbyJoinFailedText;
        }

        public override void OnRoomListUpdate(List<RoomInfo> roomList)
        {
            var snapshots = new List<RoomSnapshot>(roomList.Count);
            foreach (RoomInfo info in roomList)
            {
                snapshots.Add(new RoomSnapshot
                {
                    Name = info.Name,
                    RemovedFromList = info.RemovedFromList,
                    IsOpen = info.IsOpen,
                    IsVisible = info.IsVisible,
                    PlayerCount = info.PlayerCount,
                    MaxPlayers = info.MaxPlayers,
                    Properties = info.CustomProperties,
                });
            }
            LobbyListCache.Merge(cache, snapshots, System.Diagnostics.Stopwatch.GetTimestamp(), LayoutOfMode);
            sortedStale = true;
            ListChanged?.Invoke();
        }

        private SeatLayout? LayoutOfMode(int modeId)
        {
            GameModeDefinition mode = catalogue != null ? catalogue.ById(modeId) : null;
            return mode != null ? SeatLayoutFactory.From(mode) : (SeatLayout?)null;
        }

        public override void OnLeftLobby() => Clear();

        /// <summary>Photon sends the whole list again on entering the lobby but only reports the rooms still there, not the ones
        /// that went while this client was in a room (leaving a room does not raise OnLeftLobby), so start from nothing.</summary>
        public override void OnJoinedLobby() => Clear();

        public override void OnDisconnected(DisconnectCause cause) => Clear();

        private void Clear()
        {
            if (cache.Count == 0 && !sortedStale) return;
            cache.Clear();
            sortedStale = true;
            ListChanged?.Invoke();
        }

        /// <summary>The host's name on the list follows the master client.</summary>
        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            if (PhotonNetwork.LocalPlayer == null || !PhotonNetwork.LocalPlayer.IsMasterClient || PhotonNetwork.CurrentRoom == null) return;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { LobbyKeys.Host, PhotonNetwork.NickName ?? "" } });
        }
    }
}
