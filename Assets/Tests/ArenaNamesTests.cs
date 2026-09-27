using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>2026-09-27 designer change: names Tudor can actually read, instead of team/zone
    /// numbers. Pure mapping, no scene lookup - see ArenaNames' own class comment for where the arena
    /// layout facts (which zone is whose capital, which Tier III sits between which two teams) came
    /// from.</summary>
    public class ArenaNamesTests
    {
        [Test]
        public void TeamNamesMatchTheThreeTeamColours()
        {
            Assert.AreEqual("White", ArenaNames.TeamName(0));
            Assert.AreEqual("Purple", ArenaNames.TeamName(1));
            Assert.AreEqual("Cyan", ArenaNames.TeamName(2));
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
            Assert.AreEqual("White base (T1)", ArenaNames.ZoneName(6));
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
            StringAssert.Contains("White", ArenaNames.ZoneName(3));
            StringAssert.Contains("Purple", ArenaNames.ZoneName(3));
            StringAssert.Contains("Purple", ArenaNames.ZoneName(4));
            StringAssert.Contains("Cyan", ArenaNames.ZoneName(4));
            StringAssert.Contains("White", ArenaNames.ZoneName(5));
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
