using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Vision;

namespace Overpower.Tests
{
    /// <summary>
    /// Vision Task 10 (Tudor 2026-09-30): while a team holds the centre a sonar wave goes out at capture and every
    /// interval after, fast, through walls. An enemy is caught each time the front passes over where they are now.
    /// Every number here is the test's own; none is the tuned value.
    /// </summary>
    public class CentreScanRulesTests
    {
        private const int Team = 2;
        private const int Interval = 10000;
        private const float Speed = 50f;
        private const float Max = 120f;
        private const int Frame = 20; // ms per fake frame

        private sealed class Catcher
        {
            private readonly int scanStart;
            private readonly float speed;
            private int prevNow;
            public int Caught;

            public Catcher(int scanStart, float speed) { this.scanStart = scanStart; this.speed = speed; prevNow = scanStart; }

            // Steps the fake clock to 'now' and tests the player's position (distance from the centre) at that moment.
            public void Step(int now, float distanceNow)
            {
                float prev = CentreScanRules.WaveRadius(scanStart, prevNow, speed);
                float cur = CentreScanRules.WaveRadius(scanStart, now, speed);
                if (CentreScanRules.FrontSwept(prev, cur, distanceNow)) Caught++;
                prevNow = now;
            }
        }

        // ---- when a scan happens ----

        [Test]
        public void TheFirstScanStartsTheMomentTheCentreIsCaptured()
        {
            Assert.IsTrue(CentreScanRules.ActiveScan(Team, 5000, 5000, Interval, false, out int start));
            Assert.AreEqual(5000, start);
        }

        [Test]
        public void TheScanKeepsItsStartTimeUntilAFullIntervalHasPassed()
        {
            Assert.IsTrue(CentreScanRules.ActiveScan(Team, 5000, 5000 + Interval - 1, Interval, false, out int start));
            Assert.AreEqual(5000, start);
        }

        [Test]
        public void TheNextScanStartsOneIntervalAfterCapture()
        {
            Assert.IsTrue(CentreScanRules.ActiveScan(Team, 5000, 5000 + Interval, Interval, false, out int start));
            Assert.AreEqual(5000 + Interval, start);
        }

        [Test]
        public void TheLatestScanIsTheOneReturnedManyIntervalsLater()
        {
            Assert.IsTrue(CentreScanRules.ActiveScan(Team, 5000, 5000 + 3 * Interval + 7, Interval, false, out int start));
            Assert.AreEqual(5000 + 3 * Interval, start);
        }

        [Test]
        public void NoScanWhileTheCentreIsNeutral()
        {
            Assert.IsFalse(CentreScanRules.ActiveScan(-1, 5000, 9000, Interval, false, out _));
        }

        [Test]
        public void NoScanOnceTheMapHasShrunk()
        {
            Assert.IsFalse(CentreScanRules.ActiveScan(Team, 5000, 9000, Interval, true, out _));
        }

        [Test]
        public void ARuleReadBeforeTheCaptureMomentStillStartsAtTheCapture()
        {
            // A client whose clock reads a few ms behind the stamp must not see a scan from the past.
            Assert.IsTrue(CentreScanRules.ActiveScan(Team, 5000, 4990, Interval, false, out int start));
            Assert.AreEqual(5000, start);
        }

        [Test]
        public void TheServerClockWrappingAroundDoesNotBreakTheSchedule()
        {
            int held = int.MaxValue - 4000; // captured just before the 32-bit server clock wraps
            int now = unchecked(held + Interval + 500);
            Assert.Less(now, 0, "the test needs a clock that has wrapped");
            Assert.IsTrue(CentreScanRules.ActiveScan(Team, held, now, Interval, false, out int start));
            Assert.AreEqual(unchecked(held + Interval), start);
        }

        [Test]
        public void TheWaveRadiusAfterAWrapIsStillTimeTimesSpeed()
        {
            int start = int.MaxValue - 100;
            int now = unchecked(start + 1000);
            Assert.AreEqual(Speed, CentreScanRules.WaveRadius(start, now, Speed), 0.001f);
        }

        // ---- the wave ----

        [Test]
        public void TheWaveGrowsAtItsSpeedFromZero()
        {
            Assert.AreEqual(0f, CentreScanRules.WaveRadius(1000, 1000, Speed), 0.001f);
            Assert.AreEqual(Speed * 0.5f, CentreScanRules.WaveRadius(1000, 1500, Speed), 0.001f);
            Assert.AreEqual(Speed * 3f, CentreScanRules.WaveRadius(1000, 4000, Speed), 0.001f);
        }

        [Test]
        public void TheRadiusBeforeTheScanStartsIsZero()
        {
            Assert.AreEqual(0f, CentreScanRules.WaveRadius(1000, 900, Speed), 0.001f);
        }

        [Test]
        public void TheWaveEndsPastTheFarthestArenaPoint()
        {
            Assert.IsTrue(CentreScanRules.IsTravelling(Max - 0.1f, Max));
            Assert.IsFalse(CentreScanRules.IsTravelling(Max + 0.1f, Max));
        }

        // ---- who the front catches ----

        [Test]
        public void TheFrontSweepsADistanceInsideThePreviousToCurrentBand()
        {
            Assert.IsTrue(CentreScanRules.FrontSwept(10f, 12f, 11f));
            Assert.IsTrue(CentreScanRules.FrontSwept(10f, 12f, 12f), "the new edge counts");
            Assert.IsFalse(CentreScanRules.FrontSwept(10f, 12f, 10f), "the old edge was caught last frame");
            Assert.IsFalse(CentreScanRules.FrontSwept(10f, 12f, 9f));
            Assert.IsFalse(CentreScanRules.FrontSwept(10f, 12f, 13f));
        }

        [Test]
        public void AFirstFrameStartingAtZeroCatchesAnEnemyStandingNearTheCentre()
        {
            // prevRadius is 0 on the first frame of a scan; an enemy at distance 0.5 inside the first band is caught.
            Assert.IsTrue(CentreScanRules.FrontSwept(0f, 1f, 0.5f));
        }

        [Test]
        public void APlayerStandingStillIsCaughtExactlyOncePerScan()
        {
            Catcher c = new Catcher(1000, Speed);
            for (int t = 1000; t <= 1000 + 5000; t += Frame) c.Step(t, 63f);
            Assert.AreEqual(1, c.Caught);
        }

        [Test]
        public void AWalkingPlayerIsCaughtOnce()
        {
            Catcher c = new Catcher(1000, Speed);
            float d = 20f;
            for (int t = 1000; t <= 1000 + 5000; t += Frame)
            {
                c.Step(t, d);
                d += 6f * Frame / 1000f;
            }
            Assert.AreEqual(1, c.Caught);
        }

        [Test]
        public void ATeleportFromOutsideTheFrontToInsideItIsNeverCaught()
        {
            Catcher c = new Catcher(1000, Speed);
            float d = 80f;
            bool jumped = false;
            for (int t = 1000; t <= 1000 + 5000; t += Frame)
            {
                // At 0.5 s the front is at 25 m; the player is ahead of it at 80 m, then jumps to 10 m (behind the front).
                if (!jumped && t >= 1500) { d = 10f; jumped = true; }
                c.Step(t, d);
            }
            Assert.AreEqual(0, c.Caught);
        }

        [Test]
        public void ATeleportFromInsideBackOutAheadOfTheFrontIsCaughtTwice()
        {
            Catcher c = new Catcher(1000, Speed);
            float d = 5f;
            bool jumped = false;
            for (int t = 1000; t <= 1000 + 5000; t += Frame)
            {
                // The player stands at 5 m, caught as the front goes over; at 1.0 s (front 50 m) jumps ahead to 90 m.
                if (!jumped && t >= 2000) { d = 90f; jumped = true; }
                c.Step(t, d);
            }
            Assert.AreEqual(2, c.Caught);
        }

        // ---- how a client calls the rules frame after frame (Task 10 review) ----

        private const float ScanMax = 120f;

        // Calls the tracker every step and counts the frames a player standing at 'distance' is inside the swept band.
        private sealed class TrackedCatcher
        {
            private readonly ScanBandTracker tracker = new ScanBandTracker();
            public int Caught;
            public ScanFrame Last;
            public int Holder = Team;
            public int HeldSince = 1000;

            public void Step(int now, float distance)
            {
                Last = tracker.Step(Holder, HeldSince, now, Interval, false, Speed, ScanMax);
                if (Last.Holding && CentreScanRules.FrontSwept(Last.PrevRadius, Last.Radius, distance)) Caught++;
            }
        }

        [Test]
        public void AStillPlayerIsCaughtOncePerScan_AcrossSeveralScans()
        {
            TrackedCatcher c = new TrackedCatcher();
            for (int t = 1000; t < 1000 + 3 * Interval; t += Frame) c.Step(t, 30f);
            Assert.AreEqual(3, c.Caught);
        }

        [Test]
        public void NobodyIsCaughtOnANewScansFirstFrame_JustBecauseTheOldWaveWasFar()
        {
            TrackedCatcher c = new TrackedCatcher();
            int t = 1000;
            for (; t < 1000 + Interval; t += Frame) c.Step(t, 30f);
            int before = c.Caught;
            c.Step(1000 + Interval, 30f); // the frame the next scan starts: radius 0
            Assert.AreEqual(before, c.Caught);
            Assert.AreEqual(0f, c.Last.PrevRadius, 0.0001f);
            Assert.AreEqual(0f, c.Last.Radius, 0.0001f);
        }

        [Test]
        public void ANewHolder_StartsAFreshBandFromZero()
        {
            TrackedCatcher c = new TrackedCatcher();
            for (int t = 1000; t < 4000; t += Frame) c.Step(t, 30f);
            c.Holder = 5;
            c.HeldSince = 4000;
            c.Step(4100, 2f); // 100 ms into the new holder's scan: front at 5 m, the player at 2 m is inside the new band
            Assert.AreEqual(0f, c.Last.PrevRadius, 0.0001f);
            Assert.AreEqual(5f, c.Last.Radius, 0.001f);
            Assert.AreEqual(2, c.Caught, "once for the first holder's wave over 30 m, once for the new holder's wave over 2 m");
        }

        [Test]
        public void AHalfSecondFrame_StillCatchesAPlayerOnce()
        {
            TrackedCatcher c = new TrackedCatcher();
            for (int t = 1000; t <= 1000 + Interval; t += 500) c.Step(t, 30f);
            Assert.AreEqual(1, c.Caught);
        }

        [Test]
        public void ALateCaptureStamp_StillCatchesAPlayerStandingInTheCentre()
        {
            // The stamp arrives 100 ms after the capture moment: the first frame this client sees is 5 m into the wave.
            TrackedCatcher c = new TrackedCatcher();
            c.Step(1100, 3f);
            Assert.AreEqual(1, c.Caught);
            for (int t = 1120; t < 1000 + Interval - Frame; t += Frame) c.Step(t, 3f);
            Assert.AreEqual(1, c.Caught);
        }

        [Test]
        public void SomeoneJoiningMidWave_DoesNotCatchEveryoneBehindTheFront()
        {
            TrackedCatcher c = new TrackedCatcher();
            c.Step(1000 + 3000, 30f); // first frame ever, 3 s into the scan
            Assert.AreEqual(0, c.Caught);
            Assert.AreEqual(150f, c.Last.Radius, 0.01f);
        }

        [Test]
        public void TheClockSteppingBackAFewMs_DoesNotCatchAPlayerTwice()
        {
            TrackedCatcher c = new TrackedCatcher();
            int[] clock = { 1000, 1020, 1040, 1025, 1045, 1060, 1100, 1140, 1200 };
            foreach (int t in clock) c.Step(t, 3.5f);
            Assert.AreEqual(1, c.Caught);
        }

        [Test]
        public void AZeroServerClock_IsSkippedAndNotRemembered()
        {
            TrackedCatcher c = new TrackedCatcher();
            c.Step(1020, 0.5f);
            Assert.AreEqual(1, c.Caught);
            c.Step(0, 0.5f);
            Assert.IsFalse(c.Last.Holding);
            c.Step(1040, 0.5f);
            Assert.AreEqual(1, c.Caught, "the frame after the zero continues from 1020, not from 0");
            Assert.AreEqual(1f, c.Last.PrevRadius, 0.001f);
        }

        [Test]
        public void AfterTheMapShrinks_NoFrameHoldsAScan_AndANewCaptureStartsClean()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            tracker.Step(Team, 1000, 2000, Interval, false, Speed, ScanMax);
            Assert.IsFalse(tracker.Step(Team, 1000, 2020, Interval, true, Speed, ScanMax).Holding);
            Assert.IsFalse(tracker.Step(-1, 1000, 2040, Interval, false, Speed, ScanMax).Holding);
            ScanFrame fresh = tracker.Step(Team, 3000, 3040, Interval, false, Speed, ScanMax);
            Assert.AreEqual(0f, fresh.PrevRadius, 0.0001f);
            Assert.AreEqual(2f, fresh.Radius, 0.001f);
        }

        [Test]
        public void AfterAReset_TheTrackerForgetsTheScan_AndTheNextFrameIsAFirstFrame()
        {
            // The client left the room (or the scan was switched off): the next frame starts clean, and a frame long after
            // the scan began is a joiner's, who is not handed every enemy behind the front.
            ScanBandTracker tracker = new ScanBandTracker();
            tracker.Step(Team, 1000, 1000, Interval, false, Speed, ScanMax);
            tracker.Step(Team, 1000, 1020, Interval, false, Speed, ScanMax);
            tracker.Reset();
            ScanFrame f = tracker.Step(Team, 1000, 4000, Interval, false, Speed, ScanMax);
            Assert.AreEqual(f.Radius, f.PrevRadius, 0.0001f);
        }

        [Test]
        public void TheWaveStopsTravellingPastTheFarthestPoint_ButTheLastBandIsStillSwept()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            tracker.Step(Team, 1000, 1000, Interval, false, Speed, ScanMax);
            tracker.Step(Team, 1000, 3300, Interval, false, Speed, ScanMax); // front at 115 m
            ScanFrame f = tracker.Step(Team, 1000, 3400, Interval, false, Speed, ScanMax); // front at 120 m, still inside
            Assert.IsTrue(f.Travelling);
            f = tracker.Step(Team, 1000, 3500, Interval, false, Speed, ScanMax); // front at 125 m
            Assert.IsFalse(f.Travelling);
            Assert.IsFalse(CentreScanRules.FrontSwept(f.PrevRadius, f.Radius, 120f), "120 m was caught the frame before");
            Assert.IsTrue(CentreScanRules.FrontSwept(f.PrevRadius, f.Radius, 122f));
        }

        // ---- which zones refresh ----

        private static readonly Vector2 Centre = new Vector2(100f, 100f);
        // zone 0 at 20 m (tier 1), zone 1 at 40 m (tier 2), zone 2 at 60 m (tier 3), zone 3 at 80 m (tier 4), zone 4 at 40 m (tier 1)
        private static readonly Vector2[] Positions =
        {
            new Vector2(120f, 100f), new Vector2(100f, 140f), new Vector2(40f, 100f), new Vector2(100f, 20f), new Vector2(60f, 100f)
        };
        private static readonly int[] Tiers = { 1, 2, 3, 4, 1 };

        private static List<int> Refresh(float prev, float radius, bool t1, bool t2, bool t3, bool t4)
        {
            List<int> result = new List<int> { 999 }; // stale entry: the rule must clear it
            CentreScanRules.ZonesToRefresh(prev, radius, Centre, Positions, Tiers, t1, t2, t3, t4, result);
            return result;
        }

        [Test]
        public void ZonesTheFrontSweptThisFrameAreRefreshed()
        {
            CollectionAssert.AreEquivalent(new[] { 1, 4 }, Refresh(30f, 50f, true, true, true, true));
        }

        [Test]
        public void ZonesOutsideTheBandAreNotRefreshed()
        {
            CollectionAssert.IsEmpty(Refresh(41f, 59f, true, true, true, true));
        }

        [Test]
        public void OnlyTickedTiersAreRefreshed()
        {
            CollectionAssert.AreEquivalent(new[] { 4 }, Refresh(30f, 50f, true, false, true, true));
            CollectionAssert.AreEquivalent(new[] { 1 }, Refresh(30f, 50f, false, true, true, true));
            CollectionAssert.IsEmpty(Refresh(30f, 50f, false, false, true, true));
        }

        [Test]
        public void EachZoneUsesItsOwnTier()
        {
            CollectionAssert.AreEquivalent(new[] { 3 }, Refresh(70f, 90f, false, false, false, true));
        }

        [Test]
        public void AZoneIsRefreshedOnceAsTheFrontPassesNotEveryFrame()
        {
            int times = 0;
            float prev = 0f;
            for (float radius = 2f; radius <= 100f; radius += 2f)
            {
                if (Refresh(prev, radius, true, true, true, true).Contains(2)) times++;
                prev = radius;
            }
            Assert.AreEqual(1, times);
        }
    }
}
