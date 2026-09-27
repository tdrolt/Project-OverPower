using System.Collections.Generic;

namespace Overpower.Match
{
    public enum MatchPhase
    {
        /// <summary>Before the match goes live (2.7b, Tudor 2026-09-18): fights and captures happen, nothing counts. Never
        /// stored - MatchDirector reads it from mTeams being absent.</summary>
        Warmup = 0,
        /// <summary>Three teams still in: a team with no capital is out only once all its members are out (last stand).</summary>
        ThreeTeams = 1,
        /// <summary>Two teams still in - after the first knockout, or from the start of a host-started match.</summary>
        TwoTeams = 2,
        Over = 3,
    }

    public struct TeamStatus
    {
        public int TeamId;
        /// <summary>Fixed when the match went live (MatchDirector.TeamsInMatchKey). A team that later empties stays in
        /// (Tudor, rule 2); the third team of a host start was never in and is ignored here entirely.</summary>
        public bool InMatch;
        public int Members;
        /// <summary>Members who died while the team had no capital ("lastStand" Player Property). An ordinary respawn
        /// countdown never counts.</summary>
        public int MembersOutForLastStand;
        /// <summary>Owns its own starting capital right now.</summary>
        public bool HoldsOwnCapital;
        /// <summary>Owns at least one capital in play: its own, an enemy's, or a knocked-out team's. The third capital of a
        /// host-started match is never in play, and neither is a capital behind the phase-two wall (map shrink,
        /// 2026-09-25 - MatchDirector.IsCapitalInPlay).</summary>
        public bool HoldsAnyCapitalInPlay;
        /// <summary>The server ms this team's latest member went out for the last stand - the latest "lastStandAt"
        /// Player Property among its members who are out. Null when none has a stamp (nobody died; e.g. it emptied).
        /// Read only by the no-draw rule (Tudor: the last team to die wins a same-instant wipe).</summary>
        public int? LastOutAtMs;
    }

    public sealed class MatchPhaseResult
    {
        public MatchPhase Phase;
        public List<int> Eliminated = new List<int>();
        /// <summary>The winning team when Phase is Over, else -1.</summary>
        public int Winner = -1;
    }

    /// <summary>
    /// Who is out and which phase a LIVE match is in, recomputed from facts every client has (the teams fixed at going
    /// live, capital owners, who has died since their team lost its capitals, team sizes) - so a new master, or a second
    /// match, reaches the same answer. MatchDirector never calls this during the warm-up: nothing counts then.
    ///
    /// The phase moves ONLY on a knockout (telemetry spec Part 3): it is the number of teams still in, and "in" is fixed
    /// at going live - nobody joining or leaving can move it. What each phase MEANS (Tudor, 2026-09-18, and GDD p.20-21):
    /// - three teams: a team with no capital is out once every member is out (its last stand); an emptied team is "all
    ///   out", so its capital falling is its knockout;
    /// - two teams: with no capital while the other team holds one, you are out at once; with none on either side it is
    ///   last man standing - only a wiped team goes out, and the other wins even holding nothing;
    /// - "having a capital" is CountsAsHavingACapital (adoption);
    /// - there is never a draw: if every team still in would go out at the same instant, the team whose last player
    ///   went out latest stays in and wins (NoDrawSurvivorIndex).
    /// A fixpoint: a knockout can narrow the match to two teams, and the two-team rule then applies at once, not next tick.
    /// </summary>
    public static class MatchPhaseRules
    {
        public static MatchPhaseResult Recompute(IReadOnlyCollection<int> alreadyEliminated, IReadOnlyList<TeamStatus> teams)
        {
            var result = new MatchPhaseResult();
            result.Eliminated.AddRange(alreadyEliminated);
            var outNow = new List<TeamStatus>();

            while (true)
            {
                MatchPhase phase = PhaseFor(teams, result.Eliminated);
                if (phase == MatchPhase.Over)
                    break;

                outNow.Clear();
                foreach (TeamStatus team in teams)
                    if (IsStillIn(team, result.Eliminated) && IsOutNow(team, phase, teams, result.Eliminated))
                        outNow.Add(team);
                if (outNow.Count == 0)
                    break;

                // No draw (Tudor): never knock out every team still in at once.
                if (outNow.Count == CountStillIn(teams, result.Eliminated))
                    outNow.RemoveAt(NoDrawSurvivorIndex(outNow));

                foreach (TeamStatus team in outNow)
                    result.Eliminated.Add(team.TeamId);
            }

            result.Phase = PhaseFor(teams, result.Eliminated);
            if (result.Phase == MatchPhase.Over)
                foreach (TeamStatus team in teams)
                    if (IsStillIn(team, result.Eliminated))
                        result.Winner = team.TeamId;
            return result;
        }

        private static bool IsOutNow(TeamStatus team, MatchPhase phase, IReadOnlyList<TeamStatus> teams, List<int> eliminated)
        {
            if (HasACapital(team, phase))
                return false;

            bool everyoneOut = team.MembersOutForLastStand >= team.Members; // an emptied team: 0 >= 0
            if (phase == MatchPhase.ThreeTeams)
                return everyoneOut;

            foreach (TeamStatus other in teams)
                if (other.TeamId != team.TeamId && IsStillIn(other, eliminated) && HasACapital(other, phase))
                    return true;   // GDD p.21: the other team holds a capital - instant
            return everyoneOut;    // last man standing
        }

        /// <summary>Capital adoption (Tudor, 2026-09-18): holding ANY capital in play counts - a team that lost its own
        /// but took another's is safe and respawns there. Open for Tudor #1: default applies it in both phases (GDD p.20
        /// describes it for the three-team last stand); the phase parameter is here so the alternative is one line.</summary>
        public static bool CountsAsHavingACapital(MatchPhase phase, bool holdsOwnCapital, bool holdsAnyCapitalInPlay) =>
            holdsAnyCapitalInPlay;

        private static bool HasACapital(TeamStatus team, MatchPhase phase) =>
            CountsAsHavingACapital(phase, team.HoldsOwnCapital, team.HoldsAnyCapitalInPlay);

        private static bool IsStillIn(TeamStatus team, List<int> eliminated) => team.InMatch && !eliminated.Contains(team.TeamId);

        private static int CountStillIn(IReadOnlyList<TeamStatus> teams, List<int> eliminated)
        {
            int count = 0;
            foreach (TeamStatus team in teams)
                if (IsStillIn(team, eliminated)) count++;
            return count;
        }

        private static MatchPhase PhaseFor(IReadOnlyList<TeamStatus> teams, List<int> eliminated)
        {
            int stillIn = CountStillIn(teams, eliminated);
            return stillIn >= 3 ? MatchPhase.ThreeTeams : stillIn == 2 ? MatchPhase.TwoTeams : MatchPhase.Over;
        }

        /// <summary>Tudor, 2026-09-18 afternoon: in a same-instant wipe the team whose last player went out LAST wins. A
        /// team with no stamp (it emptied - nobody died) counts as out before any stamped team; a true tie on the same
        /// server millisecond goes to the lower team number.</summary>
        private static int NoDrawSurvivorIndex(List<TeamStatus> candidates)
        {
            int best = 0;
            for (int i = 1; i < candidates.Count; i++)
                if (WentOutLater(candidates[i], candidates[best]))
                    best = i;
            return best;
        }

        private static bool WentOutLater(TeamStatus a, TeamStatus b)
        {
            if (a.LastOutAtMs.HasValue != b.LastOutAtMs.HasValue)
                return a.LastOutAtMs.HasValue; // a death stamp beats none
            // Server-clock stamps wrap: compare by difference, never by value.
            int later = a.LastOutAtMs.HasValue ? unchecked(a.LastOutAtMs.Value - b.LastOutAtMs.Value) : 0;
            return later > 0 || (later == 0 && a.TeamId < b.TeamId);
        }

        /// <summary>The branch PlayerLifecycle.PlayerDied takes: a death is a last-stand death (wait for a capital,
        /// counts toward a knockout) only in a live match, with no capital. Warm-up deaths always respawn - the last
        /// stand exists only to decide a knockout, and nothing counts before live.</summary>
        public static bool IsLastStandDeath(bool live, bool teamHasACapital) => live && !teamHasACapital;

        public struct CapitalHold { public int Zone; public int Owner; public int HeldSinceMs; }

        /// <summary>Where a team respawns: its own capital while it holds it ("a team holding a second capital still
        /// respawns at its own"), else the in-play capital it has held longest - the one it adopted first. Derived from
        /// the replicated hold stamps, so a new master and a late joiner get the same answer with no extra state.
        /// Neutral when it holds none.</summary>
        public static int RespawnCapital(int team, int ownCapital, IReadOnlyList<CapitalHold> capitalsInPlay)
        {
            int best = TerritoryMap.Neutral, bestSince = 0;
            foreach (CapitalHold hold in capitalsInPlay)
            {
                if (hold.Owner != team) continue;
                if (hold.Zone == ownCapital) return ownCapital;
                // Server-clock stamps wrap: compare by difference, never by value (same as TerritorySnapshot's hold maths).
                if (best == TerritoryMap.Neutral || unchecked(hold.HeldSinceMs - bestSince) < 0)
                {
                    best = hold.Zone;
                    bestSince = hold.HeldSinceMs;
                }
            }
            return best;
        }

        /// <summary>Where an ended respawn countdown puts a player, as a capital zone; Neutral = don't respawn, wait.</summary>
        public static int SpawnCapitalFor(MatchPhase phase, bool teamEliminated, int ownCapital, int respawnCapital)
        {
            if (teamEliminated || phase == MatchPhase.Over) return TerritoryMap.Neutral;
            if (phase == MatchPhase.Warmup) return ownCapital;
            if (respawnCapital != TerritoryMap.Neutral) return respawnCapital;
            // No capital. Three teams: a countdown that began before the fall still ends at home (GDD p.20's last stand
            // counts only deaths AFTER the fall - unchanged from 2.7). Two teams: last man standing, the dead can't respawn.
            return phase == MatchPhase.ThreeTeams ? ownCapital : TerritoryMap.Neutral;
        }

        /// <summary>BuildingManager's second win condition: one team holds every capital in play - only once live.</summary>
        public static int TerritoryWinner(bool live, IReadOnlyList<int> ownersOfCapitalsInPlay)
        {
            if (!live || ownersOfCapitalsInPlay == null || ownersOfCapitalsInPlay.Count == 0) return TerritoryMap.Neutral;
            int owner = ownersOfCapitalsInPlay[0];
            if (owner < 0) return TerritoryMap.Neutral;
            foreach (int o in ownersOfCapitalsInPlay)
                if (o != owner) return TerritoryMap.Neutral;
            return owner;
        }

        /// <summary>The telemetry `adopt` line: a team holding no other capital in play just took one that isn't its own.</summary>
        public static bool IsAdoption(int newOwner, int capitalTeamOfZone, int otherCapitalsInPlayHeldByNewOwner) =>
            newOwner >= 0 && capitalTeamOfZone >= 0 && capitalTeamOfZone != newOwner && otherCapitalsInPlayHeldByNewOwner == 0;
    }
}
