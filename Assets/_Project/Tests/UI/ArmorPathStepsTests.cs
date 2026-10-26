using NUnit.Framework;
using UnityEngine;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// The shop draws each armour upgrade as its own path of steps, Absorb above Recharge: which steps read reached, next, later or out of reach
    /// (both paths share one budget), their colours, and where the paths and steps sit.
    /// </summary>
    public class ArmorPathStepsTests
    {
        private const ArmorStep R = ArmorStep.Reached, N = ArmorStep.Next, L = ArmorStep.Later, X = ArmorStep.OutOfReach;

        [Test]
        public void AFreshPathHasItsStartReachedAndItsFirstUpgradeNext()
        {
            CollectionAssert.AreEqual(new[] { R, N, L }, ArmorPathSteps.For(0, 2, 2));
        }

        [Test]
        public void ABoughtStepIsReachedAndTheOneAfterItIsNext()
        {
            CollectionAssert.AreEqual(new[] { R, R, N }, ArmorPathSteps.For(1, 2, 1));
        }

        [Test]
        public void AStepNeedingMoreUpgradesThanAreLeftIsOutOfReach()
        {
            CollectionAssert.AreEqual(new[] { R, N, X }, ArmorPathSteps.For(0, 2, 1), "one upgrade left: only the next step");
            CollectionAssert.AreEqual(new[] { R, R, X }, ArmorPathSteps.For(1, 2, 0), "the other path spent the budget");
            CollectionAssert.AreEqual(new[] { R, X, X }, ArmorPathSteps.For(0, 2, 0), "nothing left: no step is next");
        }

        [Test]
        public void AtTheTopLevelEveryStepIsReached()
        {
            CollectionAssert.AreEqual(new[] { R, R, R }, ArmorPathSteps.For(2, 2, 0));
            CollectionAssert.AreEqual(new[] { R, R, R }, ArmorPathSteps.For(2, 2, 3), "upgrades left elsewhere change nothing here");
        }

        [Test]
        public void OneStepPerLevelWhateverThePathsLength()
        {
            Assert.AreEqual(5, ArmorPathSteps.For(0, 4, 9).Length);
            CollectionAssert.AreEqual(new[] { R, N, L, L, L }, ArmorPathSteps.For(0, 4, 9));
            CollectionAssert.AreEqual(new[] { R }, ArmorPathSteps.For(0, 0, 2), "a path with no upgrades shows only its start");
        }

        [Test]
        public void OddInputsReadSafely()
        {
            CollectionAssert.AreEqual(new[] { R, X, X }, ArmorPathSteps.For(0, 2, -1), "a negative budget is none");
            CollectionAssert.AreEqual(new[] { R, R, R }, ArmorPathSteps.For(7, 2, 1), "a level past the top reads as the top");
            CollectionAssert.AreEqual(new[] { R, N, L }, ArmorPathSteps.For(-3, 2, 2), "a negative level reads as the start");
        }

        private static UiTheme ThemeWithDistinctColours()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.loadoutArmorStepReachedColor = new Color(0.1f, 0f, 0f, 1f);
            theme.loadoutArmorStepNextColor = new Color(0.2f, 0f, 0f, 1f);
            theme.loadoutArmorStepLaterColor = new Color(0.3f, 0f, 0f, 1f);
            theme.loadoutArmorStepOutOfReachColor = new Color(0.4f, 0f, 0f, 1f);
            theme.loadoutPanelColor = new Color(0.5f, 0f, 0f, 1f);
            return theme;
        }

        [Test]
        public void EachStepGetsItsOwnRingAndOnlyAReachedStepIsFilled()
        {
            UiTheme theme = ThemeWithDistinctColours();
            try
            {
                Assert.AreEqual(theme.loadoutArmorStepReachedColor, ArmorPathSteps.Border(R, theme));
                Assert.AreEqual(theme.loadoutArmorStepNextColor, ArmorPathSteps.Border(N, theme));
                Assert.AreEqual(theme.loadoutArmorStepLaterColor, ArmorPathSteps.Border(L, theme));
                Assert.AreEqual(theme.loadoutArmorStepOutOfReachColor, ArmorPathSteps.Border(X, theme));

                Assert.AreEqual(theme.loadoutArmorStepReachedColor, ArmorPathSteps.Fill(R, theme));
                foreach (ArmorStep hollow in new[] { N, L, X })
                    Assert.AreEqual(theme.loadoutPanelColor, ArmorPathSteps.Fill(hollow, theme), hollow + " stays hollow");
            }
            finally { Object.DestroyImmediate(theme); }
        }

        [Test]
        public void TheLineIntoAStepIsLitOnlyOnceTheStepIsReached()
        {
            UiTheme theme = ThemeWithDistinctColours();
            try
            {
                Assert.AreEqual(theme.loadoutArmorStepReachedColor, ArmorPathSteps.LineInto(R, theme));
                Assert.AreEqual(theme.loadoutArmorStepLaterColor, ArmorPathSteps.LineInto(N, theme), "the next step's line is not lit yet");
                Assert.AreEqual(theme.loadoutArmorStepLaterColor, ArmorPathSteps.LineInto(L, theme));
                Assert.AreEqual(theme.loadoutArmorStepOutOfReachColor, ArmorPathSteps.LineInto(X, theme));
            }
            finally { Object.DestroyImmediate(theme); }
        }

        [Test]
        public void TheSecondPathStartsBelowTheFirstWithTheGapBetween()
        {
            Assert.AreEqual(0f, ArmorPathLayout.RowTop(0, 64f, 8f), 1e-4f);
            Assert.AreEqual(72f, ArmorPathLayout.RowTop(1, 64f, 8f), 1e-4f);
            Assert.AreEqual(136f, ArmorPathLayout.SectionHeight(64f, 8f), 1e-4f);
        }

        [Test]
        public void ThePathsNeverOverlapAndBothFitTheSection()
        {
            foreach (float h in new[] { 1f, 40f, 64f, 120f })
                foreach (float gap in new[] { 0f, 8f, 30f })
                {
                    float upperBottom = ArmorPathLayout.RowTop(0, h, gap) + h;
                    float lowerTop = ArmorPathLayout.RowTop(1, h, gap);
                    Assert.GreaterOrEqual(lowerTop, upperBottom, $"h {h} gap {gap}: the lower path starts under the upper one");
                    Assert.AreEqual(gap, lowerTop - upperBottom, 1e-4f, $"h {h} gap {gap}");
                    Assert.AreEqual(ArmorPathLayout.SectionHeight(h, gap), lowerTop + h, 1e-4f, $"h {h} gap {gap}: the section ends with the lower path");
                }
        }

        [Test]
        public void StepsAreSpacedEvenlyAndCentredInTheTrack()
        {
            Assert.AreEqual(410f, ArmorPathLayout.StepCentreX(0, 3, 400f, 600f, 90f), 1e-4f);
            Assert.AreEqual(500f, ArmorPathLayout.StepCentreX(1, 3, 400f, 600f, 90f), 1e-4f);
            Assert.AreEqual(590f, ArmorPathLayout.StepCentreX(2, 3, 400f, 600f, 90f), 1e-4f);
            Assert.AreEqual(500f, ArmorPathLayout.StepCentreX(0, 1, 400f, 600f, 90f), 1e-4f, "a single step sits in the middle");
            Assert.AreEqual(455f, ArmorPathLayout.StepCentreX(0, 2, 400f, 600f, 90f), 1e-4f);
        }
    }
}
