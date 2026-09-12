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
    }
}
