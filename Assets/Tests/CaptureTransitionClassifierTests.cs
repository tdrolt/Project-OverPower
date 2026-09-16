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
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Idle,
                new CaptureProgress(1, 0.0f, -1f / 5f, 1000), 1000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.DrainStarted, state);
            Assert.AreEqual(1, team);
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
        public void ActiveToARateZeroHoldStillLogsPausedNotSilence()
        {
            // The "future rate-0 hold" the review asked to already be handled: a capture-ring task
            // may later publish a paused state that keeps its team and progress (rate 0, Team >= 0)
            // instead of collapsing to CaptureProgress.Idle. Classify must treat "not active" purely
            // by RatePerSecond01 == 0, not by comparing against the Idle sentinel, so this still
            // reads as Paused with the real stop progress - not silently dropped, and not misread as
            // a fresh Idle->Idle no-event.
            var wasCapturing = new CaptureProgress(0, 0.2f, 1f / 15f, 1000);
            var futureHeldState = new CaptureProgress(0, 0.2f + 2f / 15f, 0f, 3000);
            string state = CaptureTransitionClassifier.Classify(wasCapturing, futureHeldState,
                3000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.Paused, state);
            Assert.AreEqual(0, team);
            Assert.AreEqual(0.2f + 2f / 15f, progress, 1e-4f);
        }

        [Test]
        public void ResumingOutOfAFutureHeldStateIsResumedNotStarted()
        {
            // The mirror case: leaving that same future held state (team kept, progress kept, rate
            // 0) and moving again reads as Resumed, exactly as leaving today's Idle-with-memory-lost
            // would if the progress carried over were above the threshold.
            var heldState = new CaptureProgress(0, 0.35f, 0f, 3000);
            var movingAgain = new CaptureProgress(0, 0.35f, 1f / 15f, 5000);
            string state = CaptureTransitionClassifier.Classify(heldState, movingAgain,
                5000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.Resumed, state);
            Assert.AreEqual(0, team);
            Assert.AreEqual(0.35f, progress, 1e-6f);
        }
    }
}
