using System;
using UnityEngine;

namespace Overpower.Dominion
{
    /// <summary>Where sudden death stands: it goes on, one team has won it, or nobody is left and it starts over.</summary>
    public enum SuddenDeathState
    {
        Ongoing = 0,
        Won = 1,
        Replay = 2,
    }

    /// <summary>The verdict of SuddenDeathRules.Evaluate. Team is the winner when State is Won, else -1.</summary>
    public struct SuddenDeathResult
    {
        public SuddenDeathState State;
        public int Team;
    }

    /// <summary>
    /// Sudden death (Task 1 rules; the circle and its damage are Task 8): a circle that shrinks over time, and the last team with anyone
    /// alive wins. Times are the room's server clock (int ms that wraps; compared as unchecked(a - b)).
    /// </summary>
    public static class SuddenDeathRules
    {
        /// <summary>The circle's radius: the start radius until startMs, then a straight line down to the final radius over shrinkSeconds, and
        /// the final radius from then on. It never grows - the result is never above the start radius, and never rises with later time - so a
        /// client whose clock is a little late can never push the wall back out.</summary>
        public static float Radius(int startMs, int nowMs, float shrinkSeconds, float startRadius, float finalRadius)
        {
            float final = Math.Min(finalRadius, startRadius); // a final larger than the start would make it grow
            int elapsedMs = unchecked(nowMs - startMs);
            if (elapsedMs <= 0) return startRadius;
            if (shrinkSeconds <= 0f) return final;
            float t = Math.Min(1f, elapsedMs / 1000f / shrinkSeconds);
            return Math.Min(startRadius, startRadius + (final - startRadius) * t);
        }

        /// <summary>True when the position (on the XZ plane) is beyond the radius. Exactly on the edge counts as inside.</summary>
        public static bool IsOutside(Vector2 pos, Vector2 centre, float radius) =>
            (pos - centre).sqrMagnitude > radius * radius;

        /// <summary>The only team in sudden death with anyone alive; -1 while two or more still have someone, and -1 when nobody does (see
        /// Evaluate for what that means). A team not in sudden death never wins it, however many of its players live.</summary>
        public static int LastTeamStanding(int[] aliveCountPerTeam, int[] teamsInSuddenDeath)
        {
            Count(aliveCountPerTeam, teamsInSuddenDeath, out int standing, out _);
            return standing;
        }

        /// <summary>True when no team in sudden death has anyone alive.</summary>
        public static bool NobodyLeft(int[] aliveCountPerTeam, int[] teamsInSuddenDeath)
        {
            Count(aliveCountPerTeam, teamsInSuddenDeath, out _, out int teamsAlive);
            return teamsAlive == 0 && teamsInSuddenDeath != null && teamsInSuddenDeath.Length > 0;
        }

        /// <summary>The verdict. One team with anyone alive: Won. Two or more: Ongoing. Nobody left (the last players of the sudden-death
        /// teams fall in the same instant): Replay - sudden death starts over, everyone back at their spawn and the circle full size again
        /// (Tudor A8; there is no points tie-break). Missing arrays decide nothing (Ongoing).</summary>
        public static SuddenDeathResult Evaluate(int[] aliveCountPerTeam, int[] teamsInSuddenDeath)
        {
            if (aliveCountPerTeam == null || teamsInSuddenDeath == null || teamsInSuddenDeath.Length == 0)
                return new SuddenDeathResult { State = SuddenDeathState.Ongoing, Team = -1 };
            Count(aliveCountPerTeam, teamsInSuddenDeath, out int standing, out int teamsAlive);
            if (teamsAlive == 0) return new SuddenDeathResult { State = SuddenDeathState.Replay, Team = -1 };
            if (teamsAlive == 1) return new SuddenDeathResult { State = SuddenDeathState.Won, Team = standing };
            return new SuddenDeathResult { State = SuddenDeathState.Ongoing, Team = -1 };
        }

        private static void Count(int[] alive, int[] teams, out int standing, out int teamsAlive)
        {
            standing = -1;
            teamsAlive = 0;
            if (alive == null || teams == null) return;
            foreach (int team in teams)
            {
                if (team < 0 || team >= alive.Length || alive[team] <= 0) continue;
                teamsAlive++;
                standing = team;
            }
            if (teamsAlive != 1) standing = -1;
        }
    }
}
