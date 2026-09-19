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

        private static void AssertClose(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 0.0005f);
            Assert.AreEqual(expected.g, actual.g, 0.0005f);
            Assert.AreEqual(expected.b, actual.b, 0.0005f);
            Assert.AreEqual(expected.a, actual.a, 0.0005f);
        }
    }
}
