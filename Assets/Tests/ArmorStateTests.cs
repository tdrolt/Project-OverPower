using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class ArmorStateTests
    {
        // Tier 0 from the real ArmorConfig defaults: soaks 25 damage, refills after 6 seconds
        // out of combat.
        private static ArmorState NewState() => new ArmorState(
            capacity: 25f, rechargeDelaySeconds: 6f);

        [Test]
        public void AbsorbingLessThanCapacityTakesTheFullHitAndLeavesTheRemainder()
        {
            var a = NewState();

            float taken = a.Absorb(10f);

            Assert.AreEqual(10f, taken, 0.001f);
            Assert.AreEqual(15f, a.Current, 0.001f);
            Assert.IsFalse(a.IsBroken);
        }

        [Test]
        public void AbsorbingMoreThanCapacityTakesOnlyWhatItHadAndBreaks()
        {
            // The overflow is what the caller passes on to health, so Absorb reporting more than
            // it actually soaked would let a player tank damage that never existed.
            var a = NewState();

            float taken = a.Absorb(40f);

            Assert.AreEqual(25f, taken, 0.001f);
            Assert.AreEqual(0f, a.Current, 0.001f);
            Assert.IsTrue(a.IsBroken);
        }

        [Test]
        public void TickBelowTheRechargeDelayDoesNotRefill()
        {
            var a = NewState();
            a.Absorb(40f);

            a.Tick(0.1f, secondsSinceCombat: 5.9f);

            Assert.AreEqual(0f, a.Current, 0.001f);
            Assert.IsTrue(a.IsBroken);
        }

        [Test]
        public void ReachingTheRechargeDelayRefillsToFullAndClearsBroken()
        {
            var a = NewState();
            a.Absorb(40f);

            a.Tick(0.1f, secondsSinceCombat: 6f);

            Assert.AreEqual(25f, a.Current, 0.001f);
            Assert.IsFalse(a.IsBroken);
        }

        [Test]
        public void SetTierToALargerCapacityRefillsToTheNewValue()
        {
            // Buying an upgrade must hand the player the armor they just paid for, not leave them
            // on the old amount until their next lull in combat.
            var a = NewState();
            a.Absorb(20f); // 5 left of 25

            a.SetTier(capacity: 50f, rechargeDelaySeconds: 4f);

            Assert.AreEqual(50f, a.Capacity, 0.001f);
            Assert.AreEqual(50f, a.Current, 0.001f);
        }

        [Test]
        public void SetTierToASmallerCapacityClampsCurrentToTheNewCapacity()
        {
            var a = new ArmorState(capacity: 100f, rechargeDelaySeconds: 2f);

            a.SetTier(capacity: 10f, rechargeDelaySeconds: 6f);

            Assert.LessOrEqual(a.Current, 10f);
            Assert.AreEqual(10f, a.Current, 0.001f);
            Assert.AreEqual(10f, a.Capacity, 0.001f);
        }

        [Test]
        public void ClearEmptiesThePoolAndBreaksIt()
        {
            var a = NewState();

            a.Clear();

            Assert.AreEqual(0f, a.Current, 0.001f);
            Assert.IsTrue(a.IsBroken);
        }

        [Test]
        public void AbsorbingZeroTakesNothingAndChangesNothing()
        {
            var a = NewState();

            float taken = a.Absorb(0f);

            Assert.AreEqual(0f, taken, 0.001f);
            Assert.AreEqual(25f, a.Current, 0.001f);
        }

        [Test]
        public void SetFromNetworkAcceptsAnInRangeValue()
        {
            // The normal case: a remote client adopting exactly what the owner reported.
            var a = NewState();

            a.SetFromNetwork(12f);

            Assert.AreEqual(12f, a.Current, 0.001f);
        }

        [Test]
        public void SetFromNetworkClampsAboveCapacity()
        {
            // A stale or out-of-order packet must never hand a remote client more armor than the
            // pool can legitimately hold.
            var a = NewState();

            a.SetFromNetwork(999f);

            Assert.AreEqual(25f, a.Current, 0.001f);
        }

        [Test]
        public void SetFromNetworkClampsBelowZero()
        {
            var a = NewState();

            a.SetFromNetwork(-5f);

            Assert.AreEqual(0f, a.Current, 0.001f);
            Assert.IsTrue(a.IsBroken);
        }
    }
}
