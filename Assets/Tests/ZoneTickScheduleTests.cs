using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers the AoE zone's tick timing (Task 1.11b addendum): tick k due at k * tickSeconds,
    /// none early, none repeated, and a late start (a late joiner) pays out every owed tick together
    /// rather than skipping or duplicating any of them. See ZoneTickSchedule's own class comment.</summary>
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
        public void ALateStartPaysOutEveryOwedTickTogetherWithoutSkippingOrRepeating()
        {
            // A late joiner's first call can land well past several ticks' own moments at once - all
            // of them are owed, in one call, and none of them again afterwards.
            var schedule = new ZoneTickSchedule(tickSeconds: 1f, totalTicks: 6);

            Assert.AreEqual(3, schedule.ConsumeDueTicks(secondsSincePlaced: 3.2f));
            Assert.AreEqual(0, schedule.ConsumeDueTicks(secondsSincePlaced: 3.2f));
            Assert.AreEqual(3, schedule.ConsumeDueTicks(secondsSincePlaced: 6f)); // ticks 4, 5, 6
            Assert.IsTrue(schedule.IsComplete);
        }

        [Test]
        public void ALateJoinerPastTheFullDurationRunsExactlySixTicksNeverMore()
        {
            // A late joiner replaying a zone that is already long over must still only ever see the
            // six ticks it was always owed - never an extra one for having arrived late.
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
