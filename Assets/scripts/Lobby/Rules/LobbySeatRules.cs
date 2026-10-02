using System.Collections.Generic;

namespace Overpower.Lobby
{
    /// <summary>Where a seat holder is, for the master's sweep: in the room, held for the rejoin window, or gone.</summary>
    public enum SeatHolderPresence { Present, Inactive, LeftForGood }

    /// <summary>
    /// Pure C# rules for who sits where in a lobby. Seats are a dictionary seatKey -> actor number; an empty seat is a
    /// missing key or a value of 0 or less. No Photon types here: the lobby code reads the room's properties into
    /// plain values, calls these and sends the SeatWrite back. Called by the lobby room (Tasks 2-7).
    /// </summary>
    public static class LobbySeatRules
    {
        /// <summary>The value of the stage property (lS) while the lobby has not been started: every seat write expects it,
        /// so a seat can only change before Start.</summary>
        public const int LobbyBeforeStart = 0;

        /// <summary>The stage value Start writes: the warm-up (LobbyStage.Warmup).</summary>
        public const int LobbyWarmup = 1;

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

        /// <summary>The seat this actor holds, or null for No role. Only the layout's real seat keys are looked at, so a
        /// room property that happens to hold the actor's number (the stage "lS" = 2, say) can never read as a seat.</summary>
        public static string SeatOf(int actor, SeatLayout layout, IReadOnlyDictionary<string, int> seats)
        {
            if (actor <= 0) return null;
            foreach (string key in AllSeatKeys(layout))
                if (seats.TryGetValue(key, out int who) && who == actor) return key;
            return null;
        }

        private static bool IsSeatKey(string key, SeatLayout layout)
        {
            foreach (string seat in AllSeatKeys(layout))
                if (seat == key) return true;
            return false;
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
        public static SeatWrite TakeSeat(int actor, string targetKey, SeatLayout layout, IReadOnlyDictionary<string, int> seats)
        {
            if (!IsSeatKey(targetKey, layout) || IsTaken(targetKey, seats, null)) return SeatWrite.None;

            var props = new Dictionary<string, object> { { targetKey, actor } };
            var expected = new Dictionary<string, object> { { targetKey, null }, { LobbyKeys.Stage, LobbyBeforeStart } };
            string old = SeatOf(actor, layout, seats);
            if (old != null)
            {
                props[old] = null;
                expected[old] = actor;
            }
            return new SeatWrite(props, expected);
        }

        /// <summary>Going back to No role: empties the actor's seat, expecting it to still be theirs.</summary>
        public static SeatWrite LeaveSeat(int actor, SeatLayout layout, IReadOnlyDictionary<string, int> seats)
        {
            string old = SeatOf(actor, layout, seats);
            if (old == null) return SeatWrite.None;
            return new SeatWrite(
                new Dictionary<string, object> { { old, null } },
                new Dictionary<string, object> { { old, actor }, { LobbyKeys.Stage, LobbyBeforeStart } });
        }

        /// <summary>The master's sweep: empties the seats of players who left or dropped, each expecting to still be that
        /// player's, and only while the stage is still the lobby (from Start on the seats stay, as the rejoin window keeps a
        /// dropped player's place). None when there is nothing to clear.</summary>
        public static SeatWrite ClearSeats(IReadOnlyDictionary<string, int> toClear)
        {
            if (toClear == null || toClear.Count == 0) return SeatWrite.None;
            var props = new Dictionary<string, object>();
            var expected = new Dictionary<string, object> { { LobbyKeys.Stage, LobbyBeforeStart } };
            foreach (var pair in toClear)
            {
                props[pair.Key] = null;
                expected[pair.Key] = pair.Value;
            }
            return new SeatWrite(props, expected);
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
        public static IReadOnlyDictionary<string, int> AutoFill(SeatLayout layout, IReadOnlyDictionary<string, int> seats, IReadOnlyList<int> noRoleActors)
        {
            // Sorted here, on a copy: the result must not depend on the caller's order (nor reorder the caller's list).
            var ordered = new List<int>(noRoleActors);
            ordered.Sort();
            var placed = new Dictionary<string, int>();
            for (int n = 0; n < ordered.Count; n++)
            {
                int actor = ordered[n];
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

        /// <summary>
        /// The host pressing Start: every No role player is placed (AutoFill) and the stage moves from the lobby to the
        /// warm-up, all in one write. It expects EVERY seat of the layout still at the value it had when the host looked
        /// (empty seats expected empty) and the stage still the lobby, so a No role player who takes a seat in the same
        /// instant can never end up in two seats: the whole write is refused and the host retries with fresh seats.
        /// </summary>
        public static SeatWrite StartWrite(SeatLayout layout, IReadOnlyDictionary<string, int> seats, IReadOnlyList<int> noRoleActors)
        {
            var props = new Dictionary<string, object>();
            foreach (var pair in AutoFill(layout, seats, noRoleActors))
                props[pair.Key] = pair.Value;
            props[LobbyKeys.Stage] = LobbyWarmup;

            var expected = new Dictionary<string, object>();
            foreach (string key in AllSeatKeys(layout))
                expected[key] = seats.TryGetValue(key, out int who) && who > 0 ? (object)who : null;
            expected[LobbyKeys.Stage] = LobbyBeforeStart;
            return new SeatWrite(props, expected);
        }

        /// <summary>The team a team seat key ("sT" + team + index) belongs to; false for a spectator seat or anything else.</summary>
        public static bool TryTeamOfSeat(string seatKey, out int team)
        {
            team = -1;
            if (seatKey == null || seatKey.Length != 4 || seatKey[0] != 's' || seatKey[1] != 'T') return false;
            if (!char.IsDigit(seatKey[2]) || !char.IsDigit(seatKey[3])) return false;
            team = seatKey[2] - '0';
            return true;
        }

        public static bool IsSpectatorSeat(string seatKey) =>
            seatKey != null && seatKey.Length == 3 && seatKey[0] == 's' && seatKey[1] == 'S' && char.IsDigit(seatKey[2]);

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

        /// <summary>
        /// A player joining a lobby whose game has started takes a seat with its own write (the seat writes of the lobby stage
        /// expect the stage to be the lobby, which is no longer true): set the seat to this actor, but only if it is still empty
        /// AND the stage is still the one the joiner saw, so two joiners cannot share a seat and a stage move (warm-up to match)
        /// cannot slip between the look and the write.
        /// </summary>
        public static SeatWrite LateJoinWrite(int actor, string seatKey, int stage) =>
            new SeatWrite(
                new Dictionary<string, object> { { seatKey, actor } },
                new Dictionary<string, object> { { seatKey, null }, { LobbyKeys.Stage, stage } });

        /// <summary>
        /// The master's sweep of seats whose holder is not in the room as a live player. A holder who LEFT FOR GOOD (quit through
        /// the menu, or the rejoin window ran out: not in the room at all) loses the seat at once at any stage, expecting it to
        /// still be theirs and nothing about the stage. A holder who is only INACTIVE (dropped, may come back) loses it only while
        /// the stage is the lobby; from Start on the seat is held for the rejoin window. An actor missing from the presence list
        /// counts as left for good. Up to two writes (a quit and a drop at the same time need different stage expectations).
        /// </summary>
        public static IReadOnlyList<SeatWrite> SweepWrites(IReadOnlyDictionary<string, int> seats, IReadOnlyDictionary<int, SeatHolderPresence> presence, int stage)
        {
            Dictionary<string, int> gone = null, dropped = null;
            foreach (var pair in seats)
            {
                SeatHolderPresence state = presence != null && presence.TryGetValue(pair.Value, out SeatHolderPresence known) ? known : SeatHolderPresence.LeftForGood;
                if (state == SeatHolderPresence.LeftForGood)
                    (gone ?? (gone = new Dictionary<string, int>()))[pair.Key] = pair.Value;
                else if (state == SeatHolderPresence.Inactive && stage == LobbyBeforeStart)
                    (dropped ?? (dropped = new Dictionary<string, int>()))[pair.Key] = pair.Value;
            }
            var writes = new List<SeatWrite>(2);
            if (gone != null) writes.Add(ClearSeatsWithoutStage(gone));
            SeatWrite inactive = ClearSeats(dropped);
            if (!inactive.IsNone) writes.Add(inactive);
            return writes;
        }

        private static SeatWrite ClearSeatsWithoutStage(IReadOnlyDictionary<string, int> toClear)
        {
            var props = new Dictionary<string, object>();
            var expected = new Dictionary<string, object>();
            foreach (var pair in toClear)
            {
                props[pair.Key] = null;
                expected[pair.Key] = pair.Value;
            }
            return new SeatWrite(props, expected);
        }

        /// <summary>The teams a late joiner may be placed on: the ones the match fixed (mTeams) once it did, else (the warm-up)
        /// every team of the layout; a team already knocked out is never one of them (mTeams keeps it, but nobody could
        /// play for it).</summary>
        public static int[] TeamsForLateJoin(SeatLayout layout, int[] fixedTeams, ICollection<int> eliminated)
        {
            int[] teams = fixedTeams != null ? fixedTeams : layout.Teams;
            if (eliminated == null || eliminated.Count == 0) return teams;
            var playing = new List<int>(teams.Length);
            foreach (int team in teams)
                if (!eliminated.Contains(team)) playing.Add(team);
            return playing.ToArray();
        }

        /// <summary>The game of this lobby has started and this client holds no seat in it. Needs the layout: until the room's mode is
        /// read, nobody can say the seat is missing (it may simply not be readable yet).</summary>
        public static bool GameRunningWithoutSeat(bool inRoom, int stage, bool hasLayout, string seat) =>
            inRoom && hasLayout && stage >= LobbyWarmup && seat == null;

        /// <summary>The seat counts the lobby list shows without joining: "&lt;filled team seats&gt;/&lt;team seats&gt;+&lt;filled
        /// spectator seats&gt;", for example "4/9+1". Only the layout's real seat keys are counted.</summary>
        public static string FillText(SeatLayout layout, IReadOnlyDictionary<string, int> seats)
        {
            int filledTeam = 0;
            foreach (int team in layout.Teams)
                filledTeam += FilledOnTeam(layout, team, seats, null);
            int filledSpectators = 0;
            for (int i = 0; i < layout.SpectatorSeats; i++)
                if (IsTaken(SpectatorSeatKey(i), seats, null)) filledSpectators++;
            return filledTeam + "/" + layout.Teams.Length * layout.SeatsPerTeam + "+" + filledSpectators;
        }

        /// <summary>Reads what FillText wrote. False for anything else (absent, an old build's room, damaged text).</summary>
        public static bool TryParseFill(string text, out int filledTeamSeats, out int teamSeats, out int filledSpectatorSeats)
        {
            filledTeamSeats = teamSeats = filledSpectatorSeats = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int slash = text.IndexOf('/');
            int plus = text.IndexOf('+');
            if (slash <= 0 || plus <= slash + 1 || plus >= text.Length - 1) return false;
            return int.TryParse(text.Substring(0, slash), out filledTeamSeats)
                && int.TryParse(text.Substring(slash + 1, plus - slash - 1), out teamSeats)
                && int.TryParse(text.Substring(plus + 1), out filledSpectatorSeats);
        }
    }
}
