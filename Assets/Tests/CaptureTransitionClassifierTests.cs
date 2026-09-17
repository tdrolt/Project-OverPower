using NUnit.Framework;
using Overpower.Match;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    public class CaptureTransitionClassifierTests
    {
        [Test]
        public void IdleToCapturingIsStarted()
        {
            // Below CaptureTransitionClassifier.ResumeThreshold01 (0.01) - one frame's own fill at a
            // typical solo capture rate, not enough to read as a resume.
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle,
                new CaptureProgress(0, 0.002f, 1f / 15f, 1000), 1000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.Started, state);
            Assert.AreEqual(0, team);
            Assert.AreEqual(0.002f, progress, 1e-6f);
        }

        [Test]
        public void IdleToDrainingIsDrainStarted()
        {
            // A drain's Progress01 is the owner's REMAINING hold, so a fresh drain starts at 1.0, not
            // 0 - see HandleCapturedState's DrainRule.Step.Start case, which sets captureProgress =
            // CaptureSeconds when a drain starts. 1.0 is the realistic value the drain-start-label bug
            // (2026-09-17) hid.
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle,
                new CaptureProgress(1, 1.0f, -1f / 5f, 1000), 1000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.DrainStarted, state);
            Assert.AreEqual(1, team);
            Assert.AreEqual(1.0f, progress, 1e-6f);
        }

        [Test]
        public void StartingAlreadyAboveThresholdIsResumedNotStarted()
        {
            // A capture already 30% full when it starts moving again (a held state, or the T4
            // review's own "restart after the capturers left and came back") is a resume, not a
            // fresh start from zero.
            var heldThenMoving = new CaptureProgress(0, 0.30f, 1f / 15f, 2000);
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle, heldThenMoving,
                2000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.Resumed, state);
            Assert.AreEqual(0.30f, progress, 1e-6f);
        }

        [Test]
        public void StartingDrainAlreadyAboveThresholdIsDrainResumed()
        {
            var resumedDrain = new CaptureProgress(2, 0.40f, -1f / 5f, 3000);
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle, resumedDrain,
                3000, out _, out _);
            Assert.AreEqual(CaptureTransitionClassifier.DrainResumed, state);
        }

        [Test]
        public void StartingJustAboveZeroStaysStarted()
        {
            // One frame's worth of fill (well under the 1% threshold) must not misread as a resume.
            var barelyMoved = new CaptureProgress(0, 0.001f, 1f / 15f, 4000);
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle, barelyMoved,
                4000, out _, out _);
            Assert.AreEqual(CaptureTransitionClassifier.Started, state);
        }

        [Test]
        public void CapturingToIdleIsPausedWithProgressExtrapolatedToTheStopMoment()
        {
            // Started at 0.1 progress, rate 1/15 per second, and stopped 3 real seconds later - the
            // stop must log 0.1 + 3/15 = 0.3, not the 0.1 it started this segment at.
            var wasCapturing = new CaptureProgress(0, 0.1f, 1f / 15f, 1000);
            string state = CaptureTransitionClassifier.Classify(wasCapturing, CaptureProgress.Idle,
                4000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.Paused, state);
            Assert.AreEqual(0, team);
            Assert.AreEqual(0.1f + 3f / 15f, progress, 1e-4f);
        }

        [Test]
        public void DrainingToIdleIsDrainPaused()
        {
            var wasDraining = new CaptureProgress(1, 0.5f, -1f / 5f, 1000);
            string state = CaptureTransitionClassifier.Classify(wasDraining, CaptureProgress.Idle,
                3000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.DrainPaused, state);
            Assert.AreEqual(1, team);
            Assert.AreEqual(0.5f - 2f / 5f, progress, 1e-4f);
        }

        [Test]
        public void IdleToIdleIsNoEvent()
        {
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle, CaptureProgress.Idle,
                1000, out _, out _);
            Assert.IsNull(state);
        }

        [Test]
        public void ActiveToAHeldStateLogsPaused()
        {
            // The rate-0 hold the review asked to already be handled, now real: CaptureProgress.Held
            // (published since the capture ring change, 2026-09-17) keeps its team and progress (rate
            // 0, Team >= 0) instead of collapsing to CaptureProgress.Idle. Classify must treat "not
            // active" purely by RatePerSecond01 == 0, not by comparing against the Idle sentinel, so
            // this still reads as Paused with the real stop progress - not silently dropped, and not
            // misread as a fresh Idle->Idle no-event.
            var wasCapturing = new CaptureProgress(0, 0.2f, 1f / 15f, 1000);
            CaptureProgress heldState = CaptureProgress.Held(0, 0.2f + 2f / 15f, 3000);
            string state = CaptureTransitionClassifier.Classify(wasCapturing, heldState,
                3000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.Paused, state);
            Assert.AreEqual(0, team);
            Assert.AreEqual(0.2f + 2f / 15f, progress, 1e-4f);
        }

        [Test]
        public void ResumingOutOfAHeldStateIsResumed()
        {
            // The mirror case: leaving that same held (published since the capture ring change,
            // 2026-09-17) state (team kept, progress kept, rate 0) and moving again reads as Resumed,
            // exactly as leaving today's Idle-with-memory-lost would if the progress carried over
            // were above the threshold.
            CaptureProgress heldState = CaptureProgress.Held(0, 0.35f, 3000);
            var movingAgain = new CaptureProgress(0, 0.35f, 1f / 15f, 5000);
            string state = CaptureTransitionClassifier.Classify(heldState, movingAgain,
                5000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.Resumed, state);
            Assert.AreEqual(0, team);
            Assert.AreEqual(0.35f, progress, 1e-6f);
        }

        [Test]
        public void DrainingIntoAHeldDrainIsDrainPaused()
        {
            var wasDraining = new CaptureProgress(2, 0.8f, -0.2f, 1000);
            string state = CaptureTransitionClassifier.Classify(wasDraining, CaptureProgress.Held(2, 0.6f, 2000),
                2000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.DrainPaused, state);
            Assert.AreEqual(2, team);
            Assert.AreEqual(0.6f, progress, 1e-4f);
        }

        [Test]
        public void AHeldDrainMovingAgainIsDrainResumed()
        {
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Held(2, 0.6f, 2000),
                new CaptureProgress(2, 0.6f, -0.2f, 5000), 5000, out int team, out _);
            Assert.AreEqual(CaptureTransitionClassifier.DrainResumed, state);
            Assert.AreEqual(2, team);
        }

        [Test]
        public void AHoldEndingInIdleLogsNothing()
        {
            // Capturers left a held capture (it resets) or a defender stopped a held drain: the pause was already logged.
            Assert.IsNull(CaptureTransitionClassifier.Classify(CaptureProgress.Held(0, 0.4f, 3000), CaptureProgress.Idle,
                4000, out _, out _));
        }

        [Test]
        public void IdleIntoAHoldLogsNothing()
        {
            Assert.IsNull(CaptureTransitionClassifier.Classify(CaptureProgress.Idle, CaptureProgress.Held(0, 0.4f, 3000),
                3000, out _, out _));
        }

        [Test]
        public void AnotherTeamsFreshCaptureAfterAHoldIsStartedForThatTeam()
        {
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Held(0, 0.4f, 3000),
                new CaptureProgress(1, 0.001f, 1f / 15f, 4000), 4000, out int team, out _);
            Assert.AreEqual(CaptureTransitionClassifier.Started, state);
            Assert.AreEqual(1, team);
        }

        [Test]
        public void DrainOneFrameIntoItsHoldStillReadsDrainStarted()
        {
            // One frame's own drain off a full hold (comfortably above 1 - ResumeThreshold01) must
            // not misread as a resume, mirroring StartingJustAboveZeroStaysStarted for the capture side.
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle,
                new CaptureProgress(1, 0.995f, -1f / 5f, 1000), 1000, out _, out _);
            Assert.AreEqual(CaptureTransitionClassifier.DrainStarted, state);
        }

        [Test]
        public void ASlowMasterFramesFreshDrainIsStillDrainStarted()
        {
            // Review fix, 2026-09-17: a fresh drain's first publish is 1 - dt/DecaySeconds, not exactly
            // 1.0 - a master frame of >= 50ms at DecaySeconds 5 already lands below the plain 1%
            // static margin (1 - 0.05/5 = 0.99), so the old fixed ResumeThreshold01 misread this as a
            // resume. The rate-aware margin (ResumeThreshold01 vs |rate| * FirstFrameAllowanceSeconds)
            // must still call this a fresh start.
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle,
                new CaptureProgress(1, 0.985f, -1f / 5f, 1000), 1000, out int team, out _);
            Assert.AreEqual(CaptureTransitionClassifier.DrainStarted, state);
            Assert.AreEqual(1, team);
        }

        [Test]
        public void ASlowMasterFramesFreshCaptureIsStillStarted()
        {
            // Mirror on the capture side: Tier 3 (10s, the fastest tier) with 3 capturers has rate
            // 0.3/s - a single frame around 67ms already fills past the plain 1% static margin
            // (0.3 * 0.067 ~= 0.02), which the old fixed ResumeThreshold01 misread as a resume.
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle,
                new CaptureProgress(0, 0.02f, 0.3f, 1000), 1000, out int team, out _);
            Assert.AreEqual(CaptureTransitionClassifier.Started, state);
            Assert.AreEqual(0, team);
        }

        [Test]
        public void ARealDrainResumeJustPastTheAllowanceIsStillDrainResumed()
        {
            // A drain paused at a 0.9 remaining hold (well past what any single frame at this rate
            // could have contributed) resuming must still read as DrainResumed, not swallowed by a
            // margin sized generously for the fresh-start case above.
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Held(2, 0.9f, 2000),
                new CaptureProgress(2, 0.9f, -1f / 5f, 5000), 5000, out int team, out _);
            Assert.AreEqual(CaptureTransitionClassifier.DrainResumed, state);
            Assert.AreEqual(2, team);
        }

        [Test]
        public void ARealCaptureResumeJustPastTheAllowanceIsStillResumed()
        {
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Held(0, 0.1f, 2000),
                new CaptureProgress(0, 0.1f, 0.1f, 5000), 5000, out int team, out _);
            Assert.AreEqual(CaptureTransitionClassifier.Resumed, state);
            Assert.AreEqual(0, team);
        }
    }
}
