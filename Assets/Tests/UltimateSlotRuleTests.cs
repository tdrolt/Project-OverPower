using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    public class UltimateSlotRuleTests
    {
        [Test]
        public void NoUltimateWinsOverAFullMeter() =>
            Assert.AreEqual(UltimateSlotState.NoUltimate, UltimateSlotRule.StateFor(false, true, true));

        [Test]
        public void NoUltimateWinsOverThrowAndFilling()
        {
            Assert.AreEqual(UltimateSlotState.NoUltimate, UltimateSlotRule.StateFor(false, false, true));
            Assert.AreEqual(UltimateSlotState.NoUltimate, UltimateSlotRule.StateFor(false, false, false));
        }

        [Test]
        public void EquippedAndFullIsReady() =>
            Assert.AreEqual(UltimateSlotState.Ready, UltimateSlotRule.StateFor(true, true, true));

        [Test]
        public void EquippedPartialAndBlockedIsFilling() =>
            Assert.AreEqual(UltimateSlotState.Filling, UltimateSlotRule.StateFor(true, false, false));

        [Test]
        public void EquippedEmptyMeterButUsableSlotIsThrow() =>
            Assert.AreEqual(UltimateSlotState.Throw, UltimateSlotRule.StateFor(true, false, true));
    }
}
