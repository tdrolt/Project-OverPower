using System.Linq;
using NUnit.Framework;
using Overpower.Data;
using UnityEditor;

namespace Overpower.Tests
{
    public class GameModeCatalogueTests
    {
        private const string AssetPath = "Assets/Gameplay/Config/Modes/GameModeCatalogue.asset";

        private static GameModeCatalogue Load()
        {
            var c = AssetDatabase.LoadAssetAtPath<GameModeCatalogue>(AssetPath);
            Assert.IsNotNull(c, "the catalogue asset is missing at " + AssetPath);
            return c;
        }

        [Test]
        public void TheCatalogueHasModes() => Assert.Greater(Load().Modes.Count, 0);

        [Test]
        public void EveryModeIsSetAndIdsAreUnique()
        {
            var modes = Load().Modes;
            Assert.IsTrue(modes.All(m => m != null));
            Assert.AreEqual(modes.Count, modes.Select(m => m.Id).Distinct().Count());
        }

        [Test]
        public void ByIdFindsEachModeAndNullForUnknown()
        {
            var c = Load();
            foreach (var m in c.Modes) Assert.AreSame(m, c.ById(m.Id));
            Assert.IsNull(c.ById(-12345));
        }

        [Test]
        public void EveryAvailableModeHasTwoTeamsAndASeatPerTeam()
        {
            foreach (var m in Load().Modes.Where(m => m.Available))
            {
                Assert.GreaterOrEqual(m.Teams.Length, 2, m.DisplayName);
                Assert.GreaterOrEqual(m.SeatsPerTeam, 1, m.DisplayName);
            }
        }

        [Test]
        public void EveryTeamIdIsZeroToTwo()
        {
            foreach (var m in Load().Modes)
                foreach (var t in m.Teams)
                    Assert.That(t, Is.InRange(0, 2), m.DisplayName);
        }

        [Test]
        public void TheConquestModesHaveInfoCards()
        {
            var conquest = Load().Modes.Where(m => m.Family == GameModeFamily.Conquest).ToList();
            Assert.GreaterOrEqual(conquest.Count, 1);
            foreach (var m in conquest) Assert.Greater(m.InfoCards.Count, 0, m.DisplayName);
        }

        [Test]
        public void EveryModesLobbyModeValueIsItsTeamCount()
        {
            foreach (var m in Load().Modes)
                Assert.AreEqual(m.Teams.Length, m.LobbyModeValue, m.DisplayName);
        }

        [Test]
        public void TeamIdsAreDistinctWithinAMode()
        {
            foreach (var m in Load().Modes)
                Assert.AreEqual(m.Teams.Length, m.Teams.Distinct().Count(), m.DisplayName);
        }

        [Test]
        public void TheLayoutOwnsACopyOfTheTeams()
        {
            var mode = Load().Modes.First();
            int before = mode.Teams[0];
            var layout = Overpower.Lobby.SeatLayoutFactory.From(mode);
            layout.Teams[0] = before + 50;
            Assert.AreEqual(before, mode.Teams[0], "changing the layout must never change the asset");
            Assert.AreNotSame(mode.Teams, layout.Teams);
        }
    }
}
