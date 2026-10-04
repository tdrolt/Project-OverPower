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

        /// <summary>The circle's centre on the ground (x, z) and the height of its floor: the first Tier 4 zone when the map has one (3v3v3), else the
        /// midpoint of the scoring zones in play (2v2: halfway between the two), the floor being the average of their heights so a map whose two
        /// zones sit at different levels gets a circle between them. False when there is neither (the circle cannot be placed).</summary>
        public static bool TryCentre(IReadOnlyList<Vector3> centreZones, IReadOnlyList<Vector3> scoringZones, out Vector2 centre, out float floorY)
        {
            centre = Vector2.zero;
            floorY = 0f;
            IReadOnlyList<Vector3> used = centreZones != null && centreZones.Count > 0 ? centreZones : scoringZones;
            if (used == null || used.Count == 0) return false;
            if (used == centreZones)
            {
                centre = new Vector2(used[0].x, used[0].z);
                floorY = used[0].y;
                return true;
            }
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < used.Count; i++) sum += used[i];
            sum /= used.Count;
            centre = new Vector2(sum.x, sum.z);
            floorY = sum.y;
            return true;
        }

        /// <summary>Is this zone a candidate for placing the circle: a scoring zone that is in play. A capital is a spawn and scores nothing; a zone
        /// out of play (cut off the map) is not part of this match; tier 0 is a tower that has not registered yet.</summary>
        public static bool ZoneShapesTheCircle(int tier, bool isCapital, bool outOfPlay) => tier > DominionRules.CapitalTier && !isCapital && !outOfPlay;

        /// <summary>Health lost in this slice of time: the per-second rate times the seconds, but only for a living player of a team playing sudden death
        /// while sudden death is on and the player (x, z) stands outside the circle. On the edge is inside. Anything else costs nothing.</summary>
        public static float DamageAt(bool suddenDeathOn, bool teamPlays, bool playerAlive, Vector2 position, Vector2 centre, float radius,
                                     float perSecond, float seconds)
        {
            if (!suddenDeathOn || !teamPlays || !playerAlive || perSecond <= 0f || seconds <= 0f) return 0f;
            return IsOutside(position, centre, radius) ? perSecond * seconds : 0f;
        }

        /// <summary>How long a verdict must hold before the master writes it (SuddenDeathVerdictSettle): a network wait, NOT a design number, so that the death
        /// reports of every player have reached the master before it judges. WHICH deaths count as the same moment is decided by the server stamps and
        /// DominionConfig Same Instant Tolerance Seconds (Tudor A31), never by this wait. A code constant, like the judging beat below.</summary>
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


        /// <summary>One player of the room as the judge sees them. Counts = still in the match (a player who dropped for good does not).</summary>
        public struct Player
        {
            public int Team;
            public bool Counts;
            public bool HasAliveFlag;
            public bool AliveFlag;
            public bool HasDeathStamp;
            public int DeathStampMs;
        }

        /// <summary>
        /// What the room says about each team in sudden death, per team id: how many live, when its last player to fall fell (the server stamp that
        /// player wrote with their alive flag) and whether any dead player's stamp has not arrived. Filled from the players, judged by Judge.
        /// </summary>
        public sealed class Tally
        {
            public readonly int[] Alive;
            public readonly int[] LastDeathMs;
            public readonly bool[] HasDeath;
            public readonly bool[] DeathStampMissing;

            public Tally(int teamSlots)
            {
                Alive = new int[teamSlots];
                LastDeathMs = new int[teamSlots];
                HasDeath = new bool[teamSlots];
                DeathStampMissing = new bool[teamSlots];
            }

            public void Clear()
            {
                Array.Clear(Alive, 0, Alive.Length);
                Array.Clear(LastDeathMs, 0, LastDeathMs.Length);
                Array.Clear(HasDeath, 0, HasDeath.Length);
                Array.Clear(DeathStampMissing, 0, DeathStampMissing.Length);
            }

            /// <summary>Counts one player: alive (CountsAsAlive: no flag yet means alive), or dead with their stamp. A player who dropped for good counts for nothing.</summary>
            public void Add(Player p)
            {
                if (!p.Counts || p.Team < 0 || p.Team >= Alive.Length) return;
                if (CountsAsAlive(p.HasAliveFlag, p.AliveFlag))
                {
                    Alive[p.Team]++;
                    return;
                }
                if (!p.HasDeathStamp)
                {
                    DeathStampMissing[p.Team] = true;
                    return;
                }
                // The team's last death is the latest stamp (compared as unchecked differences: the server clock wraps).
                if (!HasDeath[p.Team] || unchecked(p.DeathStampMs - LastDeathMs[p.Team]) > 0) LastDeathMs[p.Team] = p.DeathStampMs;
                HasDeath[p.Team] = true;
            }
        }

        /// <summary>
        /// The verdict from the whole picture (Tudor A31). While anyone of the sudden-death teams lives it is Evaluate's: one team left wins, two or more
        /// go on. When nobody lives, the stamps decide: the team whose last player fell LATEST wins (it lasted longer); only if the last players of
        /// every team that fell last went down at the same server moment (within the tolerance, one network frame) does sudden death start over. A dead
        /// player's stamp that has not arrived yet means wait (Ongoing): the master never judges on a guess.
        /// </summary>
        public static SuddenDeathResult Judge(Tally tally, int[] teamsInSuddenDeath, int toleranceMs)
        {
            var ongoing = new SuddenDeathResult { State = SuddenDeathState.Ongoing, Team = -1 };
            if (tally == null || teamsInSuddenDeath == null || teamsInSuddenDeath.Length == 0) return ongoing;
            SuddenDeathResult byCount = Evaluate(tally.Alive, teamsInSuddenDeath);
            if (byCount.State != SuddenDeathState.Replay) return byCount;

            int latest = -1;
            foreach (int team in teamsInSuddenDeath)
            {
                if (team < 0 || team >= tally.Alive.Length) continue;
                if (tally.DeathStampMissing[team]) return ongoing;
                if (!tally.HasDeath[team]) continue; // nobody of this team was ever there: it fell at no moment
                if (latest < 0 || unchecked(tally.LastDeathMs[team] - tally.LastDeathMs[latest]) > 0) latest = team;
            }
            if (latest < 0) return byCount; // nobody anywhere: the old replay

            int sameMoment = 0;
            foreach (int team in teamsInSuddenDeath)
            {
                if (team < 0 || team >= tally.Alive.Length || !tally.HasDeath[team]) continue;
                if (unchecked(tally.LastDeathMs[latest] - tally.LastDeathMs[team]) <= Math.Max(0, toleranceMs)) sameMoment++;
            }
            return sameMoment > 1 ? byCount : new SuddenDeathResult { State = SuddenDeathState.Won, Team = latest };
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
