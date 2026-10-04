using System;
using ExitGames.Client.Photon;
using Overpower.Data;
using Overpower.Lobby;
using Photon.Pun;

namespace Overpower.Dominion
{
    /// <summary>
    /// "Is this room Dominion?" from the mode id the lobby wrote into the room (LobbyKeys.Mode). The pure part (IsDominion, TeamsOf) takes the
    /// room's properties and a lookup from mode id to definition, so the tests use plain values; IsActive is the thin Photon part: the current
    /// room's properties and the RoomManager's mode catalogue. A missing or unknown id reads as not Dominion (an old room, a corrupt value).
    /// </summary>
    public static class DominionMode
    {
        /// <summary>True when the room's mode id names a mode of the Dominion family.</summary>
        public static bool IsDominion(Hashtable roomProps, Func<int, GameModeDefinition> modeOf)
        {
            GameModeDefinition mode = ModeOf(roomProps, modeOf);
            return mode != null && mode.Family == GameModeFamily.Dominion;
        }

        /// <summary>The team ids that play in the room's mode (from the definition), or an empty array when the mode is unknown.</summary>
        public static int[] TeamsOf(Hashtable roomProps, Func<int, GameModeDefinition> modeOf)
        {
            GameModeDefinition mode = ModeOf(roomProps, modeOf);
            return mode != null && mode.Teams != null ? (int[])mode.Teams.Clone() : Array.Empty<int>();
        }

        private static GameModeDefinition ModeOf(Hashtable roomProps, Func<int, GameModeDefinition> modeOf)
        {
            if (roomProps == null || modeOf == null) return null;
            if (!roomProps.TryGetValue(LobbyKeys.Mode, out object raw) || !(raw is int id)) return null;
            return modeOf(id);
        }

        // ---- the thin Photon part. The mode never changes inside a room, so the answer is kept per Room object (a new join is a new object):
        // this runs every frame for every tower and minimap bubble through MatchDirector.CutTeam.
        private static Photon.Realtime.Room cachedRoom;
        private static bool cachedAnswer;
        private static RoomManager rooms;

        /// <summary>True when the room this client is in is a Dominion room. False outside a room.</summary>
        public static bool IsActive()
        {
            Photon.Realtime.Room room = PhotonNetwork.CurrentRoom;
            if (room == null) return false;
            if (room == cachedRoom) return cachedAnswer;

            if (rooms == null) rooms = UnityEngine.Object.FindFirstObjectByType<RoomManager>();
            GameModeCatalogue catalogue = rooms != null ? rooms.ModeCatalogue : null;
            if (catalogue == null) return false; // not cached: ask again once the RoomManager is there
            cachedRoom = room;
            cachedAnswer = IsDominion(room.CustomProperties, catalogue.ById);
            return cachedAnswer;
        }

        // Per Room object like IsActive: the spawn heal asks every frame, and TeamsOf clones the array.
        private static Photon.Realtime.Room teamCountRoom;
        private static int teamCount;

        /// <summary>How many teams the current room's mode has (2 = 2v2, 3 = 3v3v3); 0 outside a room or when the mode is unknown.</summary>
        public static int TeamCountOfCurrentRoom()
        {
            Photon.Realtime.Room room = PhotonNetwork.CurrentRoom;
            if (room == null) return 0;
            if (room == teamCountRoom) return teamCount;
            if (rooms == null) rooms = UnityEngine.Object.FindFirstObjectByType<RoomManager>();
            GameModeCatalogue catalogue = rooms != null ? rooms.ModeCatalogue : null;
            if (catalogue == null) return 0; // not cached: ask again once the RoomManager is there
            teamCountRoom = room;
            teamCount = TeamsOf(room.CustomProperties, catalogue.ById).Length;
            return teamCount;
        }

        /// <summary>The scene's DominionConfig (the RoomManager's), or null when there is no RoomManager yet.</summary>
        public static DominionConfig Config()
        {
            if (rooms == null) rooms = UnityEngine.Object.FindFirstObjectByType<RoomManager>();
            return rooms != null ? rooms.Dominion : null;
        }

        /// <summary>The team ids of the current room's mode (Dominion size), or empty.</summary>
        public static int[] TeamsOfCurrentRoom()
        {
            Photon.Realtime.Room room = PhotonNetwork.CurrentRoom;
            if (room == null) return Array.Empty<int>();
            if (rooms == null) rooms = UnityEngine.Object.FindFirstObjectByType<RoomManager>();
            GameModeCatalogue catalogue = rooms != null ? rooms.ModeCatalogue : null;
            return catalogue == null ? Array.Empty<int>() : TeamsOf(room.CustomProperties, catalogue.ById);
        }
    }
}
