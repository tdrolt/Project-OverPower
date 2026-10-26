using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers the AoE zone's tick timing (Task 1.11b addendum, review fix): tick k due at
    /// k * tickSeconds, none early, none repeated - and, since the review fix, a genuinely LATE START
    /// (initialAgeSeconds > 0) skips every tick whose moment already passed before this client's first
    /// evaluation, while a HITCH mid-life on an otherwise on-time schedule still pays out every tick
    /// the frame spike spans, together, in one call. See ZoneTickSchedule's own class comment for why
    /// those two cases are deliberately different.</summary>
    public class ZoneTickScheduleTests
    {
        [Test]
        public void NoTickIsDueBeforeTheFirstTickSecondsElapse()
        {
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);

            Assert.AreEqual(0, schedule.ConsumeDueTicks(secondsSincePlaced: 0.99f));
        }

        [Test]
        public void TheFirstTickIsDueTheInstantItsMomentArrives()
        {
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);

            Assert.AreEqual(1, schedule.ConsumeDueTicks(secondsSincePlaced: 1f));
        }

        [Test]
        public void EachTickIsOnlyEverReportedOnce()
        {
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);
            schedule.ConsumeDueTicks(1f);

            // Called again for the same moment - tick 1 must not be handed out a second time.
            Assert.AreEqual(0, schedule.ConsumeDueTicks(secondsSincePlaced: 1.0f));
        }

        [Test]
        public void SixTicksOneSecondApartAreEachReportedInOrder()
        {
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);
            int totalDelivered = 0;

            for (float t = 1f; t <= 6f; t += 1f)
                totalDelivered += schedule.ConsumeDueTicks(t);

            Assert.AreEqual(6, totalDelivered);
            Assert.IsTrue(schedule.IsComplete);
        }

        [Test]
        public void NothingIsDueAfterAllTicksAreSpent()
        {
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);
            for (float t = 1f; t <= 6f; t += 1f)
                schedule.ConsumeDueTicks(t);

            Assert.AreEqual(0, schedule.ConsumeDueTicks(secondsSincePlaced: 100f));
            Assert.IsTrue(schedule.IsComplete);
        }

        [Test]
        public void ALateStartSkipsTicksAlreadyDueBeforeItsFirstEvaluationThenCatchesUpFromThere()
        {
            // Review fix (Task 1.11b): a late joiner's zone copy is already 3.2s old (initialAgeSeconds)
            // the moment THIS client's schedule is built - ticks 1-3's own moments already passed
            // before this client's players were ever simulated for them, so they never fire at all,
            // not even bundled together. Only ticks 4-6, still ahead of 3.2s, are owed - each arriving
            // at its own moment exactly like an on-time client would see them.
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6, initialAgeSeconds: 3.2f);

            Assert.AreEqual(0, schedule.ConsumeDueTicks(secondsSincePlaced: 3.2f)); // 1-3 skipped, not bundled here
            Assert.AreEqual(3, schedule.ConsumeDueTicks(secondsSincePlaced: 6f)); // ticks 4, 5, 6
            Assert.IsTrue(schedule.IsComplete);
        }

        [Test]
        public void ALateJoinerWhoseFirstEvaluationIsAfterTheZoneAlreadyEndedGetsNoTicksAtAll()
        {
            // The zone's whole life (6 ticks) was already over 500s before this client's schedule was
            // built (in practice the owner would have destroyed the networked object long before a
            // join this late could even instantiate a copy, but the schedule itself must not assume
            // that). None of those six ticks' moments were ever seen by this client, so none of them
            // fire retroactively - the opposite of the old "always catches up everything" behaviour.
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6, initialAgeSeconds: 500f);

            Assert.AreEqual(0, schedule.ConsumeDueTicks(secondsSincePlaced: 500f));
            Assert.IsTrue(schedule.IsComplete);
        }

        [Test]
        public void AHitchSpanningMultipleTicksMidLifeStillPaysThemAllOutTogether()
        {
            // Unlike the late-start tests above, initialAgeSeconds stays 0 here - this client WAS
            // evaluating the schedule right from placement. It just suffered one bad frame spike
            // partway through the zone's life that didn't resolve until secondsSincePlaced had already
            // reached 3.5. Ticks 2 and 3 both fall inside that span, and the victim really did stand in
            // the zone for both of them, so both still land together the moment this client catches up
            // - the class comment's "hitch" case, which the late-start fix must not have broken.
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);
            Assert.AreEqual(1, schedule.ConsumeDueTicks(secondsSincePlaced: 1f)); // tick 1, on time

            Assert.AreEqual(2, schedule.ConsumeDueTicks(secondsSincePlaced: 3.5f)); // the hitch: ticks 2 and 3 together

            Assert.AreEqual(1, schedule.ConsumeDueTicks(secondsSincePlaced: 4f)); // back on schedule for tick 4
        }

        [Test]
        public void AHitchSpanningTheEntireLifetimeStillPaysOutEverySingleTick()
        {
            // The hitch-vs-late-start distinction taken to its extreme: this client was still
            // evaluating from placement (initialAgeSeconds 0), it just never got a frame again until
            // the zone's whole life had already elapsed. All 6 ticks still land together, for the same
            // reason the shorter mid-life hitch above pays out more than one at once.
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);

            Assert.AreEqual(6, schedule.ConsumeDueTicks(secondsSincePlaced: 500f));
            Assert.AreEqual(0, schedule.ConsumeDueTicks(secondsSincePlaced: 501f));
        }

        [Test]
        public void IsCompleteIsFalseUntilTheLastTickIsConsumed()
        {
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);
            schedule.ConsumeDueTicks(5f);

            Assert.IsFalse(schedule.IsComplete);
        }
    }
}
