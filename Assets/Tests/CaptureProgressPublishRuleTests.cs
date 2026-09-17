using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class CaptureProgressPublishRuleTests
    {
        [Test]
        public void AMovingCaptureIsAnActiveCapture()
        {
            // 2 eligible players of 15s Tier 2 tower, 3 seconds in: progress 0.2, rate 2/15.
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, 15f, 5f, false,
                capturingID: 1, eligibleCount: 2, enemyPresent: false, mayCaptureNow: true, captureProgress: 3f, nowMs: 5000);
            Assert.AreEqual(1, p.Team);
            Assert.AreEqual(0.2f, p.Progress01, 1e-5f);
            Assert.AreEqual(2f / 15f, p.RatePerSecond01, 1e-5f);
            Assert.IsFalse(p.IsHeld);
        }

        [Test]
        public void AContestedCaptureIsHeld()
        {
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, 15f, 5f, false,
                capturingID: 1, eligibleCount: 1, enemyPresent: true, mayCaptureNow: true, captureProgress: 3f, nowMs: 5000);
            Assert.AreEqual(1, p.Team);
            Assert.AreEqual(0.2f, p.Progress01, 1e-5f);
            Assert.AreEqual(0f, p.RatePerSecond01);
            Assert.IsTrue(p.IsHeld);
        }

        [Test]
        public void ALinkBlockedCaptureIsHeld()
        {
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, 15f, 5f, false,
                capturingID: 1, eligibleCount: 1, enemyPresent: false, mayCaptureNow: false, captureProgress: 3f, nowMs: 5000);
            Assert.AreEqual(1, p.Team);
            Assert.AreEqual(0.2f, p.Progress01, 1e-5f);
            Assert.IsTrue(p.IsHeld);
        }

        [Test]
        public void CapturersGoneIsIdle()
        {
            // EndCaptureIfCapturersLeft has already reset capturingID to -1 (and captureProgress to 0) by the time
            // this runs; the guard below fires regardless of eligibleCount/captureProgress.
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, 15f, 5f, false,
                capturingID: -1, eligibleCount: 0, enemyPresent: false, mayCaptureNow: true, captureProgress: 0f, nowMs: 5000);
            Assert.AreEqual(CaptureProgress.Idle.Team, p.Team);
            Assert.AreEqual(0f, p.Progress01);
            Assert.AreEqual(0f, p.RatePerSecond01);
        }

        [Test]
        public void OnCooldownIsIdle()
        {
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, 15f, 5f, isOnCooldown: true,
                capturingID: 1, eligibleCount: 2, enemyPresent: false, mayCaptureNow: true, captureProgress: 3f, nowMs: 5000);
            Assert.AreEqual(CaptureProgress.Idle.Team, p.Team);
        }

        [Test]
        public void AMovingDrainIsAnActiveDrain()
        {
            // Owned zone, 12 of 15 one-player-seconds still banked (0.8 remaining hold), draining at -1/5.
            CaptureProgress p = CaptureProgressPublishRule.Decide(true, isDecaying: true, isDrainPaused: false,
                captureSeconds: 15f, decaySeconds: 5f, isOnCooldown: false, capturingID: 2, eligibleCount: 0,
                enemyPresent: false, mayCaptureNow: false, captureProgress: 12f, nowMs: 5000);
            Assert.AreEqual(2, p.Team);
            Assert.AreEqual(0.8f, p.Progress01, 1e-5f);
            Assert.AreEqual(-0.2f, p.RatePerSecond01, 1e-5f);
            Assert.IsFalse(p.IsHeld);
        }

        [Test]
        public void APausedDrainIsHeld()
        {
            CaptureProgress p = CaptureProgressPublishRule.Decide(true, isDecaying: true, isDrainPaused: true,
                captureSeconds: 15f, decaySeconds: 5f, isOnCooldown: false, capturingID: 0, eligibleCount: 0,
                enemyPresent: false, mayCaptureNow: false, captureProgress: 9f, nowMs: 5000);
            Assert.AreEqual(0, p.Team);
            Assert.AreEqual(0.6f, p.Progress01, 1e-5f);
            Assert.IsTrue(p.IsHeld);
        }

        [Test]
        public void ACapturedZoneNotDecayingIsIdle()
        {
            CaptureProgress p = CaptureProgressPublishRule.Decide(true, isDecaying: false, isDrainPaused: false,
                captureSeconds: 15f, decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 0,
                enemyPresent: false, mayCaptureNow: false, captureProgress: 15f, nowMs: 5000);
            Assert.AreEqual(CaptureProgress.Idle.Team, p.Team);
        }

        [Test]
        public void AZeroProgressContestedCaptureIsIdle()
        {
            // Nothing banked yet (captureProgress 0): even a contested hold collapses to Idle through
            // CaptureProgress.Held's own zero-progress rule, not a bespoke check in this rule.
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, 15f, 5f, false,
                capturingID: 1, eligibleCount: 1, enemyPresent: true, mayCaptureNow: true, captureProgress: 0f, nowMs: 5000);
            Assert.AreEqual(CaptureProgress.Idle.Team, p.Team);
            Assert.IsFalse(p.IsHeld);
        }

        [Test]
        public void ZeroCaptureSecondsIsAlwaysIdle()
        {
            // A misconfigured tier (Territory Config missing/zero) must never divide by zero.
            CaptureProgress p = CaptureProgressPublishRule.Decide(true, isDecaying: true, isDrainPaused: false,
                captureSeconds: 0f, decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 1,
                enemyPresent: false, mayCaptureNow: true, captureProgress: 3f, nowMs: 5000);
            Assert.AreEqual(CaptureProgress.Idle.Team, p.Team);
        }
    }
}
