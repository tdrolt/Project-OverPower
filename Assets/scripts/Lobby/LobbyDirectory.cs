using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Overpower.Data;
using Overpower.Match;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Lobby
{
    /// <summary>
    /// Lobbies as Photon rooms: keeps the live list of rooms from Photon's lobby, creates a lobby (a room carrying its name,
    /// mode, stage and host as Room Properties) and joins one by name. It sits on the RoomManager's GameObject and is added
    /// by RoomManager.Awake, so the scene file does not change. The list screen (lobby Task 9) reads Entries and ListChanged.
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
        }

        /// <summary>Starts receiving the room list: joins Photon's default lobby when connected and not already in a room or
        /// lobby. Safe to call again.</summary>
        public void EnterList()
        {
            if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.InRoom || PhotonNetwork.InLobby) return;
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }

        /// <summary>Creates a lobby: a new room, visible and open, carrying its name, mode, stage and host.</summary>
        public void Create(string displayName, GameModeDefinition mode)
        {
            pendingName = displayName;
            pendingMode = mode;
            createRetries = 0;
            CreatePending();
        }

        /// <summary>Joins the lobby with this room name.</summary>
        public void Join(string roomName)
        {
            PhotonNetwork.JoinRoom(roomName);
        }

        private void CreatePending()
        {
            int maxLen = config != null ? config.LobbyNameMaxLength : 24;
            string name = (pendingName ?? "").Trim();
            if (name.Length > maxLen) name = name.Substring(0, maxLen);

            var props = new Hashtable
            {
                { LobbyKeys.Name, name },
                { LobbyKeys.Mode, pendingMode.Id },
                { LobbyKeys.Stage, 0 },
                { LobbyKeys.Host, PhotonNetwork.NickName ?? "" },
            };
            // Absent reads as three teams (MatchStartRules.LobbyModeOf), so only the two-team mode writes the key.
            if (pendingMode.LobbyModeValue == MatchStartRules.TwoTeams)
                props[MatchDirector.LobbyModeKey] = pendingMode.LobbyModeValue;

            var options = new RoomOptions
            {
                MaxPlayers = (byte)LobbySeatRules.TotalSeats(SeatLayoutFactory.From(pendingMode)),
                // Same rejoin window as every room (Task 9e): part of the room's options, so every build in a room must match.
                PlayerTtl = RejoinRules.PlayerTtlMs(roomManager != null ? roomManager.RejoinWindowSeconds : 0f),
                IsVisible = true,
                IsOpen = true,
                CustomRoomProperties = props,
                CustomRoomPropertiesForLobby = LobbyKeys.ForLobby,
            };
            string roomName = "L_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Debug.Log($"[LOBBY] creating {roomName} \"{name}\" mode {pendingMode.Id}, {options.MaxPlayers} places");
            PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default);
        }

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            if (pendingMode == null) return;
            if (returnCode == ErrorCode.GameIdAlreadyExists && createRetries < MaxCreateRetries)
            {
                createRetries++;
                CreatePending();
                return;
            }
            Debug.LogWarning($"[LOBBY] could not create a lobby ({returnCode}): {message}");
            pendingMode = null;
            CreateFailed?.Invoke(message);
        }

        public override void OnCreatedRoom()
        {
            pendingMode = null;
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            // Task 9e-2: Photon refuses a normal join while this user id still holds a dropped place in that room: that is our
            // own place being held, so go back to it instead of reporting a failure.
            if (roomManager != null && roomManager.Rejoin != null && roomManager.Rejoin.TryRejoinHeldPlace(returnCode))
                return;

            Debug.Log($"[LOBBY] join refused ({returnCode}): {message}");
            JoinFailed?.Invoke(JoinFailureText(returnCode));
        }

        public static string JoinFailureText(short returnCode)
        {
            if (returnCode == ErrorCode.GameFull) return "That lobby is full.";
            if (returnCode == ErrorCode.GameClosed) return "That lobby has closed.";
            if (returnCode == ErrorCode.GameDoesNotExist) return "That lobby no longer exists.";
            return "Could not join that lobby.";
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
            LobbyListCache.Merge(cache, snapshots, System.Diagnostics.Stopwatch.GetTimestamp());
            sortedStale = true;
            ListChanged?.Invoke();
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
