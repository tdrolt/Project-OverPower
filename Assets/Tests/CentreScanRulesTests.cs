using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Vision;

namespace Overpower.Tests
{
    /// <summary>
    /// Vision Task 10 (Tudor 2026-09-30): a sonar wave goes out fast, through walls; an enemy is caught each time the front
    /// passes over where they are now. Vision Task 16 (Tudor 2026-10-01): the waves are on a fixed clock (every interval from
    /// go-live, or on server-clock multiples in the warm-up), whoever holds the centre; the team holding it when a wave
    /// starts owns that wave, nobody if it is neutral then. Every number here is the test's own; none is the tuned value.
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

        // ---- when a scan happens (the fixed clock) ----

        private const int LiveAt = 1000;

        [Test]
        public void LiveWaves_TheFirstIsOneIntervalAfterGoLive()
        {
            Assert.IsNull(CentreScanRules.ScanStart(LiveAt, LiveAt, Interval));
            Assert.IsNull(CentreScanRules.ScanStart(LiveAt, LiveAt + Interval - 1, Interval));
            Assert.AreEqual(LiveAt + Interval, CentreScanRules.ScanStart(LiveAt, LiveAt + Interval, Interval));
        }

        [Test]
        public void LiveWaves_AWaveKeepsItsStartUntilAFullIntervalHasPassed()
        {
            Assert.AreEqual(LiveAt + Interval, CentreScanRules.ScanStart(LiveAt, LiveAt + 2 * Interval - 1, Interval));
            Assert.AreEqual(LiveAt + 2 * Interval, CentreScanRules.ScanStart(LiveAt, LiveAt + 2 * Interval, Interval));
        }

        [Test]
        public void LiveWaves_TheLatestIsReturnedManyIntervalsLater()
        {
            Assert.AreEqual(LiveAt + 3 * Interval, CentreScanRules.ScanStart(LiveAt, LiveAt + 3 * Interval + 7, Interval));
        }

        [Test]
        public void LiveWaves_AClockReadingBeforeGoLiveHasNoWave()
        {
            Assert.IsNull(CentreScanRules.ScanStart(LiveAt, LiveAt - 10, Interval));
        }

        [Test]
        public void WarmUpWaves_AreOnWholeMultiplesOfTheIntervalOnTheServerClock()
        {
            Assert.AreEqual(3 * Interval, CentreScanRules.ScanStart(null, 3 * Interval + 7, Interval));
            Assert.AreEqual(3 * Interval, CentreScanRules.ScanStart(null, 3 * Interval, Interval));
            Assert.AreEqual(2 * Interval, CentreScanRules.ScanStart(null, 3 * Interval - 1, Interval));
        }

        [Test]
        public void WarmUpWaves_StillLineUpWhenTheServerClockReadsNegative()
        {
            // Photon's 32-bit clock reads as a negative int after 2^31 ms; every client must still agree on the same multiple.
            // Hand-worked: now = -1 reads as 4294967295; minus its remainder 7295 on a 10000 ms interval is 4294960000.
            Assert.AreEqual(unchecked((int)4294960000u), CentreScanRules.ScanStart(null, -1, Interval));
        }

        [Test]
        public void LiveWaves_TheServerClockWrappingAroundDoesNotBreakTheSchedule()
        {
            int live = int.MaxValue - 4000; // the match went live just before the 32-bit server clock wraps
            int now = unchecked(live + Interval + 500);
            Assert.Less(now, 0, "the test needs a clock that has wrapped");
            Assert.AreEqual(unchecked(live + Interval), CentreScanRules.ScanStart(live, now, Interval));
            Assert.AreEqual(unchecked(live + 2 * Interval), CentreScanRules.NextScanStart(live, now, Interval));
        }

        [Test]
        public void NoWaveWithAnIntervalOfZeroOrLess()
        {
            Assert.IsNull(CentreScanRules.ScanStart(null, 5000, 0));
            Assert.IsNull(CentreScanRules.ScanStart(LiveAt, 99999, -5));
        }

        [Test]
        public void TheNextWave_LiveBeforeTheFirstIsOneIntervalAfterGoLive()
        {
            Assert.AreEqual(LiveAt + Interval, CentreScanRules.NextScanStart(LiveAt, LiveAt + 5, Interval));
            Assert.AreEqual(LiveAt + Interval, CentreScanRules.NextScanStart(LiveAt, LiveAt - 10, Interval));
        }

        [Test]
        public void TheNextWave_LiveAfterAWaveIsOneIntervalLater()
        {
            Assert.AreEqual(LiveAt + 2 * Interval, CentreScanRules.NextScanStart(LiveAt, LiveAt + Interval + 1, Interval));
            Assert.AreEqual(LiveAt + 2 * Interval, CentreScanRules.NextScanStart(LiveAt, LiveAt + 2 * Interval - 1, Interval));
            Assert.AreEqual(LiveAt + 3 * Interval, CentreScanRules.NextScanStart(LiveAt, LiveAt + 2 * Interval, Interval));
        }

        [Test]
        public void TheNextWave_WarmUpIsTheNextMultiple()
        {
            Assert.AreEqual(4 * Interval, CentreScanRules.NextScanStart(null, 3 * Interval + 7, Interval));
            Assert.AreEqual(4 * Interval, CentreScanRules.NextScanStart(null, 3 * Interval, Interval));
            Assert.AreEqual(3 * Interval, CentreScanRules.NextScanStart(null, 3 * Interval - 1, Interval));
        }

        [Test]
        public void TheCountdownShowsWholeSecondsRoundedUp()
        {
            Assert.AreEqual(10, CentreScanRules.CountdownSecondsShown(10000));
            Assert.AreEqual(10, CentreScanRules.CountdownSecondsShown(9001));
            Assert.AreEqual(9, CentreScanRules.CountdownSecondsShown(9000));
            Assert.AreEqual(1, CentreScanRules.CountdownSecondsShown(1));
            Assert.AreEqual(0, CentreScanRules.CountdownSecondsShown(0));
            Assert.AreEqual(0, CentreScanRules.CountdownSecondsShown(-40));
        }

        [Test]
        public void TheCountdownResetsAtEachWave()
        {
            int justBefore = 3 * Interval - 1;
            int atWave = 3 * Interval;
            Assert.AreEqual(1, CentreScanRules.CountdownSecondsShown(unchecked(CentreScanRules.NextScanStart(null, justBefore, Interval) - justBefore)));
            Assert.AreEqual(10, CentreScanRules.CountdownSecondsShown(unchecked(CentreScanRules.NextScanStart(null, atWave, Interval) - atWave)));
        }

        [Test]
        public void ActiveScan_IsTheClocksWave_NeverDependingOnAHolder()
        {
            Assert.IsTrue(CentreScanRules.ActiveScan(LiveAt, LiveAt + Interval + 5, Interval, false, out int start));
            Assert.AreEqual(LiveAt + Interval, start);
            Assert.IsTrue(CentreScanRules.ActiveScan(null, 3 * Interval + 5, Interval, false, out start));
            Assert.AreEqual(3 * Interval, start);
            Assert.IsFalse(CentreScanRules.ActiveScan(LiveAt, LiveAt + 5, Interval, false, out _), "live, before the first wave");
        }

        [Test]
        public void NoScanOnceTheMapHasShrunk()
        {
            Assert.IsFalse(CentreScanRules.ActiveScan(LiveAt, LiveAt + 3 * Interval, Interval, true, out _));
            Assert.IsFalse(CentreScanRules.ActiveScan(null, 3 * Interval, Interval, true, out _));
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
            public int Owner = Team;          // who owns the centre right now
            public int? LiveAtMs = 1000 - Interval; // waves at 1000, 1000 + Interval, ...
            public bool Cut;

            public void Step(int now, float distance)
            {
                Last = tracker.Step(LiveAtMs, Owner, now, Interval, Cut, Speed, ScanMax);
                if (Last.Active && CentreScanRules.FrontSwept(Last.PrevRadius, Last.Radius, distance)) Caught++;
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
        public void ACaptureMidWave_DoesNotRestartTheBand_AndTheWaveKeepsItsHolder()
        {
            TrackedCatcher c = new TrackedCatcher { Owner = 5 };
            for (int t = 1000; t < 4000; t += Frame) c.Step(t, 30f);
            Assert.AreEqual(1, c.Caught);
            c.Owner = 7; // captured 3 s into the wave
            c.Step(4000, 30f);
            Assert.AreEqual(5, c.Last.HolderTeam, "that wave still belongs to the team that held the centre when it began");
            Assert.AreEqual(Speed * 2.98f, c.Last.PrevRadius, 0.01f, "the band carries on, it does not start again from 0");
            Assert.AreEqual(1, c.Caught);
        }

        [Test]
        public void AHalfSecondFrame_StillCatchesAPlayerOnce()
        {
            TrackedCatcher c = new TrackedCatcher();
            for (int t = 1000; t <= 1000 + Interval; t += 500) c.Step(t, 30f);
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
            Assert.IsFalse(c.Last.Active);
            c.Step(1040, 0.5f);
            Assert.AreEqual(1, c.Caught, "the frame after the zero continues from 1020, not from 0");
            Assert.AreEqual(1f, c.Last.PrevRadius, 0.001f);
        }

        [Test]
        public void AfterTheMapShrinks_NoFrameIsActive_AndTheNextWaveStartsClean()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            tracker.Step(1000 - Interval, Team, 2000, Interval, false, Speed, ScanMax);
            Assert.IsFalse(tracker.Step(1000 - Interval, Team, 2020, Interval, true, Speed, ScanMax).Active);
            Assert.IsFalse(tracker.Step(1000 - Interval, -1, 2040, Interval, true, Speed, ScanMax).Active);
            ScanFrame fresh = tracker.Step(1000 - Interval, Team, 1000 + Interval + 40, Interval, false, Speed, ScanMax);
            Assert.IsTrue(fresh.Active);
            Assert.AreEqual(0f, fresh.PrevRadius, 0.0001f);
            Assert.AreEqual(2f, fresh.Radius, 0.001f);
        }

        [Test]
        public void AfterAReset_TheTrackerForgetsTheScan_AndTheNextFrameIsAFirstFrame()
        {
            // The client left the room (or the scan was switched off): the next frame starts clean, and a frame long after
            // the scan began is a joiner's, who is not handed every enemy behind the front.
            ScanBandTracker tracker = new ScanBandTracker();
            int live = 1000 - Interval;
            tracker.Step(live, Team, 1000, Interval, false, Speed, ScanMax);
            tracker.Step(live, Team, 1020, Interval, false, Speed, ScanMax);
            tracker.Reset();
            ScanFrame f = tracker.Step(live, Team, 4000, Interval, false, Speed, ScanMax);
            Assert.AreEqual(f.Radius, f.PrevRadius, 0.0001f);
        }

        [Test]
        public void ATierThreeZoneIsRefreshedOnceInEveryScan_WithUnevenFrames_WhenTierThreeIsTicked()
        {
            // Playtest log question (Task 11 review, item 6): a tier-3 zone 60 m out must be refreshed in scan 1, 2 and 3 alike.
            ScanBandTracker tracker = new ScanBandTracker();
            int[] refreshedInScan = new int[3];
            int now = 1000;
            int step = 0;
            while (now < 1000 + 3 * Interval)
            {
                now += 7 + (step++ * 13) % 40; // 7..46 ms, uneven like real frames
                ScanFrame f = tracker.Step(1000 - Interval, Team, now, Interval, false, Speed, ScanMax);
                if (!f.Active) continue;
                List<int> zones = new List<int>();
                CentreScanRules.ZonesToRefresh(f.PrevRadius, f.Radius, Centre, Positions, Tiers, true, true, true, true, zones);
                int scan = Mathf.Min(2, (now - 1000) / Interval);
                if (zones.Contains(2)) refreshedInScan[scan]++;
            }
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, refreshedInScan);
        }

        [Test]
        public void AWaveSeenLateAfterAFrameWithNoWave_StillStartsFromZero()
        {
            // Live, before the first wave: no scan. A hitch then delays the first frame past the wave's start by 800 ms. The
            // client saw no scan the frame before, so this is not a mid-wave join and the band starts from 0.
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.IsFalse(tracker.Step(0, -1, Interval - 20, Interval, false, Speed, ScanMax).Active);
            ScanFrame f = tracker.Step(0, -1, Interval + 800, Interval, false, Speed, ScanMax);
            ScanFrame next = tracker.Step(0, -1, Interval + 820, Interval, false, Speed, ScanMax);
            Assert.IsTrue(f.Active);
            Assert.AreEqual(0f, f.PrevRadius, 0.0001f, "the first frame of the wave starts from 0, however late");
            Assert.IsTrue(CentreScanRules.FrontSwept(f.PrevRadius, f.Radius, 20f), "an enemy 20 m out is caught");
            Assert.Greater(next.PrevRadius, 0f);
        }

        [Test]
        public void AFirstFrameAfterAResetWithin300ms_IsCaught_AndOneMsLaterIsNot()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            ScanFrame inside = tracker.Step(1000 - Interval, Team, 1300, Interval, false, Speed, ScanMax);
            Assert.AreEqual(0f, inside.PrevRadius, 0.0001f);
            tracker.Reset();
            ScanFrame outside = tracker.Step(1000 - Interval, Team, 1301, Interval, false, Speed, ScanMax);
            Assert.AreEqual(outside.Radius, outside.PrevRadius, 0.0001f);
        }

        [Test]
        public void TheWaveStopsTravellingPastTheFarthestPoint_ButTheLastBandIsStillSwept()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            int live = 1000 - Interval;
            tracker.Step(live, Team, 1000, Interval, false, Speed, ScanMax);
            tracker.Step(live, Team, 3300, Interval, false, Speed, ScanMax); // front at 115 m
            ScanFrame f = tracker.Step(live, Team, 3400, Interval, false, Speed, ScanMax); // front at 120 m, still inside
            Assert.IsTrue(f.Travelling);
            f = tracker.Step(live, Team, 3500, Interval, false, Speed, ScanMax); // front at 125 m
            Assert.IsFalse(f.Travelling);
            Assert.IsFalse(CentreScanRules.FrontSwept(f.PrevRadius, f.Radius, 120f), "120 m was caught the frame before");
            Assert.IsTrue(CentreScanRules.FrontSwept(f.PrevRadius, f.Radius, 122f));
        }

        // ---- who the wave belongs to (Task 16) ----

        private static ScanFrame At(ScanBandTracker tracker, int owner, int now) =>
            tracker.Step(1000 - Interval, owner, now, Interval, false, Speed, ScanMax);

        [Test]
        public void ANeutralCentreAtTheStart_MeansNobodyOwnsThatWave_ButItStillRollsOut()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            ScanFrame f = At(tracker, -1, 1000);
            Assert.IsTrue(f.Active);
            Assert.AreEqual(-1, f.HolderTeam);
            f = At(tracker, -1, 1500);
            Assert.IsTrue(f.Travelling);
            Assert.AreEqual(-1, f.HolderTeam);
        }

        [Test]
        public void ACaptureMidWave_DoesNotGiveThatWaveToTheCapturer_ButTheNextWaveIsTheirs()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.AreEqual(-1, At(tracker, -1, 1000).HolderTeam);
            Assert.AreEqual(-1, At(tracker, 4, 2000).HolderTeam, "captured one second into a neutral wave");
            Assert.AreEqual(-1, At(tracker, 4, 1000 + Interval - 20).HolderTeam);
            Assert.AreEqual(4, At(tracker, 4, 1000 + Interval).HolderTeam, "the next wave starts with team 4 holding");
        }

        [Test]
        public void ALossMidWave_KeepsThatWaveWithTheTeamThatHeldItAtTheStart()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.AreEqual(2, At(tracker, 2, 1000).HolderTeam);
            Assert.AreEqual(2, At(tracker, 6, 2500).HolderTeam, "team 6 took it mid-wave");
            Assert.AreEqual(2, At(tracker, -1, 3000).HolderTeam, "even if it went neutral");
            Assert.AreEqual(6, At(tracker, 6, 1000 + Interval).HolderTeam);
        }

        [Test]
        public void ANeutralStartAfterAHeldWave_HasNoHolder()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.AreEqual(2, At(tracker, 2, 1000).HolderTeam);
            Assert.AreEqual(-1, At(tracker, -1, 1000 + Interval).HolderTeam);
        }

        [Test]
        public void SomeoneJoiningMidWave_UsesTheOwnerTheyReadThen()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            ScanFrame f = At(tracker, 3, 1000 + 4000);
            Assert.IsTrue(f.Active);
            Assert.AreEqual(3, f.HolderTeam);
            Assert.AreEqual(3, At(tracker, 9, 1000 + 4020).HolderTeam, "and then keeps it for the rest of that wave");
        }

        [Test]
        public void AfterACut_ThereIsNoWaveAndNoHolder()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.AreEqual(2, At(tracker, 2, 1000).HolderTeam);
            ScanFrame cut = tracker.Step(1000 - Interval, 2, 1000 + Interval, Interval, true, Speed, ScanMax);
            Assert.IsFalse(cut.Active);
            Assert.AreEqual(-1, cut.HolderTeam);
        }

        [Test]
        public void TheHolderIsDecidedAcrossAClockWrap()
        {
            int live = int.MaxValue - 4000;
            ScanBandTracker tracker = new ScanBandTracker();
            int start = unchecked(live + Interval);
            Assert.Less(start, 0);
            Assert.IsFalse(tracker.Step(live, 5, unchecked(start - 20), Interval, false, Speed, ScanMax).Active);
            Assert.AreEqual(5, tracker.Step(live, 5, start, Interval, false, Speed, ScanMax).HolderTeam);
            Assert.AreEqual(5, tracker.Step(live, 8, unchecked(start + 1000), Interval, false, Speed, ScanMax).HolderTeam);
            Assert.AreEqual(8, tracker.Step(live, 8, unchecked(start + Interval), Interval, false, Speed, ScanMax).HolderTeam);
        }

        [Test]
        public void WarmUpWaves_AlsoHaveTheirHolderDecidedAtTheStart()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            ScanFrame f = tracker.Step(null, 2, 3 * Interval + 20, Interval, false, Speed, ScanMax);
            Assert.AreEqual(3 * Interval, f.ScanStartMs);
            Assert.AreEqual(2, f.HolderTeam);
            Assert.AreEqual(2, tracker.Step(null, 4, 3 * Interval + 2000, Interval, false, Speed, ScanMax).HolderTeam);
            Assert.AreEqual(4, tracker.Step(null, 4, 4 * Interval, Interval, false, Speed, ScanMax).HolderTeam);
        }

        // ---- every client agrees on a wave's holder (Task 16 review) ----

        private static ScanFrame Held(ScanBandTracker tracker, int owner, int heldSince, int now) =>
            tracker.Step(1000 - Interval, owner, now, Interval, false, Speed, ScanMax, heldSince);

        [Test]
        public void ACaptureStampedBeforeTheStart_ButReadAfterIt_GivesTheWaveToTheCapturer_AndRestartsTheBand()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.AreEqual(2, Held(tracker, 2, 0, 1000).HolderTeam);
            Held(tracker, 2, 0, 1040);
            Assert.Greater(Held(tracker, 2, 0, 1080).PrevRadius, 0f, "the band was running");
            ScanFrame late = Held(tracker, 4, 950, 1100); // team 4 took it at 950 ms; the news only arrived now
            Assert.AreEqual(4, late.HolderTeam);
            Assert.AreEqual(0f, late.PrevRadius, "the band restarts from the centre so the first metres are not lost");
            Assert.Greater(late.Radius, 0f);
            ScanFrame after = Held(tracker, 4, 950, 1120);
            Assert.AreEqual(4, after.HolderTeam);
            Assert.AreEqual(late.Radius, after.PrevRadius, 0.001f, "the correction is applied once");
        }

        [Test]
        public void ACaptureStampedAfterTheStart_LeavesTheWaveWithTheOldHolder()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.AreEqual(2, Held(tracker, 2, 0, 1000).HolderTeam);
            Held(tracker, 2, 0, 1040);
            ScanFrame f = Held(tracker, 4, 1050, 1100);
            Assert.AreEqual(2, f.HolderTeam);
            Assert.Greater(f.PrevRadius, 0f, "no restart");
        }

        [Test]
        public void ACaptureStampedAfterTheStart_ReadOnTheWavesFirstFrame_StillLeavesItWithTheOldHolder()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.IsFalse(Held(tracker, 2, 0, 990).Active, "the last frame before the wave: team 2 holds it");
            ScanFrame f = Held(tracker, 4, 1030, 1040);
            Assert.AreEqual(2, f.HolderTeam);
        }

        [Test]
        public void SomeoneJoiningAfterAMidWaveCapture_HasNoHolderForThatWave_ButTheNextIsTheirs()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            ScanFrame f = Held(tracker, 4, 1050, 3000);
            Assert.IsTrue(f.Active);
            Assert.AreEqual(-1, f.HolderTeam);
            Assert.AreEqual(-1, Held(tracker, 4, 1050, 3020).HolderTeam);
            Assert.AreEqual(4, Held(tracker, 4, 1050, 1000 + Interval).HolderTeam);
        }

        [Test]
        public void SomeoneJoiningMidWave_WithNoCaptureSinceTheStart_ReadsTheOwner()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.AreEqual(3, Held(tracker, 3, -50000, 3000).HolderTeam);
        }

        [Test]
        public void ACorrectionOneSecondIntoTheWave_CatchesNobodyTheFrontAlreadyPassed()
        {
            // Late capture news after a lag spike: the front is already 50 m out, so an enemy 10 m from the centre was passed
            // long ago and must not be caught by the corrected holder.
            ScanBandTracker tracker = new ScanBandTracker();
            Held(tracker, 2, 0, 1000);
            Held(tracker, 2, 0, 1040);
            ScanFrame late = Held(tracker, 4, 950, 2000);
            Assert.AreEqual(4, late.HolderTeam);
            Assert.AreEqual(late.Radius, late.PrevRadius, 0.0001f, "an empty band: nobody behind the front is handed over");
            Assert.IsFalse(CentreScanRules.FrontSwept(late.PrevRadius, late.Radius, 10f), "10 m out was passed long ago");
            Assert.IsFalse(CentreScanRules.FrontSwept(late.PrevRadius, late.Radius, 30f), "so was 30 m");
            ScanFrame next = Held(tracker, 4, 950, 2020);
            Assert.IsTrue(CentreScanRules.FrontSwept(next.PrevRadius, next.Radius, 51f), "the band carries on from the front the next frame");
        }

        [Test]
        public void ACorrectionWithinTheLateGrace_StillRestartsTheBandFromTheCentre()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Held(tracker, 2, 0, 1000);
            Held(tracker, 2, 0, 1040);
            ScanFrame late = Held(tracker, 4, 950, 1000 + ScanBandTracker.LateStampGraceMs);
            Assert.AreEqual(4, late.HolderTeam);
            Assert.AreEqual(0f, late.PrevRadius);
        }

        [Test]
        public void AfterAReset_ThePreviousRoomsOwnerIsForgotten_SoAJoinerAfterAMidWaveCaptureHasNoHolder()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.IsFalse(Held(tracker, 2, 0, 990).Active, "team 2 held it in the old room");
            tracker.Reset();
            Assert.AreEqual(-1, Held(tracker, 4, 1050, 3000).HolderTeam);
        }

        [Test]
        public void ANeutralStampedBeforeTheStart_ButReadAfterIt_CorrectsTheHolderToNobody()
        {
            ScanBandTracker tracker = new ScanBandTracker();
            Assert.AreEqual(2, Held(tracker, 2, 0, 1000).HolderTeam);
            Held(tracker, 2, 0, 1040);
            Assert.AreEqual(-1, Held(tracker, -1, 950, 1100).HolderTeam);
        }

        [Test]
        public void TheStampComparisonIsWrapSafe_WhenTheServerClockReadsNegative()
        {
            int live = int.MaxValue - 4000;
            int start = unchecked(live + Interval);
            Assert.Less(start, 0);
            int stampBefore = unchecked(start - 10000); // wraps to a large positive number, but is 10 s earlier
            ScanBandTracker joiner = new ScanBandTracker();
            Assert.AreEqual(5, joiner.Step(live, 5, unchecked(start + 1000), Interval, false, Speed, ScanMax, stampBefore).HolderTeam,
                            "held since before the start, so the wave is theirs");
            ScanBandTracker running = new ScanBandTracker();
            running.Step(live, 5, start, Interval, false, Speed, ScanMax, stampBefore);
            Assert.AreEqual(5, running.Step(live, 8, unchecked(start + 1000), Interval, false, Speed, ScanMax, unchecked(start + 500)).HolderTeam,
                            "a capture stamped after the start leaves the wave with the old holder");
        }

        // ---- the schedule follows mLiveAt alone (Task 16 review) ----

        [Test]
        public void AFrameBeforeMLiveAtArrives_IsOnTheWarmUpClock_AndOneAfterIsOnTheLiveClock()
        {
            int liveAt = 7 * Interval + 5000; // announced 5 s ahead
            int now = 7 * Interval - 100;
            Assert.IsNull(CentreScanRules.LiveSchedule(0), "no mLiveAt yet: warm-up");
            Assert.AreEqual(6 * Interval, CentreScanRules.ScanStart(CentreScanRules.LiveSchedule(0), now, Interval));
            Assert.AreEqual(liveAt, CentreScanRules.LiveSchedule(liveAt));
            // From the countdown start on there is no wave, and the label counts straight to the first live wave.
            Assert.IsNull(CentreScanRules.ScanStart(CentreScanRules.LiveSchedule(liveAt), 7 * Interval, Interval));
            Assert.IsNull(CentreScanRules.ScanStart(CentreScanRules.LiveSchedule(liveAt), liveAt - 1, Interval));
            Assert.AreEqual(liveAt + Interval, CentreScanRules.NextScanStart(CentreScanRules.LiveSchedule(liveAt), 7 * Interval, Interval));
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
