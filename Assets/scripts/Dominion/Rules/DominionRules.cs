using System;
using System.Collections.Generic;
using Overpower.Match;

namespace Overpower.Dominion
{
    /// <summary>
    /// The stages of a Dominion match. The numbers are what goes into the room later (Room Properties), so they are fixed: never renumber,
    /// only add.
    /// </summary>
    public enum DominionStage
    {
        None = 0,
        Break = 1,
        Round = 2,
        SuddenDeath = 3,
        Over = 4,
        /// <summary>The extra minute after a close round (Tudor A50). Appended: the numbers above are already in rooms.</summary>
        Overtime = 5,
    }

    /// <summary>What happens after a round has been scored. Winner is the match winner when Next is Over, else -1; SuddenDeathTeams is the
    /// teams that play sudden death when Next is SuddenDeath, else null.</summary>
    public struct RoundOutcome
    {
        public DominionStage Next;
        public int Winner;
        public int[] SuddenDeathTeams;
    }

    /// <summary>
    /// The pure rules of Dominion (Task 1): who wins a round and the match, when sudden death starts and between whom, what a held zone
    /// earns, when the centre pays and when a bounty is due. No Photon types: ints, arrays and server-clock milliseconds. Every number comes in
    /// as an argument (DominionConfig holds them), so the tests use made-up ones.
    /// Read later by: the stage flow (Task 2: AfterRound, StageEndMs), points, centre and bounty (Task 3: PointsThisTick, the centre and
    /// BountyDue).
    /// Times are the room's server clock in ms, an int that wraps, so they are only ever compared as unchecked(a - b), like the other rules.
    /// </summary>
    public static class DominionRules
    {
        /// <summary>Whether a respawn uses the "capital under attack" spawn point. Not in Dominion (A21): those points are 28.6 m from each capital,
        /// outside its healing circle, and the respawn shield already protects a player who spawns next to enemies.</summary>
        public static bool UsesCapitalUnderAttackSpawn(bool dominion, bool capitalUnderAttack) => !dominion && capitalUnderAttack;

        /// <summary>Does this room have health packs? 2v2 has none (the spec); 3v3v3 keeps them, and so does every Conquest room.</summary>
        public static bool HasHealthPacks(bool dominion, int teamCount) => !(dominion && teamCount == 2);

        /// <summary>May the health packs be built right now? Not while the room's mode is still unknown (the catalogue is not reachable yet, e.g. in the
        /// first frames of a new scene): a 2v2 room would get packs that then have to be taken away. Once the mode is known: HasHealthPacks.</summary>
        public static bool MayBuildHealthPacks(bool modeKnown, bool dominion, int teamCount) =>
            modeKnown && HasHealthPacks(dominion, teamCount);

        /// <summary>Do Dominion's own match rules (the fixed respawn wait, the spawn healing) apply right now? Only in a Dominion room once the match
        /// is live: the warm-up stays the free sandbox it is in Conquest (default A22).</summary>
        public static bool RulesApply(bool dominionRoom, bool matchLive) => dominionRoom && matchLive;

        /// <summary>May a dead player come back? Not in a live Dominion match's sudden death, and not once the match is Over: a death there is for good
        /// (after the last whistle nobody gets back up while the result shows). Everywhere else (a round, a break, Conquest, the warm-up) the ordinary
        /// respawn applies.</summary>
        public static bool RespawnAllowed(bool dominionLive, DominionStage stage) =>
            !(dominionLive && (stage == DominionStage.SuddenDeath || stage == DominionStage.Over));

        /// <summary>A player joining while sudden death is on is seated as a spectator when a spectator seat is free (they could only wait dead).</summary>
        public static bool LateJoinerPrefersSpectatorSeat(DominionStage stage) => stage == DominionStage.SuddenDeath;

        /// <summary>The team with the single highest points, or -1 on a tie for first (or no points at all). A tied round counts for
        /// nobody (Tudor): neither team gets a round win, and 0-0 is a tie like any other.</summary>
        public static int RoundWinner(int[] points)
        {
            if (points == null || points.Length == 0) return -1;
            int best = 0;
            bool tied = false;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i] > points[best]) { best = i; tied = false; }
                else if (points[i] == points[best]) tied = true;
            }
            return tied ? -1 : best;
        }


        // ---------------------------------------------------------------- overtime (Tudor A50)

        /// <summary>What the round's clock running out decides: either the winners (one team, or none when overtime is off and the top is tied) or the
        /// teams that play overtime. Exactly one of the two is set.</summary>
        public struct BuzzerResult
        {
            public int[] Winners;
            public int[] OvertimeTeams;
        }

        /// <summary>Overtime exists only with both some time and a lead to reach: 0 seconds (or no lead) turns it off.</summary>
        public static bool OvertimeOn(float seconds, int leadPoints) => seconds > 0f && leadPoints > 0;

        /// <summary>Does the room's stage play for points: a round, or the overtime after it (zones, bounties and the centre keep paying there).</summary>
        public static bool IsRoundPlay(DominionStage stage) => stage == DominionStage.Round || stage == DominionStage.Overtime;

        /// <summary>The teams that play overtime: every team of the match less than <paramref name="leadPoints"/> behind the top at the buzzer
        /// (a team exactly a lead behind is out). One team alone means it is a lead ahead of everyone and has simply won. A 0-0 buzzer is within the lead,
        /// so every team plays. Only the match's teams count (a 2v2 room keeps team 2's slot at 0).</summary>
        public static int[] OvertimeTeams(int[] points, int[] teamsInMatch, int leadPoints)
        {
            var result = new List<int>();
            if (teamsInMatch == null || teamsInMatch.Length == 0) return result.ToArray();
            int lead = Math.Max(1, leadPoints);
            int top = int.MinValue;
            foreach (int t in teamsInMatch) top = Math.Max(top, PointsOf(points, t));
            foreach (int t in teamsInMatch)
                if (top - PointsOf(points, t) < lead) result.Add(t);
            result.Sort();
            return result.ToArray();
        }

        /// <summary>Tudor A56: the overtime teams that still have anyone in the game. A team with nobody left drops out of the overtime - it can neither win
        /// the round nor share it - and the others carry on (a single team left has won). "Anyone in the game" is the master's player count for the A3 last-team
        /// rule (a dropped player still counts for the dropped grace). Null counts (the master could not count) leaves the teams as they were.</summary>
        public static int[] OvertimeTeamsPresent(int[] overtimeTeams, int[] playersPerTeam)
        {
            if (overtimeTeams == null || playersPerTeam == null) return overtimeTeams;
            var present = new List<int>();
            foreach (int team in overtimeTeams)
                if (team >= 0 && team < playersPerTeam.Length && playersPerTeam[team] > 0) present.Add(team);
            return present.ToArray();
        }

        /// <summary>The overtime team that is <paramref name="leadPoints"/> or more ahead of every OTHER overtime team, or -1. A team outside overtime
        /// neither wins here nor stops anyone from winning: it was a lead behind at the buzzer and is out of the round.</summary>
        public static int OvertimeLeader(int[] points, int[] overtimeTeams, int leadPoints)
        {
            if (overtimeTeams == null || overtimeTeams.Length < 2) return -1;
            int lead = Math.Max(1, leadPoints);
            foreach (int candidate in overtimeTeams)
            {
                bool ahead = true;
                foreach (int other in overtimeTeams)
                    if (other != candidate && PointsOf(points, candidate) - PointsOf(points, other) < lead) { ahead = false; break; }
                if (ahead) return candidate;
            }
            return -1;
        }

        /// <summary>The clock ran out on a round: a team a lead ahead of every other team wins it; otherwise the teams within the lead play overtime.
        /// With overtime off the old rule applies: the single top team wins, a tie for first counts for nobody (empty winners).</summary>
        public static BuzzerResult AtBuzzer(int[] points, int[] teamsInMatch, int leadPoints, bool overtimeOn)
        {
            if (!overtimeOn)
            {
                int winner = RoundWinner(points);
                return new BuzzerResult { Winners = winner >= 0 ? new[] { winner } : new int[0] };
            }
            int[] close = OvertimeTeams(points, teamsInMatch, leadPoints);
            return close.Length == 1 ? new BuzzerResult { Winners = close } : new BuzzerResult { OvertimeTeams = close };
        }

        /// <summary>The overtime's minute ran out: a team that has the lead at the final points wins, else every team still in overtime shares the round.</summary>
        public static int[] AtOvertimeEnd(int[] points, int[] overtimeTeams, int leadPoints)
        {
            int leader = OvertimeLeader(points, overtimeTeams, leadPoints);
            if (leader >= 0) return new[] { leader };
            if (overtimeTeams == null) return new int[0];
            int[] shared = (int[])overtimeTeams.Clone();
            Array.Sort(shared);
            return shared;
        }

        /// <summary>The round wins after a round: a copy of <paramref name="wins"/> with one more for each round winner (a shared round has several).</summary>
        public static int[] WinsAfterRound(int[] wins, int[] roundWinners)
        {
            var result = new int[Math.Max(DominionKeys.TeamSlots, wins != null ? wins.Length : 0)];
            if (wins != null) Array.Copy(wins, result, wins.Length);
            if (roundWinners != null)
                foreach (int team in roundWinners)
                    if (team >= 0 && team < result.Length) result[team]++;
            return result;
        }

        private static int PointsOf(int[] points, int team) => points != null && team >= 0 && team < points.Length ? points[team] : 0;

        /// <summary>The team with at least roundsToWin round wins, else -1. Two or more teams there together (a shared round can do it) have no single
        /// winner: that is sudden death between them (AfterRound), so this says -1 for it too.</summary>
        public static int MatchWinner(int[] wins, int roundsToWin)
        {
            if (wins == null) return -1;
            int found = -1;
            for (int i = 0; i < wins.Length; i++)
            {
                if (wins[i] < roundsToWin) continue;
                if (found >= 0) return -1;
                found = i;
            }
            return found;
        }

        /// <summary>True when two or more teams stand at roundsToWin round wins at once (e.g. 2-2 after two shared rounds).</summary>
        public static bool SeveralReachedTheTarget(int[] wins, int roundsToWin)
        {
            if (wins == null) return false;
            int count = 0;
            foreach (int w in wins) if (w >= roundsToWin) count++;
            return count >= 2;
        }

        /// <summary>The teams in the match tied for the most round wins, lowest first (2v2 1-1: both; 3v3v3 1-1-1: all three; 1-1-0: the two).
        /// A single leader comes back alone; AfterRound turns that into a match win rather than sudden death.</summary>
        public static int[] SuddenDeathTeams(int[] wins, int[] teamsInMatch)
        {
            var result = new List<int>();
            if (wins == null || teamsInMatch == null || teamsInMatch.Length == 0) return result.ToArray();
            int most = int.MinValue;
            foreach (int t in teamsInMatch)
                most = Math.Max(most, WinsOf(wins, t));
            foreach (int t in teamsInMatch)
                if (WinsOf(wins, t) == most) result.Add(t);
            result.Sort();
            return result.ToArray();
        }

        /// <summary>Who plays the current sudden death: the teams the room names (dSdT, A33 - narrowed by every replay), else, for a room that
        /// has none, the teams level on round wins. An empty stored list reads as none written.</summary>
        public static int[] TeamsPlayingSuddenDeath(int[] stored, int[] wins, int[] teamsInMatch) =>
            stored != null && stored.Length > 0 ? stored : SuddenDeathTeams(wins, teamsInMatch);

        private static int WinsOf(int[] wins, int team) => team >= 0 && team < wins.Length ? wins[team] : 0;

        /// <summary>What comes after round <paramref name="round"/> (1-based), with <paramref name="wins"/> already counting that round.
        /// Someone alone on roundsToWin: Over, they win (so round 3 is not played after 2-0); two or more there together: sudden death between them. Rounds left: a Break. After the last round with
        /// nobody there: the teams tied for the most round wins go to sudden death - but a single leader on round wins (say 1-0-0 after two
        /// tied rounds) simply wins the match (Tudor A7): sudden death is only for teams that are level.</summary>
        public static RoundOutcome AfterRound(int round, int[] wins, int roundsToWin, int maxRounds, int[] teamsInMatch)
        {
            int winner = MatchWinner(wins, roundsToWin);
            if (winner >= 0)
                return new RoundOutcome { Next = DominionStage.Over, Winner = winner };
            // Two or more at the target together (shared rounds): nobody has won the match, so those teams - level at the top - play sudden death.
            if (SeveralReachedTheTarget(wins, roundsToWin))
                return new RoundOutcome { Next = DominionStage.SuddenDeath, Winner = -1, SuddenDeathTeams = SuddenDeathTeams(wins, teamsInMatch) };
            if (round < maxRounds)
                return new RoundOutcome { Next = DominionStage.Break, Winner = -1 };
            int[] level = SuddenDeathTeams(wins, teamsInMatch);
            if (level.Length == 1)
                return new RoundOutcome { Next = DominionStage.Over, Winner = level[0] };
            return new RoundOutcome { Next = DominionStage.SuddenDeath, Winner = -1, SuddenDeathTeams = level };
        }

        /// <summary>Points <paramref name="team"/> earns this one second: for each zone it owns, the table entry of the zone's tier
        /// (pointsPerTier[tier - 1]). Spawn zones, Tier 1 (the capitals) and Tier 4 (the centre) never pay here whatever the table says: camping your own spawn
        /// earns nothing, and the centre pays in lumps instead (NextCentrePayoutMs) - Tudor's "alternative way to win".</summary>
        public static int PointsThisTick(int[] zoneOwner, int[] zoneTier, bool[] isSpawnZone, int team, int[] pointsPerTier)
        {
            if (zoneOwner == null || zoneTier == null || pointsPerTier == null) return 0;
            int total = 0;
            for (int z = 0; z < zoneOwner.Length && z < zoneTier.Length; z++)
            {
                if (zoneOwner[z] != team) continue;
                if (isSpawnZone != null && z < isSpawnZone.Length && isSpawnZone[z]) continue;
                int tier = zoneTier[z];
                if (tier == CentreTier || tier == CapitalTier || tier < 1 || tier > pointsPerTier.Length) continue;
                total += Math.Max(0, pointsPerTier[tier - 1]);
            }
            return total;
        }

        /// <summary>The Tier 4 zone: the centre.</summary>
        public const int CentreTier = 4;

        /// <summary>Tier 1: the capitals. They never pay per tick whatever the table says.</summary>
        public const int CapitalTier = 1;

        /// <summary>PointsThisTick for every team 0..teamCount-1.</summary>
        public static int[] PointsThisTickAll(int[] zoneOwner, int[] zoneTier, bool[] isSpawnZone, int teamCount, int[] pointsPerTier)
        {
            var all = new int[Math.Max(0, teamCount)];
            for (int t = 0; t < all.Length; t++)
                all[t] = PointsThisTick(zoneOwner, zoneTier, isSpawnZone, t, pointsPerTier);
            return all;
        }

        /// <summary>The first payout time strictly after <paramref name="nowMs"/>: the first at roundStart + firstDelay, then every interval.
        /// (An interval under 1 ms is treated as 1 so the answer always moves forward.)</summary>
        public static int NextCentrePayoutMs(int roundStartMs, int nowMs, int firstDelayMs, int intervalMs)
        {
            int first = unchecked(roundStartMs + firstDelayMs);
            int sinceFirst = unchecked(nowMs - first);
            if (sinceFirst < 0) return first;
            int interval = Math.Max(1, intervalMs);
            long steps = (long)sinceFirst / interval + 1;
            return unchecked(first + (int)(steps * interval));
        }

        /// <summary>True from the payout time on (wrap-safe).</summary>
        public static bool CentrePayoutDue(int payoutMs, int nowMs) => unchecked(nowMs - payoutMs) >= 0;

        /// <summary>The team a centre payout goes to: whoever holds the centre at that moment, or -1 (nobody holding = nobody paid).</summary>
        public static int CentrePayoutTeam(int centreOwner) => centreOwner >= 0 ? centreOwner : -1;

        /// <summary>A zone's bounty is due when it is captured by a different team than the one that held it, after that hold lasted at
        /// least holdMs. It reads what the room stores, as BuildingManager.SetCaptured does: a zone always goes neutral before it is captured,
        /// and TerritorySnapshot.WithNeutral keeps the finished hold (LastOwnerOf / LastHeldMs), so the arguments are those two, not a
        /// "held since" time. This wraps BountyRule (the same rule the Control mode uses) rather than repeating it.</summary>
        public static bool BountyDue(int lastHeldMs, int holdMs, int lastOwner, int newOwner) =>
            BountyRule.PayoutOnCapture(newOwner, lastOwner, lastHeldMs, 1, holdMs) > 0;

        /// <summary>The server time a stage that started at startMs and lasts <paramref name="seconds"/> ends (wrap-safe).</summary>
        public static int StageEndMs(int startMs, float seconds) => unchecked(startMs + (int)Math.Round(seconds * 1000f));
    }
}
