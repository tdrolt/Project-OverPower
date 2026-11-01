using System.Collections.Generic;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Net
{
    /// <summary>
    /// The room schema for bots. The server knows nothing about a bot, so everything another client needs lives in room
    /// properties the host writes: the roster (which bot actors exist), the next free actor number, each bot's name and build,
    /// and each bot's "player properties" as b{actor}.{key}. Every client rebuilds its injected BotPlayers from these.
    /// </summary>
    public static class BotRosterRules
    {
        /// <summary>Bot actor numbers are ActorBase+1, +2, ...: far above any real actor a room will reach, never reused.</summary>
        public const int ActorBase = 1000;
        public const string RosterKey = "bR";
        public const string NextKey = "bN";
        /// <summary>Meta keys under b{actor}.: the bot's name and build. Not mirrored into CustomProperties.</summary>
        public const string NickKey = "~n";
        public const string BuildKey = "~b";

        public static bool IsBotActor(int actor) => actor > ActorBase;

        public static string PropKey(int actor, string key) => "b" + actor + "." + key;

        public static bool TryParsePropKey(string roomKey, out int actor, out string key)
        {
            actor = 0;
            key = null;
            if (string.IsNullOrEmpty(roomKey) || roomKey.Length < 4 || roomKey[0] != 'b') return false;
            int dot = roomKey.IndexOf('.');
            if (dot < 2 || dot == roomKey.Length - 1) return false;
            int value = 0;
            for (int i = 1; i < dot; i++)
            {
                char c = roomKey[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (c - '0');
                if (value > 100000000) return false;
            }
            if (!IsBotActor(value)) return false;
            actor = value;
            key = roomKey.Substring(dot + 1);
            return true;
        }

        public static bool IsMetaKey(string key) => key == NickKey || key == BuildKey;

        /// <summary>A bot's player-property write as room keys. A null value stays null: in the mirror it deletes the key, as
        /// it would on a real player.</summary>
        public static Hashtable ToRoom(int actor, Hashtable playerProps)
        {
            var room = new Hashtable();
            if (playerProps == null) return room;
            foreach (var pair in playerProps)
                room[PropKey(actor, pair.Key.ToString())] = pair.Value;
            return room;
        }

        /// <summary>The bot player-property changes inside a room update, grouped per bot, meta keys left out.</summary>
        public static Dictionary<int, Hashtable> SplitByBot(Hashtable roomProps)
        {
            var split = new Dictionary<int, Hashtable>();
            if (roomProps == null) return split;
            foreach (var pair in roomProps)
            {
                if (!(pair.Key is string roomKey) || !TryParsePropKey(roomKey, out int actor, out string key) || IsMetaKey(key)) continue;
                if (!split.TryGetValue(actor, out Hashtable props))
                    split[actor] = props = new Hashtable();
                props[key] = pair.Value;
            }
            return split;
        }

        public static int[] Roster(Hashtable roomProps) =>
            roomProps != null && roomProps.TryGetValue(RosterKey, out object raw) && raw is int[] actors ? actors : new int[0];

        /// <summary>The number the next added bot gets: the room's counter, or the first bot number in a room that never had one.</summary>
        public static int NextActor(object nextSeen) => nextSeen is int next && IsBotActor(next) ? next : ActorBase + 1;

        /// <summary>Which bot players a client must inject (in the roster, not present) and drop (present, no longer in the roster).</summary>
        public static void Plan(IReadOnlyList<int> roster, IEnumerable<int> present, List<int> add, List<int> remove)
        {
            add.Clear();
            remove.Clear();
            var have = new HashSet<int>(present);
            var want = new HashSet<int>(roster);
            foreach (int actor in roster)
                if (!have.Contains(actor)) add.Add(actor);
            foreach (int actor in have)
                if (!want.Contains(actor)) remove.Add(actor);
            remove.Sort();
        }

        /// <summary>One bot out: the roster without it and every one of its keys nulled.</summary>
        public static Hashtable RemovalWrite(Hashtable roomProps, int actor)
        {
            var write = new Hashtable();
            var kept = new List<int>();
            foreach (int a in Roster(roomProps))
                if (a != actor) kept.Add(a);
            write[RosterKey] = kept.ToArray();
            if (roomProps != null)
                foreach (var pair in roomProps)
                    if (pair.Key is string roomKey && TryParsePropKey(roomKey, out int owner, out _) && owner == actor)
                        write[roomKey] = null;
            return write;
        }

        /// <summary>Every bot out (the host left): an empty roster and every bot key nulled.</summary>
        public static Hashtable ClearAllWrite(Hashtable roomProps)
        {
            var write = new Hashtable { { RosterKey, new int[0] } };
            if (roomProps != null)
                foreach (var pair in roomProps)
                    if (pair.Key is string roomKey && TryParsePropKey(roomKey, out _, out _))
                        write[roomKey] = null;
            return write;
        }
    }
}
