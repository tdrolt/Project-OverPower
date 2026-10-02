using System.Collections.Generic;

namespace Overpower.Lobby
{
    /// <summary>
    /// Pure C# rules for who sits where in a lobby. Seats are a dictionary seatKey -> actor number; an empty seat is a
    /// missing key or a value of 0 or less. No Photon types here: the lobby code reads the room's properties into
    /// plain values, calls these and sends the SeatWrite back. Called by the lobby room (Tasks 2-7).
    /// </summary>
    public static class LobbySeatRules
    {
        public static string SeatKey(int team, int index) => "sT" + team + index;

        public static string SpectatorSeatKey(int index) => "sS" + index;

        /// <summary>Every team seat in team order, then the spectator seats.</summary>
        public static IEnumerable<string> AllSeatKeys(SeatLayout layout)
        {
            foreach (int team in layout.Teams)
                for (int i = 0; i < layout.SeatsPerTeam; i++)
                    yield return SeatKey(team, i);
            for (int i = 0; i < layout.SpectatorSeats; i++)
                yield return SpectatorSeatKey(i);
        }

        public static int TotalSeats(SeatLayout layout) =>
            layout.Teams.Length * layout.SeatsPerTeam + layout.SpectatorSeats;

        /// <summary>The seat this actor holds, or null for No role.</summary>
        public static string SeatOf(int actor, IReadOnlyDictionary<string, int> seats)
        {
            if (actor <= 0) return null;
            foreach (var pair in seats)
                if (pair.Value == actor) return pair.Key;
            return null;
        }

        private static bool IsTaken(string key, IReadOnlyDictionary<string, int> seats, IReadOnlyDictionary<string, int> placed)
        {
            if (seats.TryGetValue(key, out int who) && who > 0) return true;
            return placed != null && placed.ContainsKey(key);
        }

        /// <summary>
        /// Taking (or moving to) a seat. Two players can click the same seat in the same instant, so the write says
        /// "set it to me, but only if it is still empty": the server applies the first and drops the second. A move
        /// also empties the old seat, expecting it to still be this actor's. Refused when someone else holds the
        /// target or it is already the actor's own seat.
        /// </summary>
        public static SeatWrite TakeSeat(int actor, string targetKey, IReadOnlyDictionary<string, int> seats)
        {
            if (IsTaken(targetKey, seats, null)) return SeatWrite.None;

            var props = new Dictionary<string, object> { { targetKey, actor } };
            var expected = new Dictionary<string, object> { { targetKey, null } };
            string old = SeatOf(actor, seats);
            if (old != null)
            {
                props[old] = null;
                expected[old] = actor;
            }
            return new SeatWrite(props, expected);
        }

        /// <summary>Going back to No role: empties the actor's seat, expecting it to still be theirs.</summary>
        public static SeatWrite LeaveSeat(int actor, IReadOnlyDictionary<string, int> seats)
        {
            string old = SeatOf(actor, seats);
            if (old == null) return SeatWrite.None;
            return new SeatWrite(
                new Dictionary<string, object> { { old, null } },
                new Dictionary<string, object> { { old, actor } });
        }

        private static int FilledOnTeam(SeatLayout layout, int team, IReadOnlyDictionary<string, int> seats, IReadOnlyDictionary<string, int> placed)
        {
            int count = 0;
            for (int i = 0; i < layout.SeatsPerTeam; i++)
                if (IsTaken(SeatKey(team, i), seats, placed)) count++;
            return count;
        }

        private static string LowestFreeOnTeam(SeatLayout layout, int team, IReadOnlyDictionary<string, int> seats, IReadOnlyDictionary<string, int> placed)
        {
            for (int i = 0; i < layout.SeatsPerTeam; i++)
            {
                string key = SeatKey(team, i);
                if (!IsTaken(key, seats, placed)) return key;
            }
            return null;
        }

        private static string LowestFreeSpectator(SeatLayout layout, IReadOnlyDictionary<string, int> seats, IReadOnlyDictionary<string, int> placed)
        {
            for (int i = 0; i < layout.SpectatorSeats; i++)
            {
                string key = SpectatorSeatKey(i);
                if (!IsTaken(key, seats, placed)) return key;
            }
            return null;
        }

        /// <summary>The lowest free seat of the team with the fewest filled seats that still has a free seat,
        /// among the allowed teams (null = all). A tie goes to the earliest team in the layout, the left-most
        /// column Tudor picked, so the result never depends on dictionary order.</summary>
        private static string EmptiestTeamSeat(SeatLayout layout, IReadOnlyDictionary<string, int> seats, IReadOnlyDictionary<string, int> placed, ICollection<int> allowedTeams)
        {
            string best = null;
            int bestFilled = int.MaxValue;
            foreach (int team in layout.Teams)
            {
                if (allowedTeams != null && !allowedTeams.Contains(team)) continue;
                string free = LowestFreeOnTeam(layout, team, seats, placed);
                if (free == null) continue;
                int filled = FilledOnTeam(layout, team, seats, placed);
                if (filled < bestFilled)
                {
                    bestFilled = filled;
                    best = free;
                }
            }
            return best;
        }

        /// <summary>
        /// Where each No role player goes when the host presses Start, so everyone fits: in ascending actor order,
        /// each goes to the team with the fewest filled seats (ties: left-most team), at its lowest free seat; when
        /// every team seat is full, the lowest free spectator seat; with nothing left they are left out. Returns only
        /// the new assignments, never changing seats that are already taken.
        /// </summary>
        public static IReadOnlyDictionary<string, int> AutoFill(SeatLayout layout, IReadOnlyDictionary<string, int> seats, IReadOnlyList<int> noRoleActorsAscending)
        {
            var placed = new Dictionary<string, int>();
            for (int n = 0; n < noRoleActorsAscending.Count; n++)
            {
                int actor = noRoleActorsAscending[n];
                string key = EmptiestTeamSeat(layout, seats, placed, null) ?? LowestFreeSpectator(layout, seats, placed);
                if (key != null) placed[key] = actor;
            }
            return placed;
        }

        /// <summary>
        /// The host may start the game when, after No role players are filled into seats (they count, so everyone
        /// fits at Start), every team of the layout has at least one player. A two-team layout never asks about a
        /// third team.
        /// </summary>
        public static bool MayStartGame(SeatLayout layout, IReadOnlyDictionary<string, int> seats, IReadOnlyList<int> noRoleActors)
        {
            var placed = AutoFill(layout, seats, noRoleActors);
            foreach (int team in layout.Teams)
                if (FilledOnTeam(layout, team, seats, placed) < 1) return false;
            return true;
        }

        /// <summary>The host may end the warm-up when every team of the layout has at least one player present; a
        /// team missing from the dictionary has none.</summary>
        public static bool MayEndWarmup(SeatLayout layout, IReadOnlyDictionary<int, int> presentPlayersPerTeam)
        {
            foreach (int team in layout.Teams)
                if (!presentPlayersPerTeam.TryGetValue(team, out int n) || n < 1) return false;
            return true;
        }

        /// <summary>
        /// Where someone joining a running match sits: the lowest free seat of the emptiest team that is playing
        /// (ties: left-most), else the lowest free spectator seat, else null. A team not in the match is never
        /// chosen, since nobody could play for it.
        /// </summary>
        public static string PlaceLateJoiner(SeatLayout layout, IReadOnlyDictionary<string, int> seats, IReadOnlyList<int> teamsInMatch)
        {
            var allowed = new HashSet<int>(teamsInMatch);
            return EmptiestTeamSeat(layout, seats, null, allowed) ?? LowestFreeSpectator(layout, seats, null);
        }
    }
}
