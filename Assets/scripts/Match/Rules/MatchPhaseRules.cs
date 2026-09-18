using System.Collections.Generic;

namespace Overpower.Match
{
    public enum MatchPhase
    {
        /// <summary>Three teams alive: losing your capital starts a last stand.</summary>
        ThreeTeams = 1,
        /// <summary>Two teams alive: losing your capital eliminates you at once (GDD p.21).</summary>
        TwoTeams = 2,
        Over = 3,
    }

    public struct TeamStatus
    {
        public int TeamId;
        public int Members;
        public int AliveMembers;
        public bool HoldsItsCapital;
    }

    public sealed class MatchPhaseResult
    {
        public MatchPhase Phase;
        public List<int> Eliminated = new List<int>();
        /// <summary>The winning team when Phase is Over, else -1.</summary>
        public int Winner = -1;
    }

    /// <summary>
    /// Who is out and which phase the match is in, recomputed from facts every client already has
    /// (capital owners, alive flags, team sizes) instead of a tally one machine keeps. That is what
    /// lets a new master after a disconnect - or a second match - reach the same answer.
    ///
    /// Capital adoption (a last-stand team keeping an enemy capital it just captured, GDD p.20) was
    /// cut for this task [C, 2026-09-18, controller amendment 6] - it needs the "controller decided"
    /// mark rather than [T]/[G] because Tudor was away; it is logged for him in assumptions-for-
    /// tudor.md. If it is ever built, it changes what MatchDirector treats as "this team's capital"
    /// (MatchDirector.CapitalOf), never this class - Recompute only ever asks "does this team
    /// currently hold ITS capital", whatever zone that capital currently is.
    /// </summary>
    public static class MatchPhaseRules
    {
        public static MatchPhaseResult Recompute(IReadOnlyCollection<int> alreadyEliminated, IReadOnlyList<TeamStatus> teams)
        {
            var result = new MatchPhaseResult();
            result.Eliminated.AddRange(alreadyEliminated);

            // Repeat until nothing changes: the first elimination moves the match to two teams, and
            // the two-team rule can then immediately apply to a team that is already without a capital.
            bool changed = true;
            while (changed)
            {
                changed = false;
                MatchPhase phase = PhaseFor(teams, result.Eliminated);
                foreach (TeamStatus team in teams)
                {
                    if (team.Members <= 0 || result.Eliminated.Contains(team.TeamId) || team.HoldsItsCapital)
                        continue;
                    bool eliminatedNow = phase == MatchPhase.TwoTeams || team.AliveMembers <= 0;
                    if (eliminatedNow)
                    {
                        result.Eliminated.Add(team.TeamId);
                        changed = true;
                    }
                }
            }

            result.Phase = PhaseFor(teams, result.Eliminated);
            if (result.Phase == MatchPhase.Over)
                foreach (TeamStatus team in teams)
                    if (team.Members > 0 && !result.Eliminated.Contains(team.TeamId))
                        result.Winner = team.TeamId;
            return result;
        }

        private static MatchPhase PhaseFor(IReadOnlyList<TeamStatus> teams, List<int> eliminated)
        {
            int remaining = 0;
            foreach (TeamStatus team in teams)
                if (team.Members > 0 && !eliminated.Contains(team.TeamId)) remaining++;
            return remaining >= 3 ? MatchPhase.ThreeTeams : remaining == 2 ? MatchPhase.TwoTeams : MatchPhase.Over;
        }
    }
}
