using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class DamageReductionStackTests
    {
        [Test]
        public void AnEmptyStackReducesNothing()
        {
            var stack = new DamageReductionStack();
            Assert.AreEqual(0f, stack.Total, 0.001f);
        }

        [Test]
        public void OneSourceReturnsItsOwnFraction()
        {
            var stack = new DamageReductionStack();
            stack.Set("dash", 0.3f);

            Assert.AreEqual(0.3f, stack.Total, 0.001f);
        }

        [Test]
        public void TwoFiftyPercentSourcesCombineToSeventyFivePercent()
        {
            // Multiplicative stacking, not additive: 0.5 + 0.5 must not claim 100% reduction.
            var stack = new DamageReductionStack();
            stack.Set("dash", 0.5f);
            stack.Set("armor-upgrade", 0.5f);

            Assert.AreEqual(0.75f, stack.Total, 0.001f);
        }

        [Test]
        public void SettingTheSameKeyTwiceReplacesRatherThanStacks()
        {
            var stack = new DamageReductionStack();
            stack.Set("dash", 0.3f);
            stack.Set("dash", 0.5f);

            Assert.AreEqual(0.5f, stack.Total, 0.001f);
        }

        [Test]
        public void RemovingAKeyRestoresTheTotalToWhatIsLeft()
        {
            var stack = new DamageReductionStack();
            stack.Set("dash", 0.5f);
            stack.Set("armor-upgrade", 0.5f);

            stack.Remove("dash");

            Assert.AreEqual(0.5f, stack.Total, 0.001f);
        }

        [Test]
        public void RemovingTheOnlyKeyReturnsToZero()
        {
            var stack = new DamageReductionStack();
            stack.Set("dash", 0.5f);
            stack.Remove("dash");

            Assert.AreEqual(0f, stack.Total, 0.001f);
        }

        [Test]
        public void AFractionAboveOneClampsToOne()
        {
            var stack = new DamageReductionStack();
            stack.Set("bugged-buff", 1.5f);

            Assert.AreEqual(1f, stack.Total, 0.001f);
        }

        [Test]
        public void ANegativeFractionClampsToZeroRatherThanIncreasingDamage()
        {
            var stack = new DamageReductionStack();
            stack.Set("bugged-debuff", -0.5f);

            Assert.AreEqual(0f, stack.Total, 0.001f);
        }

        [Test]
        public void ClearEmptiesEverySource()
        {
            var stack = new DamageReductionStack();
            stack.Set("dash", 0.5f);
            stack.Set("armor-upgrade", 0.5f);

            stack.Clear();

            Assert.AreEqual(0f, stack.Total, 0.001f);
        }
    }
}
