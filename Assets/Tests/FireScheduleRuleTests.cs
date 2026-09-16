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
