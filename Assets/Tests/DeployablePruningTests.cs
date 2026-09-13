using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class DeployablePruningTests
    {
        [Test]
        public void UnderTheCapRemovesNothing()
        {
            var result = DeployablePruning.OverflowBySeq(new[] { 0, 1 }, maxCount: 2);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void AtExactlyTheCapRemovesNothing()
        {
            var result = DeployablePruning.OverflowBySeq(new[] { 0, 1, 2 }, maxCount: 3);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void OneOverTheCapRemovesOnlyTheOldestSeq()
        {
            // A third portal placed - the FIRST one (seq 0) must go, never the second (seq 1).
            var result = DeployablePruning.OverflowBySeq(new[] { 0, 1, 2 }, maxCount: 2);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(0, result[0]);
        }

        [Test]
        public void UnsortedInputStillFindsTheOldest()
        {
            var result = DeployablePruning.OverflowBySeq(new[] { 5, 3, 4 }, maxCount: 2);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3, result[0]);
        }

        [Test]
        public void SeveralOverTheCapRemovesAllTheOldestOnes()
        {
            var result = DeployablePruning.OverflowBySeq(new[] { 10, 11, 12, 13 }, maxCount: 1);

            Assert.AreEqual(3, result.Count);
            CollectionAssert.AreEqual(new[] { 10, 11, 12 }, result);
        }

        [Test]
        public void EmptyInputRemovesNothing()
        {
            var result = DeployablePruning.OverflowBySeq(System.Array.Empty<int>(), maxCount: 2);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ZeroMaxCountRemovesEverything()
        {
            var result = DeployablePruning.OverflowBySeq(new[] { 7, 8 }, maxCount: 0);

            CollectionAssert.AreEqual(new[] { 7, 8 }, result);
        }

        [Test]
        public void NegativeMaxCountIsTreatedAsZero()
        {
            var result = DeployablePruning.OverflowBySeq(new[] { 7 }, maxCount: -1);

            CollectionAssert.AreEqual(new[] { 7 }, result);
        }
    }
}
