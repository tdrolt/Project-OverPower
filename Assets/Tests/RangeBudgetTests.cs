using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class RangeBudgetTests
    {
        // The baseline weapon's real range, so these tests read against numbers the designer
        // recognises from the weapon asset rather than against invented ones.
        private static RangeBudget NewBudget() => new RangeBudget(30f);

        [Test]
        public void ANewBudgetHasTravelledNothingAndIsNotSpent()
        {
            var b = NewBudget();

            Assert.AreEqual(0f, b.Travelled, 0.0001f);
            Assert.AreEqual(30f, b.MaxRange, 0.0001f);
            Assert.IsFalse(b.IsSpent);
        }

        [Test]
        public void ConsumingBelowMaxAllowsTheWholeStepAndLeavesTheBudgetUnspent()
        {
            var b = NewBudget();

            float allowed = b.Consume(10f);

            Assert.AreEqual(10f, allowed, 0.0001f);
            Assert.AreEqual(10f, b.Travelled, 0.0001f);
            Assert.IsFalse(b.IsSpent);
        }

        [Test]
        public void ConsumingExactlyMaxSpendsTheBudget()
        {
            var b = NewBudget();

            float allowed = b.Consume(30f);

            Assert.AreEqual(30f, allowed, 0.0001f);
            Assert.AreEqual(30f, b.Travelled, 0.0001f);
            Assert.IsTrue(b.IsSpent);
        }

        [Test]
        public void AStepPastMaxReturnsOnlyTheRemainingDistanceAndStopsExactlyAtMaxRange()
        {
            // The whole reason Consume returns a distance rather than void. A projectile that
            // overshoots its max range would report a Fraction above 1, and a later weapon scales
            // damage linearly with Fraction up to +50% - so an overshoot lets that weapon exceed
            // the damage cap it was designed around.
            var b = NewBudget();
            b.Consume(28f);

            float allowed = b.Consume(5f);

            Assert.AreEqual(2f, allowed, 0.0001f);
            Assert.AreEqual(30f, b.Travelled, 0.0001f);
            Assert.IsTrue(b.IsSpent);
        }

        [Test]
        public void FractionIsZeroAtTheMuzzle()
        {
            Assert.AreEqual(0f, NewBudget().Fraction, 0.0001f);
        }

        [Test]
        public void FractionIsAHalfHalfwayAlong()
        {
            var b = NewBudget();
            b.Consume(15f);

            Assert.AreEqual(0.5f, b.Fraction, 0.0001f);
        }

        [Test]
        public void FractionIsOneWhenSpent()
        {
            var b = NewBudget();
            b.Consume(30f);

            Assert.AreEqual(1f, b.Fraction, 0.0001f);
        }

        [Test]
        public void FractionNeverExceedsOneEvenAfterAnOversizedStep()
        {
            var b = NewBudget();
            b.Consume(1000f);

            Assert.AreEqual(1f, b.Fraction, 0.0001f);
        }

        [Test]
        public void AZeroRangeBudgetIsSpentImmediatelyAndDoesNotDivideByZero()
        {
            // A designer can type 0 into Max Range, and float.NaN leaking out of Fraction here
            // would surface as a projectile that never despawns rather than as an obvious error.
            var b = new RangeBudget(0f);

            Assert.IsTrue(b.IsSpent);
            Assert.AreEqual(1f, b.Fraction, 0.0001f);
            Assert.IsFalse(float.IsNaN(b.Fraction));
        }

        [Test]
        public void ConsumingFromASpentBudgetReturnsZero()
        {
            var b = NewBudget();
            b.Consume(30f);

            float allowed = b.Consume(5f);

            Assert.AreEqual(0f, allowed, 0.0001f);
            Assert.AreEqual(30f, b.Travelled, 0.0001f);
        }

        [Test]
        public void ANegativeStepIsIgnoredRatherThanWindingTheBudgetBack()
        {
            // Guards against a caller handing over a negative deltaTime-derived step - a
            // projectile must not be able to buy itself extra range by travelling backwards.
            var b = NewBudget();
            b.Consume(10f);

            float allowed = b.Consume(-5f);

            Assert.AreEqual(0f, allowed, 0.0001f);
            Assert.AreEqual(10f, b.Travelled, 0.0001f);
        }

        [Test]
        public void ANegativeMaxRangeIsTreatedAsNoRangeAtAll()
        {
            var b = new RangeBudget(-10f);

            Assert.AreEqual(0f, b.MaxRange, 0.0001f);
            Assert.IsTrue(b.IsSpent);
            Assert.AreEqual(0f, b.Consume(1f), 0.0001f);
        }

        [Test]
        public void ManySmallStepsAccumulateToTheSameTravelledDistanceAsOneBigOne()
        {
            // How the projectile actually consumes it: one small step per frame. This is the
            // property that makes the range cap trustworthy at any frame rate.
            var b = NewBudget();

            for (int i = 0; i < 100; i++)
                b.Consume(0.3f);

            Assert.AreEqual(30f, b.Travelled, 0.001f);
            Assert.IsTrue(b.IsSpent);
        }
    }
}
