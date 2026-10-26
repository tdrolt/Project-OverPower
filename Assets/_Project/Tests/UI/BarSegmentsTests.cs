using System.Reflection;
using NUnit.Framework;
using Overpower.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Tests
{
    /// <summary>The divider lines every few hit points on the health and armour bars, the "current/max" number on the HUD's bars,
    /// and the views that must draw them from these rules. Literal values only.</summary>
    public class BarSegmentsTests
    {
        private const float Tol = 1e-4f;

        private static float[] Lines(float max, float size, int room = 16)
        {
            var into = new float[room];
            int n = BarSegments.Dividers(max, size, into);
            var result = new float[n];
            System.Array.Copy(into, result, n);
            return result;
        }

        [Test]
        public void AHundredInStepsOfTwentyFiveIsCutInFour()
        {
            float[] l = Lines(100f, 25f);
            Assert.AreEqual(3, l.Length);
            Assert.AreEqual(0.25f, l[0], Tol);
            Assert.AreEqual(0.5f, l[1], Tol);
            Assert.AreEqual(0.75f, l[2], Tol);
        }

        [Test]
        public void ABarThatIsNotAWholeNumberOfStepsGetsALineAtEveryFullStep()
        {
            float[] l = Lines(110f, 25f);
            Assert.AreEqual(4, l.Length);
            Assert.AreEqual(25f / 110f, l[0], Tol);
            Assert.AreEqual(100f / 110f, l[3], Tol);
        }

        [Test]
        public void NoLineSitsOnTheBarsEnd()
        {
            float[] l = Lines(50f, 25f);
            Assert.AreEqual(1, l.Length);
            Assert.AreEqual(0.5f, l[0], Tol);
        }

        [Test]
        public void ABarShorterThanOneStepHasNoLines()
        {
            Assert.AreEqual(0, Lines(20f, 25f).Length);
        }

        [Test]
        public void AStepOfNothingOrAnEmptyBarDrawsNoLines()
        {
            Assert.AreEqual(0, Lines(100f, 0f).Length);
            Assert.AreEqual(0, Lines(100f, -5f).Length);
            Assert.AreEqual(0, Lines(0f, 25f).Length);
        }

        [Test]
        public void NeverMoreLinesThanThereIsRoomFor()
        {
            Assert.AreEqual(8, Lines(1000f, 1f, 8).Length);
        }

        [Test]
        public void ALineShowsOnAFillOnlyWhereTheFillReaches()
        {
            Assert.IsTrue(BarSegments.OnFill(0.4f, 0.5f));
            Assert.IsFalse(BarSegments.OnFill(0.6f, 0.5f));
            Assert.IsFalse(BarSegments.OnFill(0.5f, 0.5f), "a line at the very end of the fill would stick out past it");
        }

        [Test]
        public void TheNumberIsCurrentOverMax()
        {
            Assert.AreEqual("35/125", BarSegments.Number(35f, 125f));
        }

        [Test]
        public void APartHitPointCountsAsAWholeOneSoTheLivingNeverReadZero()
        {
            Assert.AreEqual("1/100", BarSegments.Number(0.3f, 100f));
            Assert.AreEqual("35/125", BarSegments.Number(34.2f, 125f));
        }

        [Test]
        public void ARoundingCrumbDoesNotAddAPoint()
        {
            Assert.AreEqual("35/125", BarSegments.Number(35.0004f, 125f));
            Assert.AreEqual(35, BarSegments.Shown(35.0004f));
        }

        [Test]
        public void TheNumberStaysBetweenZeroAndMax()
        {
            Assert.AreEqual("0/100", BarSegments.Number(-5f, 100f));
            Assert.AreEqual("100/100", BarSegments.Number(130f, 100f));
        }

        // ---- the drawn lines ------------------------------------------------------------------------------------------

        private GameObject root;

        [TearDown]
        public void DestroyRoot()
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }

        private BarDividers NewDividers()
        {
            root = new GameObject("Fill", typeof(RectTransform));
            return new BarDividers((RectTransform)root.transform, 2f, Color.black);
        }

        private static int Shown(BarDividers d)
        {
            int n = 0;
            foreach (Image line in d.Lines)
                if (line.enabled && line.gameObject.activeSelf)
                    n++;
            return n;
        }

        [Test]
        public void TheLinesAreDrawnWhereTheRuleSays()
        {
            BarDividers d = NewDividers();
            d.Layout(100f, 25f);
            Assert.AreEqual(3, Shown(d));
            Assert.AreEqual(3, d.Count);
            for (int i = 0; i < 3; i++)
            {
                RectTransform rt = d.Lines[i].rectTransform;
                Assert.AreEqual(0.25f * (i + 1), rt.anchorMin.x, Tol);
                Assert.AreEqual(rt.anchorMin.x, rt.anchorMax.x, Tol);
                Assert.AreEqual(0f, rt.anchorMin.y, Tol, "a line runs the bar's full height");
                Assert.AreEqual(1f, rt.anchorMax.y, Tol);
                Assert.AreEqual(2f, rt.sizeDelta.x, Tol);
                Assert.IsFalse(d.Lines[i].raycastTarget);
                Assert.AreSame(root.transform, rt.parent);
            }
        }

        [Test]
        public void AShorterBarHidesTheLinesItNoLongerNeeds()
        {
            BarDividers d = NewDividers();
            d.Layout(100f, 25f);
            d.Layout(50f, 25f);
            Assert.AreEqual(1, Shown(d));
            Assert.AreEqual(0.5f, d.Lines[0].rectTransform.anchorMin.x, Tol);
        }

        [Test]
        public void OnAPartFillOnlyTheLinesItReachesShow()
        {
            BarDividers d = NewDividers();
            d.Layout(100f, 25f);
            d.ShowUpTo(0.6f);
            Assert.AreEqual(2, Shown(d));
            d.ShowUpTo(1f);
            Assert.AreEqual(3, Shown(d));
        }

        private static MethodInfo M(System.Type type, string name) => type.GetMethod(name);

        [Test]
        public void TheLinesComeFromTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BarDividers), nameof(BarDividers.Layout), M(typeof(BarSegments), nameof(BarSegments.Dividers))));
            Assert.IsTrue(IlWiring.Uses(typeof(BarDividers), nameof(BarDividers.ShowUpTo), M(typeof(BarSegments), nameof(BarSegments.OnFill))));
        }

        [Test]
        public void TheHudLaysItsLinesOutEveryUpdate() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHud), "UpdateHealthAndArmor", M(typeof(BarDividers), nameof(BarDividers.Layout))));

        [Test]
        public void TheHudWritesItsNumbersFromTheRule()
        {
            MethodInfo setNumber = typeof(PlayerHud).GetMethod("SetBarNumber", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(setNumber, "PlayerHud.SetBarNumber writes one bar's number");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHud), "SetBarNumber", M(typeof(BarSegments), nameof(BarSegments.Number))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHud), "UpdateHealthAndArmor", setNumber));
        }

        [Test]
        public void ThePlateDrawsItsLinesFromTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(NameplateView), nameof(NameplateView.ShowSegments), M(typeof(BarDividers), nameof(BarDividers.Layout))));
            Assert.IsTrue(IlWiring.Uses(typeof(NameplateView), nameof(NameplateView.ShowSegments), M(typeof(BarDividers), nameof(BarDividers.ShowUpTo))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "UpdateOverheadBar", M(typeof(NameplateView), nameof(NameplateView.ShowSegments))));
        }
    }
}
