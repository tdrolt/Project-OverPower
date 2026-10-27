using System.Reflection;
using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>A team's HUD colour comes from its own entry; a team the list does not hold (nobody, a malformed id) gets the screen's
    /// neutral colour, never team 0's, which is a real team's colour and would claim the thing for that team.</summary>
    public class TeamPaletteTests
    {
        private static readonly Color A = new Color(0.1f, 0.2f, 0.3f, 1f);
        private static readonly Color B = new Color(0.4f, 0.5f, 0.6f, 1f);
        private static readonly Color Unknown = new Color(0.7f, 0.7f, 0.7f, 1f);

        [Test]
        public void AKnownTeamGetsItsOwnEntry()
        {
            Assert.AreEqual(A, TeamPalette.Of(new[] { A, B }, 0, Unknown));
            Assert.AreEqual(B, TeamPalette.Of(new[] { A, B }, 1, Unknown));
        }

        [Test]
        public void AnUnknownTeamGetsTheNeutralNeverTeamZeros()
        {
            Assert.AreEqual(Unknown, TeamPalette.Of(new[] { A, B }, -1, Unknown), "nobody");
            Assert.AreEqual(Unknown, TeamPalette.Of(new[] { A, B }, 2, Unknown), "past the list, never its last entry");
            Assert.AreEqual(Unknown, TeamPalette.Of(null, 0, Unknown), "no list");
            Assert.AreEqual(Unknown, TeamPalette.Of(new Color[0], 0, Unknown), "an empty list");
        }

        // Named per screen: a call anywhere else in the class (a second picker kept while another went back to an inline clamp) does not count.
        [TestCase(typeof(BreakCard), "Pick")]
        [TestCase(typeof(RoundHud), "Pick")]
        [TestCase(typeof(ScoreBars), "Pick")]
        [TestCase(typeof(DominionResultPanel), "Pick")]
        [TestCase(typeof(CentrePayoutLabel), "Pick")]
        [TestCase(typeof(DeathCard), "TeamTextColour")]
        [TestCase(typeof(LobbyRoomPanel), "TeamColour")]
        [TestCase(typeof(UiTheme), nameof(UiTheme.ShotColorFor))]
        [TestCase(typeof(Overpower.Arena.LookColours), nameof(Overpower.Arena.LookColours.Accent))]
        public void EveryScreenThatColoursATeamAsksThePalette(System.Type screen, string picker)
        {
            var of = typeof(TeamPalette).GetMethod(nameof(TeamPalette.Of));
            Assert.IsTrue(IlWiring.Uses(screen, picker, of), screen.Name + "." + picker + " picks its team colours through TeamPalette.Of");
        }

        [Test]
        public void TheShotTrailCacheSharesThePalettesTeamTest()
        {
            Assert.IsTrue(TeamPalette.Holds(new[] { A, B }, 1));
            Assert.IsFalse(TeamPalette.Holds(new[] { A, B }, 2));
            Assert.IsFalse(TeamPalette.Holds(null, 0));
            Assert.IsTrue(IlWiring.Uses(typeof(UiTheme), nameof(UiTheme.GradientFor), typeof(TeamPalette).GetMethod(nameof(TeamPalette.Holds))),
                "an unknown team shares the unknown trail, as ShotColorFor gives it the unknown colour");
        }

        [TestCase(typeof(BreakCard), "TextColour")]
        [TestCase(typeof(RoundHud), "TextColour")]
        public void EveryTeamColourHelperGoesThroughThePicker(System.Type screen, string helper)
        {
            var pick = screen.GetMethod("Pick", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pick, screen.Name + " has its Pick");
            Assert.IsTrue(IlWiring.Uses(screen, helper, pick), screen.Name + "." + helper + " goes through Pick");
        }
    }
}
