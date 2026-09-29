using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class ChargePoolTests
    {
        // Dash's real values: 2 charges, 5s to recharge one.
        private static ChargePool NewPool() => new ChargePool(maxCharges: 2, rechargeSeconds: 5f);

        [Test]
        public void NewPoolIsFull()
        {
            var p = NewPool();
            Assert.AreEqual(2, p.Available);
        }

        [Test]
        public void TryConsumeDecrementsAndReturnsTrue()
        {
            var p = NewPool();
            bool result = p.TryConsume();

            Assert.IsTrue(result);
            Assert.AreEqual(1, p.Available);
        }

        [Test]
        public void ConsumingAnEmptyPoolReturnsFalseAndDoesNotGoNegative()
        {
            var p = NewPool();
            p.TryConsume();
            p.TryConsume();

            bool result = p.TryConsume();

            Assert.IsFalse(result);
            Assert.AreEqual(0, p.Available);
        }

        [Test]
        public void OneChargeReturnsAfterExactlyRechargeSeconds()
        {
            var p = NewPool();
            p.TryConsume();
            p.Tick(5f);

            Assert.AreEqual(2, p.Available);
        }

        [Test]
        public void SpendingTwoAndTickingOneRechargeSecondsReturnsOnlyOneCharge()
        {
            // The rule that matters: charges recharge one at a time, sequentially. Spending both
            // starts the timer for the first; only once that completes does the second timer
            // begin. A naive implementation that refills every missing charge together would
            // wrongly report 2 available here after a single rechargeSeconds has passed.
            var p = NewPool();
            p.TryConsume();
            p.TryConsume();

            p.Tick(5f);

            Assert.AreEqual(1, p.Available);
        }

        [Test]
        public void SpendingTwoAndTickingTwiceTheRechargeReturnsBoth()
        {
            var p = NewPool();
            p.TryConsume();
            p.TryConsume();

            p.Tick(10f);

            Assert.AreEqual(2, p.Available);
        }

        [Test]
        public void AFullPoolDoesNotExceedMaxChargesNoMatterHowLongYouTick()
        {
            var p = NewPool();
            p.Tick(1000f);

            Assert.AreEqual(2, p.Available);
        }

        [Test]
        public void RefillAllFillsInstantlyAndResetsProgress()
        {
            var p = NewPool();
            p.TryConsume();
            p.TryConsume();
            p.Tick(2f); // partway into recharging the first charge

            p.RefillAll();

            Assert.AreEqual(2, p.Available);
            Assert.AreEqual(0f, p.RechargeProgress, 0.001f);
        }

        [Test]
        public void SetMaxChargesUpwardGrantsTheNewChargeImmediately()
        {
            var p = NewPool();
            p.TryConsume();
            p.TryConsume(); // Available == 0

            p.SetMaxCharges(3);

            Assert.AreEqual(3, p.MaxCharges);
            // Going from a 2-cap pool sitting at 0 to a 3-cap pool should not silently forfeit
            // the extra slot - the new headroom is granted immediately, same as a respawn refill.
            Assert.AreEqual(1, p.Available);
        }

        [Test]
        public void SetMaxChargesDownwardClampsAvailableToTheNewMaximum()
        {
            var p = NewPool();
            p.SetMaxCharges(1);

            Assert.AreEqual(1, p.MaxCharges);
            Assert.AreEqual(1, p.Available);
        }

        [Test]
        public void RechargeProgressIsZeroOnAFullPool()
        {
            var p = NewPool();
            Assert.AreEqual(0f, p.RechargeProgress, 0.001f);
        }

        [Test]
        public void RechargeProgressIsRoughlyHalfwayThroughARecharge()
        {
            var p = NewPool();
            p.TryConsume();
            p.Tick(2.5f); // half of the 5s recharge

            Assert.AreEqual(0.5f, p.RechargeProgress, 0.01f);
        }

        [Test]
        public void SetRechargeSecondsKeepsAvailableUnchanged()
        {
            var p = NewPool();
            p.TryConsume(); // Available == 1

            p.SetRechargeSeconds(2f);

            Assert.AreEqual(1, p.Available);
        }

        [Test]
        public void SetRechargeSecondsChangesHowLongTheNextChargeTakes()
        {
            var p = NewPool();
            p.TryConsume();

            p.SetRechargeSeconds(2f);
            p.Tick(2f);

            Assert.AreEqual(2, p.Available);
        }

        [Test]
        public void AZeroSecondRechargeReportsProgressOfZeroRatherThanNaN()
        {
            var p = NewPool();
            p.TryConsume();

            p.SetRechargeSeconds(0f);

            Assert.AreEqual(0f, p.RechargeProgress, 0.001f);
            Assert.IsFalse(float.IsNaN(p.RechargeProgress));
        }

        [Test]
        public void AZeroSecondPoolRefillsInstantlyOnTheNextTick()
        {
            var p = NewPool();
            p.TryConsume();
            p.TryConsume(); // Available == 0

            p.SetRechargeSeconds(0f);
            p.Tick(0.016f); // one ordinary frame, not a specially large step

            Assert.AreEqual(2, p.Available);
        }

        [Test]
        public void SetRechargeSecondsClampsANegativeValueToZero()
        {
            var p = NewPool();
            p.TryConsume();

            p.SetRechargeSeconds(-5f);

            Assert.AreEqual(0f, p.RechargeProgress, 0.001f);
            Assert.IsFalse(float.IsNaN(p.RechargeProgress));
        }

        // ---- lock-out after running dry (numbers passed in, not Tudor's asset values) ----

        private static ChargePool NewLockoutPool(int needed) =>
            new ChargePool(maxCharges: 3, rechargeSeconds: 5f, chargesNeededAfterRunningDry: needed);

        [Test]
        public void RunningDryLocksUntilEnoughChargesHaveRefilled()
        {
            var p = NewLockoutPool(2);
            Assert.IsTrue(p.TryConsume());
            Assert.IsTrue(p.TryConsume());
            Assert.IsTrue(p.TryConsume());
            Assert.IsTrue(p.IsLocked);
            Assert.IsFalse(p.TryConsume());

            p.Tick(5f); // one charge back - still locked
            Assert.AreEqual(1, p.Available);
            Assert.IsTrue(p.IsLocked);
            Assert.IsFalse(p.TryConsume());
            Assert.AreEqual(1, p.Available);

            p.Tick(5f); // two back - unlocked
            Assert.AreEqual(2, p.Available);
            Assert.IsFalse(p.IsLocked);
            Assert.IsTrue(p.TryConsume());
        }

        [Test]
        public void UsingTwoOfThreeNeverLocks()
        {
            var p = NewLockoutPool(2);
            p.TryConsume();
            p.TryConsume();
            Assert.IsFalse(p.IsLocked);

            p.Tick(5f);
            Assert.AreEqual(2, p.Available);
            Assert.IsTrue(p.TryConsume());
            Assert.IsFalse(p.IsLocked);
        }

        [Test]
        public void NeededZeroIsTodaysBehaviour()
        {
            var p = NewLockoutPool(0);
            p.TryConsume();
            p.TryConsume();
            p.TryConsume();
            Assert.IsFalse(p.IsLocked);

            p.Tick(5f);
            Assert.AreEqual(1, p.Available);
            Assert.IsTrue(p.TryConsume());
        }

        [Test]
        public void NeededOneIsTodaysBehaviour()
        {
            var p = NewLockoutPool(1);
            p.TryConsume();
            p.TryConsume();
            p.TryConsume();
            Assert.IsFalse(p.IsLocked);
            p.Tick(5f);
            Assert.IsTrue(p.TryConsume());
        }

        [Test]
        public void RefillAllClearsTheLock()
        {
            var p = NewLockoutPool(2);
            p.TryConsume();
            p.TryConsume();
            p.TryConsume();
            Assert.IsTrue(p.IsLocked);

            p.RefillAll();
            Assert.IsFalse(p.IsLocked);
            Assert.IsTrue(p.TryConsume());
        }

        [Test]
        public void NeededAboveMaxChargesUnlocksAtFull()
        {
            var p = new ChargePool(maxCharges: 2, rechargeSeconds: 5f, chargesNeededAfterRunningDry: 5);
            p.TryConsume();
            p.TryConsume();
            Assert.IsTrue(p.IsLocked);
            p.Tick(5f);
            Assert.IsTrue(p.IsLocked);
            p.Tick(5f);
            Assert.IsFalse(p.IsLocked);
        }

        [Test]
        public void LoweringMaxChargesBelowNeededUnlocksAtTheClampedNumber()
        {
            var p = NewLockoutPool(3);
            p.TryConsume();
            p.TryConsume();
            p.TryConsume();
            p.Tick(5f);
            p.Tick(5f); // 2 available, needed 3: still locked
            Assert.IsTrue(p.IsLocked);

            p.SetMaxCharges(2); // needed clamps to 2, which we already have
            Assert.IsFalse(p.IsLocked);
        }

        [Test]
        public void TurningTheLockOutOffUnlocksAtOnce()
        {
            var p = NewLockoutPool(2);
            p.TryConsume();
            p.TryConsume();
            p.TryConsume();
            Assert.IsTrue(p.IsLocked);

            p.SetChargesNeededAfterRunningDry(0);
            Assert.IsFalse(p.IsLocked);
            p.Tick(5f);
            Assert.IsTrue(p.TryConsume());
        }

        [Test]
        public void OneBigTickThatRefillsTwoChargesUnlocks()
        {
            var p = NewLockoutPool(2);
            p.TryConsume();
            p.TryConsume();
            p.TryConsume();

            p.Tick(10.5f); // a lag spike: two recharge periods in one frame
            Assert.AreEqual(2, p.Available);
            Assert.IsFalse(p.IsLocked);
            Assert.IsTrue(p.TryConsume());
        }
    }
}
