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

        /// <summary>The first team with at least roundsToWin round wins, else -1.</summary>
        public static int MatchWinner(int[] wins, int roundsToWin)
        {
            if (wins == null) return -1;
            for (int i = 0; i < wins.Length; i++)
                if (wins[i] >= roundsToWin) return i;
            return -1;
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
        /// Someone on roundsToWin: Over, they win (so round 3 is not played after 2-0). Rounds left: a Break. After the last round with
        /// nobody there: the teams tied for the most round wins go to sudden death - but a single leader on round wins (say 1-0-0 after two
        /// tied rounds) simply wins the match (Tudor A7): sudden death is only for teams that are level.</summary>
        public static RoundOutcome AfterRound(int round, int[] wins, int roundsToWin, int maxRounds, int[] teamsInMatch)
        {
            int winner = MatchWinner(wins, roundsToWin);
            if (winner >= 0)
                return new RoundOutcome { Next = DominionStage.Over, Winner = winner };
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
