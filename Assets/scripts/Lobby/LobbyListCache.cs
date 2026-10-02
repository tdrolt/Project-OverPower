using System.Collections;
using System.Collections.Generic;

namespace Overpower.Lobby
{
    /// <summary>One lobby as the list shows it.</summary>
    public sealed class LobbyEntry
    {
        public string RoomName;
        public string DisplayName;
        /// <summary>The game mode id, or -1 when the room carries none (a room from an old build).</summary>
        public int ModeId;
        public int Stage;
        public string HostName;
        public int PlayerCount;
        public int MaxPlayers;
        /// <summary>When this client first saw the room (any increasing number); keeps the newest-first order steady.</summary>
        public long FirstSeen;
        /// <summary>The room's creation stamp (lC), when it carries one.</summary>
        public bool CreatedKnown;
        public long Created;
        /// <summary>True when the room's seat fill text (lF) was present and readable.</summary>
        public bool FillKnown;
        public int FilledTeamSeats;
        public int TeamSeats;
        public int FilledSpectatorSeats;
        /// <summary>The seat layout of the room's mode, when the mode is known.</summary>
        public SeatLayout? Layout;

        /// <summary>What "newest first" sorts by: the creation stamp when there is one, else when this client first saw the
        /// room, placed after every stamped room (a room from an old build is older than any stamped one).</summary>
        public long SortTime => CreatedKnown ? Created : FirstSeen - OldBuildOffset;
        private const long OldBuildOffset = 1L << 40;

        public string ModeIdText => ModeId < 0 ? "?" : ModeId.ToString();
        public LobbyStage StageValue => Stage == 1 ? LobbyStage.Warmup : Stage == 2 ? LobbyStage.InMatch : LobbyStage.Lobby;
    }

    /// <summary>What the cache needs to know about one room from Photon's list, as plain data (a RoomInfo cannot be built in tests).</summary>
    public struct RoomSnapshot
    {
        public string Name;
        public bool RemovedFromList;
        public bool IsOpen;
        public bool IsVisible;
        public int PlayerCount;
        public int MaxPlayers;
        public IDictionary Properties;
    }

    /// <summary>The pure part of the lobby directory: merging Photon's room list updates into a cache and ordering it.</summary>
    public static class LobbyListCache
    {
        /// <summary>Applies one update: add or update each room, drop the removed, closed and hidden ones. An existing
        /// entry keeps its first-seen time.</summary>
        public static void Merge(Dictionary<string, LobbyEntry> cache, IEnumerable<RoomSnapshot> update, long now, System.Func<int, SeatLayout?> layoutOfMode = null)
        {
            foreach (var room in update)
            {
                if (room.Name == null) continue;
                if (room.RemovedFromList || !room.IsOpen || !room.IsVisible)
                {
                    cache.Remove(room.Name);
                    continue;
                }
                long firstSeen = cache.TryGetValue(room.Name, out var old) ? old.FirstSeen : now;
                int modeId = IntProp(room.Properties, LobbyKeys.Mode, -1);
                bool fillKnown = LobbySeatRules.TryParseFill(StringProp(room.Properties, LobbyKeys.Fill, null), out int filledTeam, out int teamSeats, out int filledSpectators);
                cache[room.Name] = new LobbyEntry
                {
                    RoomName = room.Name,
                    DisplayName = StringProp(room.Properties, LobbyKeys.Name, room.Name),
                    ModeId = modeId,
                    Stage = IntProp(room.Properties, LobbyKeys.Stage, 0),
                    HostName = StringProp(room.Properties, LobbyKeys.Host, "?"),
                    PlayerCount = room.PlayerCount,
                    MaxPlayers = room.MaxPlayers,
                    FirstSeen = firstSeen,
                    CreatedKnown = HasInt(room.Properties, LobbyKeys.Created),
                    Created = IntProp(room.Properties, LobbyKeys.Created, 0),
                    FillKnown = fillKnown,
                    FilledTeamSeats = filledTeam,
                    TeamSeats = teamSeats,
                    FilledSpectatorSeats = filledSpectators,
                    Layout = layoutOfMode != null && modeId >= 0 ? layoutOfMode(modeId) : null,
                };
            }
        }

        /// <summary>The button the row would show. From the seat fill the master keeps (lF) when the room has it: Join while a
        /// team seat is free, Spectate when only spectator seats are, Full when none. Without it (a room from an old build or one
        /// whose master has not written it yet) the only thing known is players against room size.</summary>
        public static JoinAction ActionOf(LobbyEntry e)
        {
            if (e.FillKnown)
            {
                SeatLayout layout = e.Layout ?? new SeatLayout(new int[1], e.TeamSeats, 0);
                return LobbyListRules.ActionFor(layout, e.FilledTeamSeats, e.FilledSpectatorSeats);
            }
            return LobbyListRules.ActionFor(new SeatLayout(new int[1], e.MaxPlayers, 0), e.PlayerCount, 0);
        }

        public static IReadOnlyList<LobbyEntry> Sorted(IEnumerable<LobbyEntry> entries) =>
            LobbyListRules.Sort(entries, ActionOf, e => e.StageValue, e => e.SortTime);

        private static string StringProp(IDictionary props, string key, string fallback) =>
            props != null && props.Contains(key) && props[key] is string s && s.Length > 0 ? s : fallback;

        private static bool HasInt(IDictionary props, string key) => IntProp(props, key, int.MinValue) != int.MinValue;

        private static int IntProp(IDictionary props, string key, int fallback)
        {
            if (props == null || !props.Contains(key)) return fallback;
            object raw = props[key];
            if (raw is int i) return i;
            if (raw is byte b) return b;
            if (raw is short sh) return sh;
            if (raw is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
            return fallback;
        }
    }
}
