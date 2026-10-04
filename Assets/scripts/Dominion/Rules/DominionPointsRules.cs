using System;
using ExitGames.Client.Photon;
using Overpower.Match;

namespace Overpower.Dominion
{
    /// <summary>Everything the master knows when it decides what the room's points and the centre's clock should say next. Plain ints and
    /// arrays, so the tests build any situation by hand.</summary>
    public struct DominionTickInput
    {
        public int NowMs;
        /// <summary>Server ms of the last points tick this master paid (or started counting from); 0 = the points clock has not started yet.</summary>
        public int LastTickMs;
        /// <summary>The room's Dominion values (stage, round, end time, zones-reset-for); the points and dCtr to build on come in separately.</summary>
        public DominionRoomState Room;
        /// <summary>The points to build on: the last value this master wrote while its echo is pending, else the room's.</summary>
        public int[] BasePoints;
        /// <summary>The dCtr to build on, same rule. 0 = none written.</summary>
        public int BaseCentreMs;
        public int[] TeamsInMatch;
        public int[] ZoneOwner;
        public int[] ZoneTier;
        public bool[] IsSpawnZone;
        public int[] PointsPerTier;
        /// <summary>True when this match has a centre that pays lumps (3v3v3 on a map with a Tier 4 zone).</summary>
        public bool HasCentre;
        public int CentreOwner;
        public int CentrePoints;
        public int CentreFirstMs;
        public int CentreIntervalMs;
        /// <summary>Bounty points won since the last write, per team id (null = none). Added to the points in the same write.</summary>
        public int[] PendingBounty;
    }

    /// <summary>What the master does about the points now.</summary>
    public sealed class DominionTickPlan
    {
        /// <summary>The one check-and-set to send, or null when nothing changes.</summary>
        public DominionWrite Write;
        /// <summary>The points clock after this look (0 = not running).</summary>
        public int NewLastTickMs;
        /// <summary>The centre paid out in this write; CentreTeam is who got it (-1 = nobody was holding it).</summary>
        public bool CentrePaid;
        public int CentreTeam = -1;
        public int CentrePoints;
        /// <summary>The write schedules the centre's first payout (dCtr was missing).</summary>
        public bool CentreScheduled;
    }

    /// <summary>The pure rules of Dominion Task 3: when a points tick is due, what a tick adds, when the centre pays and who gets it, what
    /// the next dCtr is, and the one check-and-set that carries it all. DominionDirector reads the room and the territory, asks this and
    /// sends the answer.</summary>
    public static class DominionPointsRules
    {
        public const int TickMs = 1000;

        /// <summary>True when a points tick is due: a second since the last one. A frame up to two seconds late pays that one tick and keeps the
        /// beat (the next look pays the catch-up second); later than that pays one tick and restarts the beat from now, so a hitch or a master
        /// switch never pays a burst. Wrap-safe.</summary>
        public static bool TickDue(int nowMs, int lastTickMs, out int newLastTickMs)
        {
            int elapsed = unchecked(nowMs - lastTickMs);
            newLastTickMs = lastTickMs;
            if (elapsed < TickMs) return false;
            newLastTickMs = elapsed < 2 * TickMs ? unchecked(lastTickMs + TickMs) : nowMs;
            return true;
        }

        /// <summary>The bounty in points for a capture: bountyPoints when the zone was held without a break for holdMs by another team, else 0.</summary>
        public static int BountyPoints(int lastHeldMs, int holdMs, int lastOwner, int newOwner, int bountyPoints) =>
            bountyPoints > 0 && DominionRules.BountyDue(lastHeldMs, holdMs, lastOwner, newOwner) ? bountyPoints : 0;

        /// <summary>What the master writes now, if anything. Nothing outside a Round, once the round's time is up, or before the zones were
        /// reset for this round (dRz = dEnd: so a zone taken in the break never pays). A write carries dPts and/or dCtr and expects the stage,
        /// round and end time it was computed from, so a late one is refused once the stage has moved.</summary>
        public static DominionTickPlan Plan(DominionTickInput i)
        {
            var plan = new DominionTickPlan();
            DominionRoomState room = i.Room;
            if (!room.HasRound || room.Stage != DominionStage.Round) return plan;
            if (room.ResetFor != room.EndMs) return plan;
            if (room.EndMs != 0 && MatchStartRules.HasReached(i.NowMs, room.EndMs)) { plan.NewLastTickMs = i.LastTickMs; return plan; }

            int[] points = Slots(i.BasePoints);
            bool pointsChanged = false;

            if (i.PendingBounty != null)
                for (int t = 0; t < i.PendingBounty.Length && t < points.Length; t++)
                    if (i.PendingBounty[t] > 0) { points[t] += i.PendingBounty[t]; pointsChanged = true; }

            if (i.LastTickMs == 0) plan.NewLastTickMs = i.NowMs;
            else if (TickDue(i.NowMs, i.LastTickMs, out int newLast))
            {
                plan.NewLastTickMs = newLast;
                if (i.TeamsInMatch != null)
                    foreach (int t in i.TeamsInMatch)
                    {
                        if (t < 0 || t >= points.Length) continue;
                        int add = DominionRules.PointsThisTick(i.ZoneOwner, i.ZoneTier, i.IsSpawnZone, t, i.PointsPerTier);
                        if (add > 0) { points[t] += add; pointsChanged = true; }
                    }
            }
            else plan.NewLastTickMs = i.LastTickMs;

            bool centreChanged = false;
            int centreNext = i.BaseCentreMs;
            if (i.HasCentre)
            {
                if (i.BaseCentreMs == 0)
                {
                    centreNext = DominionRules.NextCentrePayoutMs(i.NowMs, i.NowMs, i.CentreFirstMs, i.CentreIntervalMs);
                    centreChanged = true;
                    plan.CentreScheduled = true;
                }
                else if (DominionRules.CentrePayoutDue(i.BaseCentreMs, i.NowMs))
                {
                    plan.CentrePaid = true;
                    int team = DominionRules.CentrePayoutTeam(i.CentreOwner);
                    if (team >= 0 && team < points.Length && Contains(i.TeamsInMatch, team) && i.CentrePoints > 0)
                    {
                        points[team] += i.CentrePoints;
                        pointsChanged = true;
                        plan.CentreTeam = team;
                        plan.CentrePoints = i.CentrePoints;
                    }
                    centreNext = DominionRules.NextCentrePayoutMs(i.BaseCentreMs, i.NowMs, 0, i.CentreIntervalMs);
                    centreChanged = true;
                }
            }

            if (!pointsChanged && !centreChanged) return plan;
            var props = new Hashtable();
            if (pointsChanged) props[DominionKeys.Points] = points;
            if (centreChanged) props[DominionKeys.CentrePayout] = centreNext;
            plan.Write = new DominionWrite
            {
                What = "points",
                Props = props,
                Expected = new Hashtable
                {
                    { DominionKeys.Stage, (int)room.Stage },
                    { DominionKeys.Round, room.Round },
                    { DominionKeys.StageEnd, room.EndMs },
                },
            };
            return plan;
        }

        private static bool Contains(int[] list, int value)
        {
            if (list == null) return false;
            foreach (int v in list) if (v == value) return true;
            return false;
        }

        private static int[] Slots(int[] values)
        {
            var copy = new int[Math.Max(DominionKeys.TeamSlots, values != null ? values.Length : 0)];
            if (values != null) Array.Copy(values, copy, values.Length);
            return copy;
        }
    }
}
