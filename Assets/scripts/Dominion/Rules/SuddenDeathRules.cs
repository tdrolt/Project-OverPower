using System;
using System.Collections.Generic;
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

        /// <summary>The circle's start radius for the match size: 2 teams = 2v2, otherwise 3v3v3.</summary>
        public static float StartRadiusFor(int teamCount, float radius2v2, float radius3v3v3) => teamCount <= 2 ? radius2v2 : radius3v3v3;

        /// <summary>The circle's final radius for the match size: 2 teams = 2v2, otherwise 3v3v3.</summary>
        public static float FinalRadiusFor(int teamCount, float radius2v2, float radius3v3v3) => teamCount <= 2 ? radius2v2 : radius3v3v3;

        /// <summary>The circle's centre on the ground (x, z): the first Tier 4 zone when the map has one (3v3v3), else the midpoint of the scoring zones
        /// (2v2: halfway between the two). False when there is neither (the circle cannot be placed).</summary>
        public static bool TryCentre(IReadOnlyList<Vector2> centreZones, IReadOnlyList<Vector2> scoringZones, out Vector2 centre)
        {
            centre = Vector2.zero;
            if (centreZones != null && centreZones.Count > 0)
            {
                centre = centreZones[0];
                return true;
            }
            if (scoringZones == null || scoringZones.Count == 0) return false;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < scoringZones.Count; i++) sum += scoringZones[i];
            centre = sum / scoringZones.Count;
            return true;
        }

        /// <summary>Health lost in this slice of time: the per-second rate times the seconds, but only for a living player of a team playing sudden death
        /// while sudden death is on and the player (x, z) stands outside the circle. On the edge is inside. Anything else costs nothing.</summary>
        public static float DamageAt(bool suddenDeathOn, bool teamPlays, bool playerAlive, Vector2 position, Vector2 centre, float radius,
                                     float perSecond, float seconds)
        {
            if (!suddenDeathOn || !teamPlays || !playerAlive || perSecond <= 0f || seconds <= 0f) return 0f;
            return IsOutside(position, centre, radius) ? perSecond * seconds : 0f;
        }

        /// <summary>How long a verdict must hold before the master writes it (SuddenDeathVerdictSettle): deaths closer together than this count as the same instant.
        /// A network beat like the judging beat above - a stalled frame or a slow link spreads two simultaneous deaths by well under this.</summary>
        public const float VerdictSettleSeconds = 0.5f;

        /// <summary>Is a player alive, from their alive flag in the room? A player who has never died has no flag at all (it is only written when it
        /// changes), so a missing flag means alive; only an explicit false is dead. Found in the first 3-client check: counting only an explicit true
        /// ended sudden death at once, with the one team that had died earlier as its winner.</summary>
        public static bool CountsAsAlive(bool hasFlag, bool flag) => !hasFlag || flag;

        /// <summary>Does this team play sudden death (is it among the tied teams)? A team not tied waits dead.</summary>
        public static bool TeamPlays(int team, int[] suddenDeathTeams) => suddenDeathTeams != null && team >= 0 && Array.IndexOf(suddenDeathTeams, team) >= 0;

        /// <summary>The master decides the end only from this moment: a short beat after the circle starts, so every player's alive flag has had time to
        /// come back true after the reset (someone who died in round 3 is still dead in the room until their own client revives them).</summary>
        public static bool MayEvaluate(int startMs, int nowMs) => unchecked(nowMs - startMs) >= EvaluationBeatMs;

        /// <summary>How long after the circle starts before the master judges. A network beat, not a design number: the room's alive flags need this long to
        /// come back true after everyone is revived.</summary>
        public const int EvaluationBeatMs = 1500;

        /// <summary>Seconds until the circle stops shrinking (0 once it has, and 0 before there is a circle). The shrink starts at startMs.</summary>
        public static float SecondsUntilStopped(int startMs, int nowMs, float shrinkSeconds)
        {
            if (startMs == 0) return 0f;
            int stopsAt = unchecked(startMs + (int)Math.Round(Math.Max(0f, shrinkSeconds) * 1000f));
            int remainingMs = unchecked(stopsAt - nowMs);
            return remainingMs <= 0 ? 0f : remainingMs / 1000f;
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
