using System.Collections.Generic;
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
            // capturingID already -1 (nobody has ever claimed this zone, or captureFadeSpeed's own fade already
            // reached 0 and reset it) - the isOnCooldown/capturingID guard fires regardless of
            // eligibleCount/captureProgress.
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

        // --------------------------------------------------------- captureFadeSpeed (2026-09-24)

        [Test]
        public void ANeutralZoneWithNobodyOfTheClaimingTeamFadesTowardZero()
        {
            // 10s tier, 4s banked (progress 0.4), capturing team gone (eligibleCount 0) - fades at the given rate
            // instead of holding, whether the zone is empty or only another team stands there (both read
            // eligibleCount 0 the same way - see BuildingCapture.ComputeCurrentProgress's own comment).
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, captureSeconds: 10f,
                decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 0, enemyPresent: false,
                mayCaptureNow: true, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: 2f);
            Assert.AreEqual(1, p.Team, "keeps the fading team's colour until it reaches 0");
            Assert.AreEqual(0.4f, p.Progress01, 1e-5f);
            Assert.AreEqual(-0.2f, p.RatePerSecond01, 1e-5f, "2 one-player-seconds/s over a 10s tier = -0.2/s of progress01");
            Assert.IsTrue(p.Fading);
            Assert.IsFalse(p.IsHeld, "moving, not frozen - IsHeld is rate 0 only");
        }

        [Test]
        public void ANeutralZoneOnlyAnEnemyPresentAlsoFades()
        {
            // Same as above but enemyPresent true (only another team stands there) - eligibleCount 0 wins over
            // enemyPresent, so this is a fade too, not the contested Held branch (that needs eligibleCount > 0).
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, captureSeconds: 10f,
                decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 0, enemyPresent: true,
                mayCaptureNow: true, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: 2f);
            Assert.AreEqual(1, p.Team);
            Assert.IsTrue(p.Fading);
            Assert.AreEqual(-0.2f, p.RatePerSecond01, 1e-5f);
        }

        [Test]
        public void ANeutralFadeWithNothingBankedIsIdle()
        {
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, captureSeconds: 10f,
                decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 0, enemyPresent: false,
                mayCaptureNow: true, captureProgress: 0f, nowMs: 5000, fadeRatePerSecond: 2f);
            Assert.AreEqual(CaptureProgress.Idle.Team, p.Team);
        }

        [Test]
        public void ANeutralFadeSpeedOfZeroHoldsInsteadOfMoving()
        {
            // captureFadeSpeed 0 = "it holds where it was" - the field's own tooltip. Published as an ordinary
            // Held (rate 0, blinking), not a moving-but-zero fade.
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, captureSeconds: 10f,
                decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 0, enemyPresent: false,
                mayCaptureNow: true, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: 0f);
            Assert.AreEqual(1, p.Team);
            Assert.AreEqual(0f, p.RatePerSecond01);
            Assert.IsTrue(p.IsHeld);
            Assert.IsFalse(p.Fading, "frozen, not fading - Held already means 'nothing here is going to move'");
        }

        [Test]
        public void AContestedCaptureStillHoldsNotFades()
        {
            // eligibleCount > 0 AND enemyPresent: the brief's "both teams inside: unchanged (holds)" - must not
            // regress into a fade just because fadeRatePerSecond is now always passed in.
            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, captureSeconds: 10f,
                decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 1, enemyPresent: true,
                mayCaptureNow: true, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: 2f);
            Assert.IsTrue(p.IsHeld);
            Assert.IsFalse(p.Fading);
        }

        [Test]
        public void AnOwnedZoneRefillsTowardFullOnceTheDrainStops()
        {
            // 10s tier, drain stopped (isDecaying false) at 4s banked (0.4) - refills toward full at the fade rate
            // instead of snapping (CaptureProgressPublishRuleTests' own ACapturedZoneNotDecayingIsIdle above covers
            // the already-full case, unchanged).
            CaptureProgress p = CaptureProgressPublishRule.Decide(true, isDecaying: false, isDrainPaused: false,
                captureSeconds: 10f, decaySeconds: 5f, isOnCooldown: false, capturingID: 2, eligibleCount: 0,
                enemyPresent: false, mayCaptureNow: false, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: 2f);
            Assert.AreEqual(2, p.Team, "the last drainer's team, same field HandleCapturedState already tracks");
            Assert.AreEqual(0.4f, p.Progress01, 1e-5f);
            Assert.AreEqual(0.2f, p.RatePerSecond01, 1e-5f);
            Assert.IsTrue(p.Fading);
        }

        [Test]
        public void AnOwnedRefillSpeedOfZeroHoldsInsteadOfMoving()
        {
            CaptureProgress p = CaptureProgressPublishRule.Decide(true, isDecaying: false, isDrainPaused: false,
                captureSeconds: 10f, decaySeconds: 5f, isOnCooldown: false, capturingID: 2, eligibleCount: 0,
                enemyPresent: false, mayCaptureNow: false, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: 0f);
            Assert.AreEqual(0f, p.RatePerSecond01);
            Assert.IsTrue(p.IsHeld);
            Assert.IsFalse(p.Fading);
        }

        [Test]
        public void FadeAndRefillRatesExtrapolateCorrectlyThroughEvaluate()
        {
            // The published-progress contract every remote client relies on: CaptureProgress.Evaluate at some
            // later timestamp must land exactly where the rate says it should, for both directions.
            CaptureProgress fade = CaptureProgressPublishRule.Decide(false, false, false, captureSeconds: 10f,
                decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 0, enemyPresent: false,
                mayCaptureNow: true, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: 2f);
            Assert.AreEqual(0.4f - 0.2f * 1.5f, fade.Evaluate(5000 + 1500), 1e-5f);

            CaptureProgress refill = CaptureProgressPublishRule.Decide(true, isDecaying: false, isDrainPaused: false,
                captureSeconds: 10f, decaySeconds: 5f, isOnCooldown: false, capturingID: 2, eligibleCount: 0,
                enemyPresent: false, mayCaptureNow: false, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: 2f);
            Assert.AreEqual(0.4f + 0.2f * 1.5f, refill.Evaluate(5000 + 1500), 1e-5f);
        }

        // --------------------------------------------------------- opus re-review, 2026-09-24 (item 4: "the
        // wiring wasn't tested - reverting line 542 passed every test"). These chain CaptureFadeRule.NeutralFadeRate
        // straight into Decide, the same way BuildingCapture.ComputeCurrentProgress now must (it used to pass the
        // plain FadeRatePerSecond instead), so a regression at that call site fails a test again.

        [Test]
        public void TwoPushersChainedThroughNeutralFadeRatePublishDoubleTheConfiguredRate()
        {
            float rate = CaptureFadeRule.NeutralFadeRate(fadeRate: 1f, claimTeam: 1,
                teamsInZone: new List<int> { 2, 2 }, perPlayerSpeed: 1f, pushersMayCapture: true);
            Assert.AreEqual(2f, rate, "two pushers beat the configured speed 1: max(1, 2x1)");

            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, captureSeconds: 10f,
                decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 0, enemyPresent: true,
                mayCaptureNow: true, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: rate);
            Assert.AreEqual(1, p.Team);
            Assert.AreEqual(-0.2f, p.RatePerSecond01, 1e-5f, "2 one-player-seconds/s over a 10s tier = -0.2/s of progress01");
            Assert.IsTrue(p.Fading);
        }

        [Test]
        public void OnePusherAtFadeSpeedZeroChainedThroughNeutralFadeRatePublishesFadingNotHeld()
        {
            // The exact bug this round fixes: at fade speed 0 with a lone pusher, publishing the plain (unboosted)
            // FadeRatePerSecond gave a frozen Held band for the whole push-down instead of a moving Fading one.
            float rate = CaptureFadeRule.NeutralFadeRate(fadeRate: 0f, claimTeam: 1,
                teamsInZone: new List<int> { 2 }, perPlayerSpeed: 1f, pushersMayCapture: true);
            Assert.AreEqual(1f, rate);

            CaptureProgress p = CaptureProgressPublishRule.Decide(false, false, false, captureSeconds: 10f,
                decaySeconds: 5f, isOnCooldown: false, capturingID: 1, eligibleCount: 0, enemyPresent: true,
                mayCaptureNow: true, captureProgress: 4f, nowMs: 5000, fadeRatePerSecond: rate);
            Assert.IsTrue(p.Fading);
            Assert.IsFalse(p.IsHeld);
            Assert.AreEqual(-0.1f, p.RatePerSecond01, 1e-5f);
        }
    }
}
