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
