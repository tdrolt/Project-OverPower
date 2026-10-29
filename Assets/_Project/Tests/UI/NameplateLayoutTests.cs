using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Where the plate over a player's head puts its name, bar and the rows above it; the name's colour; which status label
    /// shows. Literal values only; no asset numbers.</summary>
    public class NameplateLayoutTests
    {
        private static NameplateLayout.Settings S() => new NameplateLayout.Settings
        {
            nameSize = 4f,
            barSize = new Vector2(20f, 3f),
            padding = new Vector2(1f, 0.5f),
            rowGap = 0.5f,
            maxWidth = 30f,
            bottomY = -2f,
            edge = 0.25f,
            statusTextSize = 3f,
            statusGap = 1f,
            statusBarSize = new Vector2(20f, 1f),
            statusLabelGap = 0.5f,
            wastedTextSize = 3f,
            markSize = 2f,
            ownRowGap = 0.5f,
        };

        [Test]
        public void AShortNameLeavesThePlateAsWideAsItsBarPlusPadding()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(S(), 5f, true);
            Assert.AreEqual(22f, f.plate.width, 1e-4f);
            Assert.AreEqual(20f, f.bar.width, 1e-4f);
        }

        [Test]
        public void ALongNameWidensThePlateButNotTheBar()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(S(), 25f, true);
            Assert.AreEqual(27f, f.plate.width, 1e-4f);
            Assert.AreEqual(25f, f.name.width, 1e-4f);
            Assert.AreEqual(20f, f.bar.width, 1e-4f, "every bar is the same width so health compares at a glance");
        }

        [Test]
        public void AVeryLongNameStopsAtTheWidestPlateAndIsCutInsideIt()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(S(), 40f, true);
            Assert.AreEqual(30f, f.plate.width, 1e-4f);
            Assert.AreEqual(28f, f.name.width, 1e-4f);
        }

        [Test]
        public void TheWidestPlateNeverCutsTheBar()
        {
            NameplateLayout.Settings s = S();
            s.maxWidth = 10f;
            Assert.AreEqual(22f, NameplateLayout.Compute(s, 40f, true).plate.width, 1e-4f);
        }

        [Test]
        public void EverythingIsCentredOnThePlayer()
        {
            foreach (float width in new[] { 0f, 25f, 40f })
            {
                NameplateLayout.Frame f = NameplateLayout.Compute(S(), width, true);
                Assert.AreEqual(0f, f.plate.center.x, 1e-4f);
                Assert.AreEqual(0f, f.name.center.x, 1e-4f);
                Assert.AreEqual(0f, f.bar.center.x, 1e-4f);
                Assert.AreEqual(0f, f.statusBar.center.x, 1e-4f);
                Assert.AreEqual(0f, f.statusLabel.center.x, 1e-4f);
                Assert.AreEqual(0f, f.wasted.center.x, 1e-4f);
            }
        }

        [Test]
        public void TheNameSitsAboveTheBarBothInsideThePadding()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(S(), 5f, true);
            Assert.AreEqual(-2f, f.plate.yMin, 1e-4f, "the plate's bottom sits at its set height");
            Assert.AreEqual(-1.5f, f.bar.yMin, 1e-4f);
            Assert.AreEqual(3f, f.bar.height, 1e-4f);
            Assert.AreEqual(f.bar.yMax + 0.5f, f.name.yMin, 1e-4f);
            Assert.AreEqual(4f, f.name.height, 1e-4f);
            Assert.AreEqual(f.name.yMax + 0.5f, f.plate.yMax, 1e-4f);
            Assert.AreEqual(6.5f, f.plate.yMax, 1e-4f);
        }

        [Test]
        public void WithoutTheBarThePlateShrinksToTheNameAndNothingAboveMoves()
        {
            NameplateLayout.Frame alive = NameplateLayout.Compute(S(), 5f, true);
            NameplateLayout.Frame dead = NameplateLayout.Compute(S(), 5f, false);
            Assert.AreEqual(alive.name, dead.name);
            Assert.AreEqual(alive.plate.yMax, dead.plate.yMax, 1e-4f);
            Assert.AreEqual(alive.name.yMin - 0.5f, dead.plate.yMin, 1e-4f);
            Assert.AreEqual(alive.statusBar, dead.statusBar);
            Assert.AreEqual(alive.statusLabel, dead.statusLabel);
            Assert.AreEqual(alive.wasted, dead.wasted);
        }

        [Test]
        public void TheStatusBarItsLabelAndTheWastedTextStackAboveThePlate()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(S(), 5f, true);
            Assert.AreEqual(f.plate.yMax + 1f, f.statusBar.yMin, 1e-4f);
            Assert.AreEqual(1f, f.statusBar.height, 1e-4f);
            Assert.AreEqual(20f, f.statusBar.width, 1e-4f);
            Assert.AreEqual(f.statusBar.yMax + 0.5f, f.statusLabel.yMin, 1e-4f);
            Assert.AreEqual(3f, f.statusLabel.height, 1e-4f);
            Assert.AreEqual(f.statusLabel.yMax + 1f, f.wasted.yMin, 1e-4f);
            Assert.AreEqual(3f, f.wasted.height, 1e-4f);
        }

        [Test]
        public void TheRowsAboveThePlateDoNotDependOnTheName()
        {
            NameplateLayout.Frame none = NameplateLayout.Compute(S(), 0f, true);
            NameplateLayout.Frame longName = NameplateLayout.Compute(S(), 40f, true);
            Assert.AreEqual(none.statusBar, longName.statusBar);
            Assert.AreEqual(none.statusLabel, longName.statusLabel);
            Assert.AreEqual(none.wasted, longName.wasted);
        }

        [Test]
        public void TheEdgeThicknessIsHandedThrough()
        {
            Assert.AreEqual(0.25f, NameplateLayout.Compute(S(), 5f, true).edge, 1e-4f);
        }

        [Test]
        public void TheArmourMarkIsCentredOnTheBar()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(S(), 5f, true);
            Assert.AreEqual(f.bar.center, f.marks.center);
            Assert.AreEqual(2f, f.marks.width, 1e-4f);
            Assert.AreEqual(2f, f.marks.height, 1e-4f);
        }

        [Test]
        public void TheNameNoLongerMakesRoomForMarks()
        {
            Assert.AreEqual(22f, NameplateLayout.Compute(S(), 16f, true).plate.width, 1e-4f);
            Assert.AreEqual(0f, NameplateLayout.Compute(S(), 16f, true).name.center.x, 1e-4f);
        }

        [Test]
        public void ACorpseHasNoMarkRoom() =>
            Assert.AreEqual(0f, NameplateLayout.Compute(S(), 16f, false).marks.width, 1e-4f);

        [Test]
        public void YourOwnPlateHasTheShieldRowOverTheHealthRowAndNoName()
        {
            NameplateLayout.Frame other = NameplateLayout.Compute(S(), 5f, true);
            NameplateLayout.Frame own = NameplateLayout.Compute(S(), 5f, true, true);
            Assert.AreEqual(0f, own.name.width, 1e-4f, "no name");
            Assert.AreEqual(other.plate.yMax, own.plate.yMax, 1e-4f, "same top, so the status rows above line up");
            Assert.AreEqual(22f, own.plate.width, 1e-4f, "as wide as the bar plus padding");
            Assert.AreEqual(own.plate.yMax - 0.5f, own.shieldRow.yMax, 1e-4f);
            Assert.AreEqual(3f, own.shieldRow.height, 1e-4f);
            Assert.AreEqual(3f, own.healthRow.height, 1e-4f);
            Assert.AreEqual(own.shieldRow.yMin - 0.5f, own.healthRow.yMax, 1e-4f, "the row gap between them");
            Assert.AreEqual(own.healthRow.yMin - 0.5f, own.plate.yMin, 1e-4f);
            Assert.AreEqual(own.healthRow.yMin, own.bar.yMin, 1e-4f);
            Assert.AreEqual(own.shieldRow.yMax, own.bar.yMax, 1e-4f);
            Assert.AreEqual(own.shieldRow.center, own.marks.center, "the mark sits on the shield");
            Assert.AreEqual(other.statusBar, own.statusBar);
            Assert.AreEqual(other.wasted, own.wasted);
        }

        [Test]
        public void AnyoneElsesPlateKeepsOneBarForBoth()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(S(), 5f, true);
            Assert.AreEqual(f.bar, f.shieldRow);
            Assert.AreEqual(f.bar, f.healthRow);
        }

        [Test]
        public void YourOwnCorpseHasAnEmptyPlate()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(S(), 5f, false, true);
            Assert.AreEqual(0f, f.plate.height, 1e-4f);
            Assert.AreEqual(0f, f.marks.width, 1e-4f);
        }

        [Test]
        public void TheSettingsAreTheThemesNumbers()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            try
            {
                theme.nameplateNameSize = 1.1f;
                theme.nameplateBarSize = new Vector2(1.2f, 1.3f);
                theme.nameplatePadding = new Vector2(1.4f, 1.5f);
                theme.nameplateRowGap = 1.6f;
                theme.nameplateMaxWidth = 1.7f;
                theme.nameplateBottomY = 1.8f;
                theme.nameplateEdgeThickness = 1.9f;
                theme.nameplateStatusTextSize = 2.1f;
                theme.nameplateStatusGap = 2.2f;
                theme.nameplateStatusBarSize = new Vector2(2.3f, 2.4f);
                theme.nameplateStatusLabelGap = 2.5f;
                theme.shieldWastedTextSize = 2.6f;
                theme.nameplateArmourMarkSize = 2.7f;
                theme.nameplateOwnRowGap = 2.8f;

                NameplateLayout.Settings s = NameplateLayout.From(theme);
                Assert.AreEqual(1.1f, s.nameSize);
                Assert.AreEqual(new Vector2(1.2f, 1.3f), s.barSize);
                Assert.AreEqual(new Vector2(1.4f, 1.5f), s.padding);
                Assert.AreEqual(1.6f, s.rowGap);
                Assert.AreEqual(1.7f, s.maxWidth);
                Assert.AreEqual(1.8f, s.bottomY);
                Assert.AreEqual(1.9f, s.edge);
                Assert.AreEqual(2.1f, s.statusTextSize);
                Assert.AreEqual(2.2f, s.statusGap);
                Assert.AreEqual(new Vector2(2.3f, 2.4f), s.statusBarSize);
                Assert.AreEqual(2.5f, s.statusLabelGap);
                Assert.AreEqual(2.6f, s.wastedTextSize);
                Assert.AreEqual(2.7f, s.markSize);
                Assert.AreEqual(2.8f, s.ownRowGap);
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        private static readonly Color Self = new Color(0.9f, 0.9f, 0.9f, 1f);
        private static readonly Color Mate = new Color(0.1f, 0.8f, 0.1f, 1f);
        private static readonly Color Enemy = new Color(0.8f, 0.1f, 0.1f, 1f);
        private static readonly Color Unknown = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Test]
        public void OnlyYourOwnPlateSplitsItsBars()
        {
            Assert.IsTrue(NameplateRules.ShowsOwnBars(true));
            Assert.IsFalse(NameplateRules.ShowsOwnBars(false));
        }

        [Test]
        public void YourOwnNameIsYourColourEvenBeforeTheTeamsArrive()
        {
            Assert.AreEqual(Self, NameplateRules.NameColour(true, -1, -1, Self, Mate, Enemy, Unknown));
            Assert.AreEqual(Self, NameplateRules.NameColour(true, 1, 2, Self, Mate, Enemy, Unknown));
        }

        [Test]
        public void AnotherPlayersNameIsGreyWhileEitherTeamIsMissing()
        {
            Assert.AreEqual(Unknown, NameplateRules.NameColour(false, -1, 1, Self, Mate, Enemy, Unknown));
            Assert.AreEqual(Unknown, NameplateRules.NameColour(false, 1, -1, Self, Mate, Enemy, Unknown));
            Assert.AreEqual(Unknown, NameplateRules.NameColour(false, -1, -1, Self, Mate, Enemy, Unknown),
                "two missing teams must not read as the same team");
        }

        [Test]
        public void ATeammatesNameAndAnEnemysNameDiffer()
        {
            Assert.AreEqual(Mate, NameplateRules.NameColour(false, 2, 2, Self, Mate, Enemy, Unknown));
            Assert.AreEqual(Enemy, NameplateRules.NameColour(false, 0, 2, Self, Mate, Enemy, Unknown));
        }

        [Test]
        public void ThePlatesEdgeIsThePlayersTeamColour()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            try
            {
                theme.teamShotColors = new[] { new Color(0.9f, 0.4f, 0.1f, 1f), new Color(0.5f, 0.2f, 0.9f, 1f) };
                theme.unknownTeamShotColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                Assert.AreEqual(new Color(0.5f, 0.2f, 0.9f, 1f), NameplateRules.EdgeColour(1, theme), "the team's own colour, whoever looks at it");
                Assert.AreEqual(new Color(0.9f, 0.4f, 0.1f, 1f), NameplateRules.EdgeColour(0, theme));
                Assert.AreEqual(new Color(0.4f, 0.4f, 0.4f, 0.5f), NameplateRules.EdgeColour(-1, theme), "the no-team grey while the team is still arriving");
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        private static readonly Color Stun = new Color(0.2f, 0.4f, 1f, 1f);
        private static readonly Color Slow = new Color(0.7f, 0.6f, 0.4f, 1f);

        [Test]
        public void NoStatusShowsNoLabel()
        {
            Assert.IsFalse(StatusLabelRule.Look(StatusLabel.None, "S1", Stun, "S2", Slow, out _, out _));
        }

        [Test]
        public void AStunShowsTheStunTextInTheStunColour()
        {
            Assert.IsTrue(StatusLabelRule.Look(StatusLabel.Stunned, "S1", Stun, "S2", Slow, out string text, out Color colour));
            Assert.AreEqual("S1", text);
            Assert.AreEqual(Stun, colour);
        }

        [Test]
        public void ASlowShowsTheSlowTextInTheSlowColour()
        {
            Assert.IsTrue(StatusLabelRule.Look(StatusLabel.Slowed, "S1", Stun, "S2", Slow, out string text, out Color colour));
            Assert.AreEqual("S2", text);
            Assert.AreEqual(Slow, colour);
        }
    }
}
