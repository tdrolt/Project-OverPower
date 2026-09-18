using System.Collections.Generic;

namespace Overpower.Match
{
    public enum MatchPhase
    {
        /// <summary>No team has been eliminated yet.</summary>
        ThreeTeams = 1,
        /// <summary>At least one team is out, and two or more teams with players remain.</summary>
        TwoTeams = 2,
        Over = 3,
    }

    public struct TeamStatus
    {
        public int TeamId;
        public int Members;
        /// <summary>How many of this team's members currently died with the capital already lost
        /// ("out for the last stand" - see IsLastStandDeath). A player on an ordinary respawn
        /// countdown, with the capital still held at the moment of death, is never counted here.</summary>
        public int MembersOutForLastStand;
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
    /// (capital owners, who has died since the capital fell, team sizes) instead of a tally one
    /// machine keeps. That is what lets a new master after a disconnect - or a second match - reach
    /// the same answer.
    ///
    /// Two separate rules, not one, review round 2: Tudor's rule governs what MOVES the phase - the
    /// phase changes ONLY on an elimination, never on who is connected, so a player joining or
    /// leaving can never move it and a lone player can never win by simply being the only one in the
    /// room. GDD p.21 governs what an already-decided phase MEANS: while the phase is ThreeTeams, a
    /// team without its capital is only out once every one of its members has died since (the last
    /// stand); the INSTANT the phase truly is TwoTeams, losing your capital is enough on its own - no
    /// last stand. A two-player test never reaches that second rule on its own, because it stays
    /// ThreeTeams (Tudor's rule) until an actual third team is eliminated.
    ///
    /// Capital adoption (a last-stand team keeping an enemy capital it just captures, GDD p.20) was
    /// cut for this task; if it is ever built, it changes what MatchDirector treats as "this team's
    /// capital" (MatchDirector.CapitalOf), never this class.
    /// </summary>
    public static class MatchPhaseRules
    {
        public static MatchPhaseResult Recompute(IReadOnlyCollection<int> alreadyEliminated, IReadOnlyList<TeamStatus> teams)
        {
            var result = new MatchPhaseResult();
            result.Eliminated.AddRange(alreadyEliminated);

            // Fixpoint: eliminating a team can move the phase itself to TwoTeams, and the instant it
            // does, GDD p.21's rule applies to every OTHER capital-less team at once, not next tick -
            // so a second pass may find more once the first pass's own phase change is known.
            bool changed = true;
            while (changed)
            {
                changed = false;
                MatchPhase phase = PhaseFor(teams, result.Eliminated);
                foreach (TeamStatus team in teams)
                {
                    if (team.Members <= 0 || result.Eliminated.Contains(team.TeamId) || team.HoldsItsCapital)
                        continue;
                    bool eliminatedNow = phase == MatchPhase.TwoTeams || team.MembersOutForLastStand >= team.Members;
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
            // Tudor's rule: nothing before the first elimination can move the phase, however many
            // teams are actually connected right now.
            if (eliminated.Count == 0)
                return MatchPhase.ThreeTeams;

            int remaining = 0;
            foreach (TeamStatus team in teams)
                if (team.Members > 0 && !eliminated.Contains(team.TeamId)) remaining++;
            return remaining >= 2 ? MatchPhase.TwoTeams : MatchPhase.Over;
        }

        /// <summary>GDD p.20: a death only counts toward a team's last stand when the capital was
        /// ALREADY lost at the moment of death - an ordinary respawn countdown (the capital still
        /// held) never counts, even though the player is briefly not alive either way. The single
        /// source of truth for the branch PlayerLifecycle.PlayerDied takes.</summary>
        public static bool IsLastStandDeath(bool teamHoldsCapitalAtDeath) => !teamHoldsCapitalAtDeath;

        /// <summary>Review round 2: BuildingManager's other win condition (holding every capital) has
        /// no player-count check of its own, unlike elimination, which requires two teams by
        /// construction. A lone player must not win by draining and taking capitals nobody is
        /// defending.</summary>
        public static bool TerritoryWinCounts(int teamsWithPlayers) => teamsWithPlayers >= 2;
    }
}
