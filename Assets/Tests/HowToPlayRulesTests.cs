using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    /// <summary>The page navigation of How to play: the ends are disabled and never wrap, the counter and the neighbouring titles.</summary>
    public class HowToPlayRulesTests
    {
        private static readonly string[] Titles = { "Moving and aiming", "Shooting and overheat", "Ability slots", "Vision", "The shop", "Capturing zones" };

        [Test]
        public void TheFirstPageHasNoPreviousAndTheLastHasNoNext()
        {
            Assert.IsFalse(HowToPlayRules.HasPrevious(0, 6));
            Assert.IsTrue(HowToPlayRules.HasNext(0, 6));
            Assert.IsTrue(HowToPlayRules.HasPrevious(5, 6));
            Assert.IsFalse(HowToPlayRules.HasNext(5, 6));
        }

        [Test]
        public void AMiddlePageHasBoth()
        {
            Assert.IsTrue(HowToPlayRules.HasPrevious(3, 6));
            Assert.IsTrue(HowToPlayRules.HasNext(3, 6));
        }

        [Test]
        public void NextAndPreviousStepOnePageAndNeverWrapRound()
        {
            Assert.AreEqual(4, HowToPlayRules.Next(3, 6));
            Assert.AreEqual(2, HowToPlayRules.Previous(3, 6));
            Assert.AreEqual(5, HowToPlayRules.Next(5, 6), "the last page stays the last, it does not wrap to the first");
            Assert.AreEqual(0, HowToPlayRules.Previous(0, 6), "the first page stays the first, it does not wrap to the last");
        }

        [Test]
        public void NoPagesAndOnePageNeverHaveANeighbour()
        {
            Assert.IsFalse(HowToPlayRules.HasPrevious(0, 0));
            Assert.IsFalse(HowToPlayRules.HasNext(0, 0));
            Assert.IsFalse(HowToPlayRules.HasPrevious(0, 1));
            Assert.IsFalse(HowToPlayRules.HasNext(0, 1));
            Assert.AreEqual(0, HowToPlayRules.Next(0, 1));
        }

        [TestCase(-3, 6, 0)]
        [TestCase(0, 6, 0)]
        [TestCase(5, 6, 5)]
        [TestCase(6, 6, 5)]
        [TestCase(99, 6, 5)]
        [TestCase(4, 0, 0)]
        public void TheIndexIsKeptInsideThePages(int index, int count, int expected) => Assert.AreEqual(expected, HowToPlayRules.Clamp(index, count));

        [Test]
        public void TheCounterCountsFromOne()
        {
            Assert.AreEqual("4 / 6", HowToPlayRules.CounterText(3, 6, "{0} / {1}"));
            Assert.AreEqual("1 / 6", HowToPlayRules.CounterText(0, 6, "{0} / {1}"));
            Assert.AreEqual("6 / 6", HowToPlayRules.CounterText(5, 6, "{0} / {1}"));
            Assert.AreEqual("Page 2 of 6", HowToPlayRules.CounterText(1, 6, "Page {0} of {1}"), "the words are the theme's");
        }

        [Test]
        public void TheCounterReadsAnOutOfRangeIndexAsTheNearestPage()
        {
            Assert.AreEqual("6 / 6", HowToPlayRules.CounterText(40, 6, "{0} / {1}"));
            Assert.AreEqual("0 / 0", HowToPlayRules.CounterText(0, 0, "{0} / {1}"));
        }

        [Test]
        public void ABrokenCounterFormatShowsTheFormatInsteadOfThrowing()
        {
            Assert.DoesNotThrow(() => HowToPlayRules.CounterText(0, 6, "{0} / {1} / {2}"));
        }

        [Test]
        public void TheButtonsNameTheNeighbouringPages()
        {
            Assert.AreEqual("Ability slots", HowToPlayRules.PreviousTitle(Titles, 3));
            Assert.AreEqual("The shop", HowToPlayRules.NextTitle(Titles, 3));
            Assert.AreEqual("Shooting and overheat", HowToPlayRules.NextTitle(Titles, 0));
            Assert.AreEqual("The shop", HowToPlayRules.PreviousTitle(Titles, 5));
        }

        [Test]
        public void ThereIsNoTitleBeyondTheEnds()
        {
            Assert.IsNull(HowToPlayRules.PreviousTitle(Titles, 0));
            Assert.IsNull(HowToPlayRules.NextTitle(Titles, 5));
            Assert.IsNull(HowToPlayRules.PreviousTitle(null, 3));
            Assert.IsNull(HowToPlayRules.NextTitle(new string[0], 0));
        }
    }
}
