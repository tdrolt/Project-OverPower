using System.Collections.Generic;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Lobby
{
    /// <summary>
    /// The host's Add bot / Fill with bots / remove-a-bot as single compare-and-swap writes. Seat, roster, name and build go in
    /// one write so a seat never holds a bot nobody knows; the next-number counter is part of the condition, so two quick clicks
    /// cannot hand out the same bot number. Bots only ever take team seats (a spectator seat has no body).
    /// </summary>
    public static class BotSeatRules
    {
        public const int BuildCount = 3;

        /// <summary>The empty team seats of one team (or of every team when team is null), in seat order.</summary>
        public static List<string> FreeTeamSeats(SeatLayout layout, IReadOnlyDictionary<string, int> seats, int? team)
        {
            var free = new List<string>();
            if (layout.Teams == null) return free;
            foreach (int t in layout.Teams)
            {
                if (team.HasValue && t != team.Value) continue;
                for (int i = 0; i < layout.SeatsPerTeam; i++)
                {
                    string key = LobbySeatRules.SeatKey(t, i);
                    if (seats == null || !seats.ContainsKey(key)) free.Add(key);
                }
            }
            return free;
        }

        /// <summary>One bot per seat key, numbered on from nextSeen (the room's counter as last read, null in a room that never
        /// had a bot). None when there is nothing valid to add.</summary>
        public static SeatWrite AddBots(IReadOnlyList<string> seatKeys, IReadOnlyList<(string nick, int build)> who, object nextSeen,
            int[] roster, SeatLayout layout, IReadOnlyDictionary<string, int> seats)
        {
            if (seatKeys == null || who == null || seatKeys.Count == 0 || seatKeys.Count != who.Count) return SeatWrite.None;
            foreach (string key in seatKeys)
                if (!LobbySeatRules.TryTeamOfSeat(key, out int team) || !IsTeamOf(layout, team) || (seats != null && seats.ContainsKey(key)))
                    return SeatWrite.None;

            int actor = BotRosterRules.NextActor(nextSeen);
            var newRoster = new List<int>(roster ?? new int[0]);
            var props = new Dictionary<string, object>();
            var expected = new Dictionary<string, object> { { LobbyKeys.Stage, LobbySeatRules.LobbyBeforeStart }, { BotRosterRules.NextKey, nextSeen } };
            for (int i = 0; i < seatKeys.Count; i++, actor++)
            {
                props[seatKeys[i]] = actor;
                expected[seatKeys[i]] = null;
                props[BotRosterRules.PropKey(actor, BotRosterRules.NickKey)] = who[i].nick;
                props[BotRosterRules.PropKey(actor, BotRosterRules.BuildKey)] = who[i].build;
                newRoster.Add(actor);
            }
            props[BotRosterRules.RosterKey] = newRoster.ToArray();
            props[BotRosterRules.NextKey] = actor;
            return new SeatWrite(props, expected);
        }

        /// <summary>Takes a bot out of the lobby: its seat emptied (expecting it still there) and its whole record deleted.</summary>
        public static SeatWrite RemoveBot(int actor, SeatLayout layout, IReadOnlyDictionary<string, int> seats, Hashtable roomProps)
        {
            if (!BotRosterRules.IsBotActor(actor)) return SeatWrite.None;
            var props = new Dictionary<string, object>();
            var expected = new Dictionary<string, object> { { LobbyKeys.Stage, LobbySeatRules.LobbyBeforeStart } };
            string seat = LobbySeatRules.SeatOf(actor, layout, seats);
            if (seat != null)
            {
                props[seat] = null;
                expected[seat] = actor;
            }
            foreach (var pair in BotRosterRules.RemovalWrite(roomProps, actor))
                props[pair.Key.ToString()] = pair.Value;
            return new SeatWrite(props, expected);
        }

        /// <summary>A name from the pool nobody in the room has yet (case does not matter), chosen by roll; when the pool is used
        /// up, the first pool name numbered on ("Mira 2").</summary>
        public static string PickName(IReadOnlyList<string> pool, ICollection<string> taken, int roll)
        {
            var takenLower = new HashSet<string>();
            if (taken != null)
                foreach (string t in taken)
                    if (!string.IsNullOrEmpty(t)) takenLower.Add(t.Trim().ToLowerInvariant());

            var free = new List<string>();
            if (pool != null)
                foreach (string name in pool)
                    if (!string.IsNullOrWhiteSpace(name) && !takenLower.Contains(name.Trim().ToLowerInvariant())) free.Add(name.Trim());
            if (free.Count > 0) return free[Index(roll, free.Count)];

            string stem = pool != null && pool.Count > 0 && !string.IsNullOrWhiteSpace(pool[0]) ? pool[0].Trim() : "Player";
            for (int n = 2; n < 1000; n++)
            {
                string candidate = stem + " " + n;
                if (!takenLower.Contains(candidate.ToLowerInvariant())) return candidate;
            }
            return stem;
        }

        public static int PickBuild(int roll) => Index(roll, BuildCount);

        private static int Index(int roll, int count) => ((roll % count) + count) % count;

        private static bool IsTeamOf(SeatLayout layout, int team)
        {
            if (layout.Teams == null) return false;
            foreach (int t in layout.Teams)
                if (t == team) return true;
            return false;
        }
    }
}
