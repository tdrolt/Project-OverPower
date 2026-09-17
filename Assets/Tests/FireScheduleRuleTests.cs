using NUnit.Framework;
using Overpower.Weapons;

namespace Overpower.Tests
{
    public class FireScheduleRuleTests
    {
        [Test]
        public void HeldCadenceCarriesOverByExactlyOneInterval()
        {
            // Previous deadline still within one interval of now (the trigger has been held
            // continuously) - the new deadline is the old one plus exactly one interval, not
            // re-based off now.
            float next = FireScheduleRule.NextFireTime(previousNextFireTime: 1f, now: 1f, interval: 0.5f,
                                                        triggerHeldContinuously: true, blockedThisTick: false);
            Assert.AreEqual(1.5f, next, 1e-5f);
        }

        [Test]
        public void AReleasedTriggerRebasesOffNowRegardlessOfThePreviousDeadline()
        {
            float next = FireScheduleRule.NextFireTime(previousNextFireTime: 1f, now: 1f, interval: 0.5f,
                                                        triggerHeldContinuously: false, blockedThisTick: false);
            Assert.AreEqual(1.5f, next, 1e-5f);

            // A previous deadline far in the past must not matter here the way it would for a
            // held cadence - this always just rebases.
            float next2 = FireScheduleRule.NextFireTime(previousNextFireTime: -100f, now: 1f, interval: 0.5f,
                                                         triggerHeldContinuously: false, blockedThisTick: false);
            Assert.AreEqual(1.5f, next2, 1e-5f);
        }

        [Test]
        public void ChargeWeaponsASingleReleaseAlwaysRebasesEvenWhenTheNumbersWouldOtherwiseCarryOver()
        {
            // The exact condition that WOULD carry over for a held weapon (previous within one
            // interval of now) - a charge weapon's one-off release (triggerHeldContinuously=false)
            // must still simply rebase.
            float next = FireScheduleRule.NextFireTime(previousNextFireTime: 0.9f, now: 1f, interval: 1f,
                                                        triggerHeldContinuously: false, blockedThisTick: false);
            Assert.AreEqual(2f, next, 1e-5f);
        }

        [Test]
        public void BlockedThisTickAdvancesAStaleDeadlineUpToNow()
        {
            float next = FireScheduleRule.NextFireTime(previousNextFireTime: 1f, now: 5f, interval: 1f,
                                                        triggerHeldContinuously: false, blockedThisTick: true);
            Assert.AreEqual(5f, next, 1e-5f);
        }

        [Test]
        public void BlockedThisTickNeverPullsTheDeadlineBackwards()
        {
            // now sits BEHIND the existing deadline (e.g. blocked the same tick a shot's own
            // ordinary cooldown already schedules further out) - blocking must not pull the
            // deadline earlier than a real cooldown already has it.
            float next = FireScheduleRule.NextFireTime(previousNextFireTime: 5f, now: 1f, interval: 1f,
                                                        triggerHeldContinuously: false, blockedThisTick: true);
            Assert.AreEqual(5f, next, 1e-5f);
        }

        [Test]
        public void DocumentsTheBug546ad44WhereANonChargeWeaponsFreshClickWronglyReadsAsHeldContinuously()
        {
            // Tudor: "the laser was firing way too fast". A beam fires at t=0 with interval 0.5,
            // leaving the deadline at 0.5. The player releases and re-presses at t=0.9 - short of a
            // full interval after that deadline. WeaponFiring.TryFire (546ad44) computed
            // triggerHeldContinuously as simply "!weapon.CanCharge", true for every non-charge
            // weapon regardless of whether the trigger was actually held on the previous frame - so
            // a brand new click read exactly the same as a genuine continuing hold, reproduced
            // literally here. That carries the STALE 0.5 deadline forward (0.5 + 0.5 = 1.0, only
            // 0.1s after the click) instead of the correct 0.9 + 0.5 = 1.4 a fresh click must get -
            // see FreshReClickUsesTheFixedDecisionAndRebasesCorrectly for the fix, which is that a
            // fresh click's heldLastFrame must be false, never the bare "!weapon.CanCharge" this
            // pins as wrong.
            float next = FireScheduleRule.NextFireTime(previousNextFireTime: 0.5f, now: 0.9f, interval: 0.5f,
                                                        triggerHeldContinuously: true, blockedThisTick: false);
            Assert.AreEqual(1.0f, next, 1e-5f);
        }

        [Test]
        public void IsContinuingHoldIsFalseForAFreshClickEvenOnANonChargeWeapon()
        {
            // The decision WeaponFiring.TryFire's click handler and its own public TryFire() must
            // both make: heldLastFrame is false for anything that isn't a continuing hold (a fresh
            // click, or the frame right after a release) - the weapon's CanCharge does not matter.
            Assert.IsFalse(FireScheduleRule.IsContinuingHold(heldLastFrame: false, weaponCanCharge: false));
        }

        [Test]
        public void IsContinuingHoldIsTrueOnceHeldAcrossAFrameBoundaryOnANonChargeWeapon()
        {
            // Update's held-fire path only ever sees this true once the trigger was ALSO held on
            // the immediately preceding frame - never on the frame of the press itself.
            Assert.IsTrue(FireScheduleRule.IsContinuingHold(heldLastFrame: true, weaponCanCharge: false));
        }

        [Test]
        public void IsContinuingHoldIsAlwaysFalseForAChargeWeapon()
        {
            // A charge weapon's one-off release must still simply rebase, exactly as before -
            // heldLastFrame is irrelevant for it (Update's own held-fire path never calls TryFire
            // for a CanCharge weapon in the first place, but the decision stays defensive either way).
            Assert.IsFalse(FireScheduleRule.IsContinuingHold(heldLastFrame: true, weaponCanCharge: true));
        }

        [Test]
        public void FreshReClickUsesTheFixedDecisionAndRebasesCorrectly()
        {
            // The fixed end-to-end sequence: same numbers as the bug reproduction above, but fed
            // through IsContinuingHold the way the fixed WeaponFiring.TryFire now does - a fresh
            // click's heldLastFrame is false, so the schedule rebases off the re-click at 0.9
            // instead of carrying the stale deadline forward.
            bool continuingHold = FireScheduleRule.IsContinuingHold(heldLastFrame: false, weaponCanCharge: false);
            float next = FireScheduleRule.NextFireTime(previousNextFireTime: 0.5f, now: 0.9f, interval: 0.5f,
                                                        triggerHeldContinuously: continuingHold, blockedThisTick: false);
            Assert.AreEqual(1.4f, next, 1e-5f);
        }

        [Test]
        public void ASteadyHoldStillCarriesOverAtExactlyTheFireRateThroughTheFixedDecision()
        {
            // The 2.6 behaviour that must not regress: once genuinely held across a frame
            // boundary, the schedule still carries forward by exactly one interval, frame-rate
            // independent - see the class comment on why that matters for a buffed fast weapon.
            bool continuingHold = FireScheduleRule.IsContinuingHold(heldLastFrame: true, weaponCanCharge: false);
            float next = FireScheduleRule.NextFireTime(previousNextFireTime: 1f, now: 1f, interval: 0.5f,
                                                        triggerHeldContinuously: continuingHold, blockedThisTick: false);
            Assert.AreEqual(1.5f, next, 1e-5f);
        }

        [Test]
        public void ACastGateInterruptionDoesNotLeaveACatchUpShotOneTickAfterItLifts()
        {
            // Reproduces the review's own numbers: a real shot at t=0 with interval I=1 leaves the
            // deadline at 1. Silenced/stunned from just after that until the block lifts at
            // t = 2*I - tick, where "tick" (0.1s here) stands in for one rendered frame - the last
            // BLOCKED tick immediately before that is what determines the final clamped deadline,
            // since every earlier blocked tick's clamp is superseded by a later one.
            const float interval = 1f;
            const float tick = 0.1f;
            float staleDeadline = 1f; // left behind by the real shot at t = 0.

            float lastBlockedTickTime = 2f * interval - tick - tick;
            float deadlineAfterLastBlockedTick = FireScheduleRule.NextFireTime(
                staleDeadline, lastBlockedTickTime, interval, triggerHeldContinuously: false, blockedThisTick: true);

            float unblockAt = 2f * interval - tick;
            float afterUnblock = FireScheduleRule.NextFireTime(
                deadlineAfterLastBlockedTick, unblockAt, interval, triggerHeldContinuously: true, blockedThisTick: false);

            // Before this fix, the deadline stayed frozen at staleDeadline (1) the whole time it
            // was blocked, so the same decision at unblockAt (1.9) computed 1 + interval = 2 - only
            // one tick after unblockAt, letting a second shot fire almost immediately. The fix
            // keeps the next shot no sooner than roughly a full interval after the block lifts,
            // off by at most one blocked tick's own granularity rather than nearly a whole interval.
            Assert.GreaterOrEqual(afterUnblock, unblockAt + interval - tick - 1e-4f);
        }
    }
}
