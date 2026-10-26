using NUnit.Framework;
using UnityEngine;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Arena rebuild step 2: OwnerPaintColours.For, shared by the capture ring's edge and a tower's crown
    /// and caps, so the two draw exactly the same colour for the same OwnerPaint.</summary>
    public class OwnerPaintColoursTests
    {
        private UiTheme theme;

        [SetUp]
        public void SetUp() => theme = ScriptableObject.CreateInstance<UiTheme>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(theme);

        [Test]
        public void OwnedNeutralAndOutOfPlayMapToTheThemeColours()
        {
            var owned = new OwnerPaint(OwnerPaintBase.Team, 1, false, -1);
            Assert.AreEqual(theme.ShotColorFor(1), OwnerPaintColours.For(owned, theme, theme.towerNeutralColor, 0f));

            var neutral = new OwnerPaint(OwnerPaintBase.Neutral, -1, false, -1);
            Assert.AreEqual(theme.towerNeutralColor, OwnerPaintColours.For(neutral, theme, theme.towerNeutralColor, 0f));

            var outOfPlay = new OwnerPaint(OwnerPaintBase.OutOfPlay, -1, false, -1);
            Assert.AreEqual(theme.outOfPlayZoneColor, OwnerPaintColours.For(outOfPlay, theme, theme.towerNeutralColor, 0f));
        }

        [Test]
        public void AtThePulsePeakAnAttackedZoneShowsTheWarningAndADrainShowsTheDrainer()
        {
            float peak = 0.5f / theme.captureRingPulseSpeed;

            var attacked = new OwnerPaint(OwnerPaintBase.Team, 0, true, -1);
            Color attackedColour = OwnerPaintColours.For(attacked, theme, theme.towerNeutralColor, peak);
            AssertClose(theme.captureRingWarningColor, attackedColour);

            var draining = new OwnerPaint(OwnerPaintBase.Team, 0, false, 1);
            Color drainingColour = OwnerPaintColours.For(draining, theme, theme.towerNeutralColor, peak);
            AssertClose(theme.ShotColorFor(1), drainingColour);
        }

        [Test]
        public void APulseNeverMixesTwoColoursIntoAThird()
        {
            var purple = new Color(0.68f, 0.32f, 1f, 1f);
            var white = new Color(0.93f, 0.97f, 1f, 1f);
            var red = new Color(1f, 0.2f, 0.15f, 0.8f);
            theme.teamShotColors = new[] { white, purple };
            theme.captureRingWarningColor = red;
            theme.captureRingPulseSpeed = 1f;
            theme.captureRingPulseDip = 0.35f;
            var attacked = new OwnerPaint(OwnerPaintBase.Team, 1, true, -1);
            var drained = new OwnerPaint(OwnerPaintBase.Team, 0, false, 1);
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 80f; // half a period: from the owner's colour to the pulse's peak
                AssertOneOf(OwnerPaintColours.For(attacked, theme, theme.towerNeutralColor, t), purple, red, "attacked purple at " + t);
                AssertOneOf(OwnerPaintColours.For(drained, theme, theme.towerNeutralColor, t), white, purple, "white drained by purple at " + t);
            }
            Color dark = OwnerPaintColours.For(attacked, theme, theme.towerNeutralColor, 0.25f);
            Assert.AreEqual(0.35f, dark.maxColorComponent, 1e-3f, "at the swap the edge is Capture Ring Pulse Dip as bright");
            Assert.AreEqual(Mathf.Lerp(1f, 0.8f, 0.5f), dark.a, 1e-3f, "opacity moves straight from one to the other");
        }

        [Test]
        public void NoDipIsAHardBlink()
        {
            var purple = new Color(0.68f, 0.32f, 1f, 1f);
            var red = new Color(1f, 0.2f, 0.15f, 1f);
            theme.teamShotColors = new[] { purple };
            theme.captureRingWarningColor = red;
            theme.captureRingPulseSpeed = 1f;
            theme.captureRingPulseDip = 1f;
            var attacked = new OwnerPaint(OwnerPaintBase.Team, 0, true, -1);
            AssertClose(purple, OwnerPaintColours.For(attacked, theme, theme.towerNeutralColor, 0.24f));
            AssertClose(red, OwnerPaintColours.For(attacked, theme, theme.towerNeutralColor, 0.26f));
        }

        // The colour is one of the two scaled by one brightness (its hue and saturation unchanged), never a blend of both.
        private static void AssertOneOf(Color actual, Color a, Color b, string label)
        {
            bool Scaled(Color c)
            {
                float k = actual.maxColorComponent / c.maxColorComponent;
                return Mathf.Abs(actual.r - c.r * k) < 1e-3f && Mathf.Abs(actual.g - c.g * k) < 1e-3f && Mathf.Abs(actual.b - c.b * k) < 1e-3f;
            }
            Assert.IsTrue(Scaled(a) || Scaled(b), label + ": " + actual + " mixes " + a + " and " + b);
        }

        private static void AssertClose(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 0.0005f);
            Assert.AreEqual(expected.g, actual.g, 0.0005f);
            Assert.AreEqual(expected.b, actual.b, 0.0005f);
            Assert.AreEqual(expected.a, actual.a, 0.0005f);
        }
    }
}
