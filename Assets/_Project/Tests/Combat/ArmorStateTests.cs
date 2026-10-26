using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class ArmorStateTests
    {
        // Tier 0 from the real ArmorConfig defaults: soaks 25 damage, refills after 6 seconds out
        // of combat, then climbs from empty to full over 2.5 seconds once it starts - a rate of
        // 10 armor per second.
        private static ArmorState NewState() => new ArmorState(
            capacity: 25f, rechargeDelaySeconds: 6f, refillSeconds: 2.5f);

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
        public void ReachingTheRechargeDelayBeginsAGradualRefillRatherThanFillingInstantly()
        {
            // Tudor's 2026-09-13 revision: the old design snapped straight to Capacity here. A
            // pool that just crossed the delay should have moved only a little, not all the way.
            var a = NewState();
            a.Absorb(40f); // fully broken: Current == 0

            a.Tick(0.1f, secondsSinceCombat: 6f);

            Assert.AreEqual(1f, a.Current, 0.001f); // 10 armor/sec * 0.1s
            Assert.Less(a.Current, a.Capacity);
            Assert.IsFalse(a.IsBroken); // above zero is enough to clear Broken, even mid-refill.
        }

        [Test]
        public void RefillClimbsAtCapacityDividedByRefillSecondsPerSecond()
        {
            var a = NewState();
            a.Absorb(25f); // fully broken

            a.Tick(1f, secondsSinceCombat: 6f);

            Assert.AreEqual(10f, a.Current, 0.001f); // 25 / 2.5 = 10 per second
        }

        [Test]
        public void RefillReachesFullCapacityAfterRefillSecondsOfTimePastTheDelay()
        {
            var a = NewState();
            a.Absorb(25f); // fully broken

            a.Tick(2.5f, secondsSinceCombat: 8.5f); // 2.5s worth of refill ticked in one call

            Assert.AreEqual(25f, a.Current, 0.001f);
            Assert.IsFalse(a.IsBroken);
        }

        [Test]
        public void RefillNeverExceedsCapacityEvenWithAnOvershootingDeltaTime()
        {
            var a = NewState();
            a.Absorb(25f);

            a.Tick(10f, secondsSinceCombat: 20f); // far more than refillSeconds' worth

            Assert.AreEqual(25f, a.Current, 0.001f);
        }

        [Test]
        public void APartlyDepletedPoolFinishesRefillingSoonerThanAFullyBrokenPool()
        {
            // The whole point of a gradual refill over an instant one: less damage taken means
            // less time spent waiting for the pool to top back up.
            var partlyDepleted = NewState();
            partlyDepleted.Absorb(10f); // 15 of 25 left, needs 1s at 10/sec to finish

            var fullyBroken = NewState();
            fullyBroken.Absorb(25f); // needs the full 2.5s to finish

            partlyDepleted.Tick(1f, secondsSinceCombat: 6f);
            fullyBroken.Tick(1f, secondsSinceCombat: 6f);

            Assert.AreEqual(25f, partlyDepleted.Current, 0.001f); // already full
            Assert.AreEqual(10f, fullyBroken.Current, 0.001f);    // still climbing
        }

        [Test]
        public void TakingDamageMidRefillDropsCurrentAndTheNextLowSecondsSinceCombatHaltsProgress()
        {
            // secondsSinceCombat is owned by the caller (PlayerHealth) and is reset to 0 the
            // instant this player deals or takes damage - ArmorState only needs to see that reset
            // reflected here to stop climbing and wait out the delay again.
            var a = NewState();
            a.Absorb(25f);
            a.Tick(0.1f, secondsSinceCombat: 6f); // Current is now 1, mid-refill

            a.Absorb(1f); // took another hit - back to fully broken
            a.Tick(0.1f, secondsSinceCombat: 0f); // the caller's timer just restarted

            Assert.AreEqual(0f, a.Current, 0.001f);
            Assert.IsTrue(a.IsBroken);
        }

        [Test]
        public void ARefillSecondsOfZeroFillsInstantlyRatherThanDividingByZero()
        {
            // A designer-facing safety net, not a supported tuning: ArmorConfig.RefillSeconds
            // should never actually be typed as 0, but this must not throw or produce NaN/Infinity
            // if it ever is.
            var a = new ArmorState(capacity: 25f, rechargeDelaySeconds: 6f, refillSeconds: 0f);
            a.Absorb(25f);

            a.Tick(0.001f, secondsSinceCombat: 6f);

            Assert.AreEqual(25f, a.Current, 0.001f);
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
            var a = new ArmorState(capacity: 100f, rechargeDelaySeconds: 2f, refillSeconds: 2.5f);

            a.SetTier(capacity: 10f, rechargeDelaySeconds: 6f);

            Assert.LessOrEqual(a.Current, 10f);
            Assert.AreEqual(10f, a.Current, 0.001f);
            Assert.AreEqual(10f, a.Capacity, 0.001f);
        }

        [Test]
        public void SetTierMidRefillFillsToTheNewCapacityRatherThanContinuingTheOldClimb()
        {
            // A purchase landing while a gradual refill is already in progress must still be
            // instant and complete - it does not keep whatever fraction had ticked in so far.
            var a = NewState();
            a.Absorb(25f); // fully broken
            a.Tick(0.1f, secondsSinceCombat: 6f); // mid-refill: Current == 1, well short of 25

            a.SetTier(capacity: 100f, rechargeDelaySeconds: 2f);

            Assert.AreEqual(100f, a.Current, 0.001f);
            Assert.AreEqual(100f, a.Capacity, 0.001f);
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
        public void RefillToFullFillsToCapacityWithoutChangingTier()
        {
            // Backs PlayerHealth.ResetForRespawn's RespawnWithFullArmor path - a respawn decides
            // how much of the CURRENT tier's armor a player starts with, never a tier change.
            var a = NewState();
            a.Absorb(10f); // 15 of 25 left

            a.RefillToFull();

            Assert.AreEqual(25f, a.Current, 0.001f);
            Assert.AreEqual(25f, a.Capacity, 0.001f);
        }

        [Test]
        public void RefillToFullOnAnAlreadyBrokenPoolAlsoFills()
        {
            var a = NewState();
            a.Absorb(25f); // fully broken

            a.RefillToFull();

            Assert.AreEqual(25f, a.Current, 0.001f);
            Assert.IsFalse(a.IsBroken);
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
