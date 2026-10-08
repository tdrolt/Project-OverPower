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
        /// <summary>The dPseq to build on, same rule. 0 = none written yet.</summary>
        public int BasePointsSeq;
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
        public int CentrePoints;    }

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

        /// <summary>Do points, bounties and the centre accrue in the room's stage right now? In a Round once the zones were reset for it (dRz = dEnd:
        /// a zone taken in the break never pays), and in Overtime (Tudor A50: zones, bounties and the centre keep paying; the zones are not reset for
        /// it, so dRz still names the round's end). Everything else (break, sudden death, over) pays nothing.</summary>
        public static bool PointsRun(DominionRoomState room) =>
            room.HasRound && (room.Stage == DominionStage.Overtime || (room.Stage == DominionStage.Round && room.ResetFor == room.EndMs));

        /// <summary>What the master writes now, if anything. Nothing outside a Round, or before the zones were reset for this round (dRz = dEnd:
        /// so a zone taken in the break never pays). Once the round's time is up only one thing is still paid: a centre payout that fell due at
        /// or before the end (Tudor's A18: the buzzer payout counts), to whoever holds the centre at that moment. A write carries dPts and/or
        /// dCtr plus the next dPseq, and expects the stage, round, end time and dPseq it was computed from, so a late one is refused once the
        /// stage has moved and two masters cannot both add to the same points.</summary>
        public static DominionTickPlan Plan(DominionTickInput i)
        {
            var plan = new DominionTickPlan();
            DominionRoomState room = i.Room;
            if (!PointsRun(room)) return plan;
            // The buzzer: the clock stops, but a centre payout that was due by the end is still owed (the stage writer scores the round
            // from the points this write leaves).
            bool buzzer = room.EndMs != 0 && MatchStartRules.HasReached(i.NowMs, room.EndMs);
            plan.NewLastTickMs = i.LastTickMs;
            if (buzzer && !(i.HasCentre && i.BaseCentreMs != 0 && DominionRules.CentrePayoutDue(i.BaseCentreMs, i.NowMs)
                            && unchecked(room.EndMs - i.BaseCentreMs) >= 0))
                return plan;

            int[] points = Slots(i.BasePoints);
            bool pointsChanged = false;

            if (!buzzer && i.PendingBounty != null)
                for (int t = 0; t < i.PendingBounty.Length && t < points.Length; t++)
                    if (i.PendingBounty[t] > 0) { points[t] += i.PendingBounty[t]; pointsChanged = true; }

            if (!buzzer && i.LastTickMs == 0) plan.NewLastTickMs = i.NowMs;
            else if (!buzzer && TickDue(i.NowMs, i.LastTickMs, out int newLast))
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

            bool centreChanged = false;
            int centreNext = i.BaseCentreMs;
            if (i.HasCentre)
            {
                if (i.BaseCentreMs == 0 && !buzzer)
                {
                    centreNext = DominionRules.NextCentrePayoutMs(i.NowMs, i.NowMs, i.CentreFirstMs, i.CentreIntervalMs);
                    centreChanged = true;
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
            props[DominionKeys.PointsSeq] = unchecked(i.BasePointsSeq + 1);
            plan.Write = new DominionWrite
            {
                What = "points",
                Props = props,
                Expected = new Hashtable
                {
                    { DominionKeys.Stage, (int)room.Stage },
                    { DominionKeys.Round, room.Round },
                    { DominionKeys.StageEnd, room.EndMs },
                    { DominionKeys.PointsSeq, i.BasePointsSeq == 0 ? null : (object)i.BasePointsSeq }, // null: no points write yet, the key must be absent
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
