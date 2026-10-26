using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Lobby;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The pure parts of the name screen, the lobby list and the create screen (lobby Task 9): the players text, the mode choice, the name prefill.</summary>
    public class LobbyScreenRulesTests
    {
        // ---- the players text on a list row: "5 / 9 +1 spec"

        [TestCase(2, 9, 1, 4, 12, "2 / 9", "+1 spec")]
        [TestCase(0, 6, 0, 1, 9, "0 / 6", "")]
        [TestCase(9, 9, 3, 12, 12, "9 / 9", "+3 spec")]
        public void ThePlayersTextReadsTheRowsSeatCounts(int filled, int teamSeats, int watching, int playerCount, int maxPlayers, string main, string spectators)
        {
            var entry = new LobbyEntry { FillKnown = true, FilledTeamSeats = filled, TeamSeats = teamSeats, FilledSpectatorSeats = watching, PlayerCount = playerCount, MaxPlayers = maxPlayers };
            LobbyScreenRules.PlayersPartsOf(entry, out string gotMain, out string gotSpec);
            Assert.AreEqual(main, gotMain);
            Assert.AreEqual(spectators, gotSpec);
            Assert.AreEqual(spectators.Length == 0 ? main : main + " " + spectators, LobbyScreenRules.PlayersText(entry));
        }

        [Test]
        public void TheBriefsExamples() =>
            Assert.AreEqual("2 / 9 +1 spec", LobbyScreenRules.PlayersText(new LobbyEntry { FillKnown = true, FilledTeamSeats = 2, TeamSeats = 9, FilledSpectatorSeats = 1, PlayerCount = 3, MaxPlayers = 12 }));

        [Test]
        public void WithoutAReadableFillTheRoomsOwnCountsAreShown() =>
            Assert.AreEqual("3 / 9", LobbyScreenRules.PlayersText(new LobbyEntry { FillKnown = false, PlayerCount = 3, MaxPlayers = 9 }));

        [Test]
        public void ARowReadsItsParsedSeatCounts()
        {
            var known = new LobbyEntry { FillKnown = true, FilledTeamSeats = 5, TeamSeats = 9, FilledSpectatorSeats = 1, PlayerCount = 6, MaxPlayers = 12 };
            Assert.AreEqual("5 / 9 +1 spec", LobbyScreenRules.PlayersText(known));
            var unknown = new LobbyEntry { FillKnown = false, PlayerCount = 2, MaxPlayers = 9 };
            Assert.AreEqual("2 / 9", LobbyScreenRules.PlayersText(unknown));
        }

        // ---- the create screen's mode choice

        private static GameModeDefinition Mode(int id, GameModeFamily family, string size, bool available)
        {
            var mode = ScriptableObject.CreateInstance<GameModeDefinition>();
            void Set(string field, object value) =>
                typeof(GameModeDefinition).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode, value);
            Set("id", id);
            Set("family", family);
            Set("familyName", family.ToString());
            Set("sizeName", size);
            Set("available", available);
            return mode;
        }

        private List<GameModeDefinition> made;

        [SetUp]
        public void SetUp()
        {
            made = new List<GameModeDefinition>
            {
                Mode(1, GameModeFamily.Conquest, "3v3v3", true),
                Mode(2, GameModeFamily.Conquest, "3v3", true),
                Mode(3, GameModeFamily.Dominion, "2v2", false),
                Mode(4, GameModeFamily.Dominion, "3v3v3", false),
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var m in made) Object.DestroyImmediate(m);
        }

        [Test]
        public void TheFamiliesAreInCatalogueOrderWithoutRepeats() =>
            CollectionAssert.AreEqual(new[] { GameModeFamily.Conquest, GameModeFamily.Dominion }, LobbyScreenRules.Families(made));

        [Test]
        public void TheSizesOfAFamilyAreItsModesInCatalogueOrder()
        {
            CollectionAssert.AreEqual(new[] { made[0], made[1] }, LobbyScreenRules.SizesOf(made, GameModeFamily.Conquest));
            CollectionAssert.AreEqual(new[] { made[2], made[3] }, LobbyScreenRules.SizesOf(made, GameModeFamily.Dominion));
        }

        [Test]
        public void OnlyAnAvailableModeCanBeSelected()
        {
            Assert.IsTrue(LobbyScreenRules.IsSelectable(made[0]));
            Assert.IsFalse(LobbyScreenRules.IsSelectable(made[2]));
            Assert.IsFalse(LobbyScreenRules.IsSelectable(null));
        }

        [Test]
        public void TheDefaultSelectionIsTheFirstAvailableMode()
        {
            Assert.AreSame(made[0], LobbyScreenRules.DefaultMode(made));
            made[0].GetType().GetField("available", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(made[0], false);
            Assert.AreSame(made[1], LobbyScreenRules.DefaultMode(made), "the first one is not available: the next one is");
        }

        [Test]
        public void NothingAvailableMeansNoDefault()
        {
            foreach (var m in made) m.GetType().GetField("available", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(m, false);
            Assert.IsNull(LobbyScreenRules.DefaultMode(made));
            Assert.IsNull(LobbyScreenRules.DefaultMode(null));
        }

        [Test]
        public void AFamilyWithOnlyUnavailableModesCannotBeChosen()
        {
            Assert.IsTrue(LobbyScreenRules.FamilyIsAvailable(made, GameModeFamily.Conquest));
            Assert.IsFalse(LobbyScreenRules.FamilyIsAvailable(made, GameModeFamily.Dominion));
            Assert.AreSame(made[1], LobbyScreenRules.ChooseFamily(made, GameModeFamily.Dominion, made[1]), "choosing Dominion changes nothing");
        }

        [Test]
        public void ChoosingTheFamilyYouAreInKeepsYourSizeElseTakesItsFirstAvailable()
        {
            Assert.AreSame(made[1], LobbyScreenRules.ChooseFamily(made, GameModeFamily.Conquest, made[1]));
            // switching from Dominion's (unavailable) size is not reachable, but the rule still falls back to the first available one
            Assert.AreSame(made[0], LobbyScreenRules.ChooseFamily(made, GameModeFamily.Conquest, null));
        }

        [Test]
        public void ChoosingASizeChangesOnlyWhenItIsAvailable()
        {
            Assert.AreSame(made[1], LobbyScreenRules.ChooseSize(made[1], made[0]));
            Assert.AreSame(made[0], LobbyScreenRules.ChooseSize(made[2], made[0]), "Dominion 2v2 is not available: the choice stays");
        }

        // ---- the lobby name prefill

        [TestCase("Tudor", "{0}'s lobby", 24, "Tudor's lobby")]
        [TestCase("Tudor", "{0}'s lobby", 8, "Tudor's")]
        [TestCase("Tudor", "{0}'s lobby", 7, "Tudor's")]
        [TestCase("Tudor", "{0}'s lobby", 6, "Tudor'")]
        [TestCase("Ab", "{0} lobby", 4, "Ab l")]
        [TestCase("Ab", "{0} lobby", 3, "Ab")]
        public void ThePrefillIsTheNamePlusTheFormatCutToTheMaximumWithoutATrailingSpace(string player, string format, int max, string expected) =>
            Assert.AreEqual(expected, LobbyScreenRules.LobbyNamePrefill(player, format, max));

        [Test]
        public void AnEmptyPlayerNameStillGivesAnUsableName() =>
            Assert.AreEqual("Lobby", LobbyScreenRules.LobbyNamePrefill("", "{0}'s lobby", 24, "Lobby"));
    }
}
