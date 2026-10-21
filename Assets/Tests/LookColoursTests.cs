using NUnit.Framework;
using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Each colour family reads from exactly one UiTheme field. The sentinels are made-up so the tests check where a colour
    /// comes from, not what it is tuned to.</summary>
    public class LookColoursTests
    {
        private static readonly Color Team0 = new Color(0.11f, 0.12f, 0.13f, 1f);
        private static readonly Color Team1 = new Color(0.21f, 0.22f, 0.23f, 1f);
        private static readonly Color Gold = new Color(0.31f, 0.32f, 0.33f, 1f);
        private static readonly Color Stunned = new Color(0.41f, 0.42f, 0.43f, 1f);
        private static readonly Color Silver = new Color(0.51f, 0.52f, 0.53f, 1f);
        private static readonly Color FireCore = new Color(0.61f, 0.62f, 0.63f, 1f);
        private static readonly Color FireEdge = new Color(0.71f, 0.72f, 0.73f, 1f);
        private static readonly Color Danger = new Color(0.81f, 0.82f, 0.83f, 1f);
        private static readonly Color White = new Color(0.91f, 0.92f, 0.93f, 1f);

        private UiTheme theme;

        [SetUp]
        public void CreateTheme()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.teamShotColors = new[] { Team0, Team1 };
            theme.immuneBarColor = Gold;
            theme.statusStunnedColor = Stunned;
            theme.movementSilverColor = Silver;
            theme.fireCoreColor = FireCore;
            theme.fireEdgeColor = FireEdge;
            theme.dangerColor = Danger;
            theme.flashWhiteColor = White;
        }

        [TearDown]
        public void DestroyTheme() => Object.DestroyImmediate(theme);

        [Test] public void TeamIsShotColorFor()
        {
            Assert.AreEqual(Team1, LookColours.For(LookFamily.Team, 1, theme));
            Assert.AreEqual(theme.ShotColorFor(-1), LookColours.For(LookFamily.Team, -1, theme), "an unknown team falls back like a shot does");
        }

        [Test] public void GoldIsImmuneBarColor() => Assert.AreEqual(Gold, LookColours.For(LookFamily.Gold, 0, theme));

        [Test] public void ElectricBlueIsTheStunnedColour() => Assert.AreEqual(Stunned, LookColours.For(LookFamily.ElectricBlue, 0, theme));

        [Test] public void SilverDangerAndWhiteEachReadTheirOwnField()
        {
            Assert.AreEqual(Silver, LookColours.For(LookFamily.Silver, 0, theme));
            Assert.AreEqual(Danger, LookColours.For(LookFamily.Danger, 0, theme));
            Assert.AreEqual(White, LookColours.For(LookFamily.White, 0, theme));
        }

        [Test] public void FireIsTheEdgeColourAndItsCoreIsAskedForSeparately()
        {
            Assert.AreEqual(FireEdge, LookColours.For(LookFamily.Fire, 0, theme));
            Assert.AreEqual(FireCore, LookColours.FireCore(theme));
        }

        [Test] public void NoFamilyDrawsNothing() => Assert.AreEqual(0f, LookColours.For(LookFamily.None, 0, theme).a);

        [Test] public void EveryFamilyButNoneDrawsSomething()
        {
            foreach (LookFamily family in System.Enum.GetValues(typeof(LookFamily)))
            {
                if (family == LookFamily.None) continue;
                Assert.Greater(LookColours.For(family, 0, theme).a, 0f, family + " must give a visible colour");
            }
        }
    }
}
