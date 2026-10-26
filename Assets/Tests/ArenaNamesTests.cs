using NUnit.Framework;
using UnityEngine;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>2026-09-27 designer change: names Tudor can actually read, instead of team/zone
    /// numbers. Pure mapping, no scene lookup - see ArenaNames' own class comment for where the arena
    /// layout facts (which zone is whose capital, which Tier III sits between which two teams) came
    /// from.</summary>
    public class ArenaNamesTests
    {
        // The report is read next to the game: a team carries the name and the colour the scoreboard gives it.
        [Test]
        public void TeamNamesAreTheGamesOwnNames()
        {
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            for (int team = 0; team < 3; team++)
                Assert.AreEqual(theme.scoreboardTeamNames[team], ArenaNames.TeamName(team), "team " + team);
        }

        [Test]
        public void TheReportDrawsEachTeamInTheGamesColour()
        {
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            string source = System.IO.File.ReadAllText("Assets/_Project/Code/Editor/Telemetry/HtmlReportWriter.cs");
            var list = System.Text.RegularExpressions.Regex.Match(source, @"var TEAM_COLORS = \[([^\]]*)\]");
            Assert.IsTrue(list.Success, "the report's team colour list");
            var hexes = System.Text.RegularExpressions.Regex.Matches(list.Groups[1].Value, "#([0-9a-fA-F]{6})");
            for (int team = 0; team < 3; team++)
                Assert.AreEqual(ColorUtility.ToHtmlStringRGB(theme.teamShotColors[team]), hexes[team].Groups[1].Value.ToUpperInvariant(), "team " + team);
        }

        [Test]
        public void AnUnknownTeamIdFallsBackToAPlainLabel()
        {
            Assert.AreEqual("Team -1", ArenaNames.TeamName(-1));
            Assert.AreEqual("Team 5", ArenaNames.TeamName(5));
        }

        [Test]
        public void EveryZoneIdZeroToNineGetsAUniqueNonEmptyName()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int zone = 0; zone <= 9; zone++)
            {
                string name = ArenaNames.ZoneName(zone);
                Assert.IsNotEmpty(name, "zone " + zone + " must have a non-empty name");
                Assert.IsTrue(seen.Add(name), "zone " + zone + " produced a duplicate name: " + name);
            }
        }

        [Test]
        public void TheThreeCapitalsAreNamedColourBase()
        {
            Assert.AreEqual(ArenaNames.TeamName(0) + " base (T1)", ArenaNames.ZoneName(6));
            Assert.AreEqual("Purple base (T1)", ArenaNames.ZoneName(7));
            Assert.AreEqual("Cyan base (T1)", ArenaNames.ZoneName(8));
        }

        [Test]
        public void TheCentreIsNamedCentre()
        {
            Assert.AreEqual("Centre (T4)", ArenaNames.ZoneName(9));
        }

        [Test]
        public void ATierThreeZoneNamesBothTeamsItSitsBetween()
        {
            StringAssert.Contains(ArenaNames.TeamName(0), ArenaNames.ZoneName(3));
            StringAssert.Contains("Purple", ArenaNames.ZoneName(3));
            StringAssert.Contains("Purple", ArenaNames.ZoneName(4));
            StringAssert.Contains("Cyan", ArenaNames.ZoneName(4));
            StringAssert.Contains(ArenaNames.TeamName(0), ArenaNames.ZoneName(5));
            StringAssert.Contains("Cyan", ArenaNames.ZoneName(5));
        }

        [Test]
        public void AnUnknownZoneIdFallsBackToAPlainLabel()
        {
            Assert.AreEqual("Zone 42", ArenaNames.ZoneName(42));
            Assert.AreEqual("Zone -1", ArenaNames.ZoneName(-1));
        }
    }
}
