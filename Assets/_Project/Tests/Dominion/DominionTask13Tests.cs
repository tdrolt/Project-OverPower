using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Lobby;
using Photon.Pun;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Task 13: both Dominion modes can be created (available, with their info cards), the match log tells the story round by round (the notes of
    /// the round markers and the rule that decides which markers an edge of the room drops), and the log folder carries the mode's name.
    /// </summary>
    public class DominionTask13Tests
    {
        private const string ModesFolder = "Assets/Gameplay/Config/Modes/";
        private static GameModeDefinition Mode(string name)
        {
            var mode = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(ModesFolder + name + ".asset");
            Assert.IsNotNull(mode, name);
            return mode;
        }

        // The titles of the designer's cards, in the order of his notes ("Dominion info cards" in lobby-dominion-design-notes.md).
        private static readonly string[] CardTitles2v2 = { "Rounds", "Zones and points", "Bounties", "Your build each round", "Respawns", "Sudden death" };
        private static readonly string[] CardTitles3v3v3 = { "Rounds", "Zones and points", "The centre", "Bounties", "Your build each round", "Respawns", "Sudden death" };

        // ---------------------------------------------------------------- the modes are on, with their cards

        [Test] public void BothDominionModesCanBeCreated()
        {
            foreach (string name in new[] { "Dominion 2v2", "Dominion 3v3v3" })
            {
                GameModeDefinition mode = Mode(name);
                Assert.IsTrue(mode.Available, name);
                Assert.IsTrue(LobbyScreenRules.IsSelectable(mode), name + ": the create screen lets a player pick it");
                Assert.AreEqual(GameModeFamily.Dominion, mode.Family);
            }
        }

        [Test] public void TheDominionCardsCarryTheDesignersTitlesInOrder()
        {
            CollectionAssert.AreEqual(CardTitles2v2, Mode("Dominion 2v2").InfoCards.Select(c => c.title).ToArray());
            CollectionAssert.AreEqual(CardTitles3v3v3, Mode("Dominion 3v3v3").InfoCards.Select(c => c.title).ToArray());
        }

        [Test] public void OnlyTheThreeTeamModeHasTheCentreCard()
        {
            Assert.IsFalse(Mode("Dominion 2v2").InfoCards.Any(c => c.title == "The centre"), "2v2 has no centre");
            Assert.IsTrue(Mode("Dominion 3v3v3").InfoCards.Any(c => c.title == "The centre"));
        }

        [Test] public void EveryDominionCardHasATextAndAnOpaqueAccent()
        {
            foreach (string name in new[] { "Dominion 2v2", "Dominion 3v3v3" })
                foreach (GameModeDefinition.InfoCard card in Mode(name).InfoCards)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(card.text), name + " / " + card.title);
                    Assert.AreEqual(1f, card.accent.a, 0.001f, name + " / " + card.title + ": an accent with no alpha would draw no edge");
                }
        }

        [Test] public void TheZonesCardOfTheThreeTeamModeLeavesOutTheCentre()
        {
            string two = Mode("Dominion 2v2").InfoCards.First(c => c.title == "Zones and points").text;
            string three = Mode("Dominion 3v3v3").InfoCards.First(c => c.title == "Zones and points").text;
            StringAssert.DoesNotContain("centre", two);
            StringAssert.Contains("apart from the centre", three);
            StringAssert.Contains("can't be captured", two, "both keep the sentence about the spawn");
            StringAssert.Contains("can't be captured", three);
        }

        [Test] public void TheMatchLogFolderOfADominionMatchNamesTheMode()
        {
            var when = new System.DateTime(2026, 10, 5, 10, 10, 0);
            Assert.AreEqual("2026-10-05_1010_Dominion-2v2_Tudors-lobby", MatchFolderName.For(when, Mode("Dominion 2v2").DisplayName, "Tudor's lobby", n => false));
            StringAssert.Contains("_Dominion-3v3v3_", MatchFolderName.For(when, Mode("Dominion 3v3v3").DisplayName, "x", n => false));
        }

        // ---------------------------------------------------------------- the notes

        [Test] public void TheRoundNotesReadAsTheStoryOfTheMatch()
        {
            Assert.AreEqual("dominion round 2 start", DominionMarkerNotes.RoundStart(2));
            Assert.AreEqual("dominion round 1 end winner team 0 points team0 120 team1 80", DominionMarkerNotes.RoundEnd(1, 0, new[] { 0, 1 }, new[] { 120, 80, 0 }));
            Assert.AreEqual("dominion round 3 end tied points team0 100 team1 100 team2 100", DominionMarkerNotes.RoundEnd(3, -1, new[] { 0, 1, 2 }, new[] { 100, 100, 100 }));
            Assert.AreEqual("dominion break start before round 2", DominionMarkerNotes.BreakStart(2));
            Assert.AreEqual("dominion sudden death start teams 0,2", DominionMarkerNotes.SuddenDeathStart(new[] { 0, 2 }));
            Assert.AreEqual("dominion sudden death replay teams 1", DominionMarkerNotes.SuddenDeathReplay(new[] { 1 }));
            Assert.AreEqual("dominion match over winner team 1 wins team0 1 team1 2", DominionMarkerNotes.MatchOver(1, new[] { 0, 1 }, new[] { 1, 2, 0 }));
        }

        [Test] public void APointsListNamesOnlyTheTeamsOfTheMatchAndSurvivesShortArrays()
        {
            Assert.AreEqual("dominion round 1 end winner team 1 points team0 5 team1 9", DominionMarkerNotes.RoundEnd(1, 1, new[] { 0, 1 }, new[] { 5, 9, 700 }), "team 2 is not in a 2v2");
            Assert.AreEqual("dominion round 1 end tied points team0 0 team1 0", DominionMarkerNotes.RoundEnd(1, -1, new[] { 0, 1 }, new[] { 0 }), "a missing slot reads 0");
            Assert.AreEqual("dominion round 1 end tied points", DominionMarkerNotes.RoundEnd(1, -1, null, null));
        }

        // ---------------------------------------------------------------- which markers an edge drops

        private static DominionRoomState Room(int round, DominionStage stage, int[] points = null, int[] wins = null, int winner = -1, int sd = 0, int[] sdTeams = null, int[] historyWinners = null) =>
            new DominionRoomState { HasRound = true, Round = round, Stage = stage, Points = points ?? new int[3], Wins = wins ?? new int[3], Winner = winner, SuddenDeathMs = sd, SuddenDeathTeams = sdTeams, HistoryWinners = historyWinners };

        private static readonly int[] Two = { 0, 1 };
        private static readonly int[] Three = { 0, 1, 2 };

        [Test] public void TheMatchOpensWithTheBreakBeforeRoundOne()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 0, DominionStage.None, 0, Room(1, DominionStage.Break), Two, null);
            CollectionAssert.AreEqual(new[] { "dominion break start before round 1" }, notes);
        }

        [Test] public void TheBreakEndingStartsTheRound()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 2, DominionStage.Break, 0, Room(2, DominionStage.Round), Two, null);
            CollectionAssert.AreEqual(new[] { "dominion round 2 start" }, notes);
        }

        [Test] public void ARoundEndingWithAWinnerThenTheBreakOfTheNextRound()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 1, DominionStage.Round, 0, Room(2, DominionStage.Break, new[] { 120, 80, 0 }, new[] { 1, 0, 0 }), Two, new[] { 0, 0, 0 });
            CollectionAssert.AreEqual(new[] { "dominion round 1 end winner team 0 points team0 120 team1 80", "dominion break start before round 2" }, notes);
        }

        [Test] public void ATiedRoundIsNamedTied()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 1, DominionStage.Round, 0, Room(2, DominionStage.Break, new[] { 90, 90, 0 }), Two, null);
            Assert.AreEqual("dominion round 1 end tied points team0 90 team1 90", notes[0]);
        }

        [Test] public void TheLastRoundEndingInSuddenDeathNamesTheTeamsThatPlayIt()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 3, DominionStage.Round, 0,
                Room(3, DominionStage.SuddenDeath, new[] { 10, 20, 30 }, new[] { 1, 1, 1 }, sd: 5000, sdTeams: new[] { 0, 1, 2 }), Three, new[] { 1, 1, 0 });
            CollectionAssert.AreEqual(new[] { "dominion round 3 end winner team 2 points team0 10 team1 20 team2 30", "dominion sudden death start teams 0,1,2" }, notes);
        }

        [Test] public void ARoundEndingTheMatchNamesTheWinnerAndTheWins()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 2, DominionStage.Round, 0, Room(2, DominionStage.Over, new[] { 50, 40, 0 }, new[] { 2, 0, 0 }, winner: 0), Two, new[] { 1, 0, 0 });
            CollectionAssert.AreEqual(new[] { "dominion round 2 end winner team 0 points team0 50 team1 40", "dominion match over winner team 0 wins team0 2 team1 0" }, notes);
        }

        [Test] public void SuddenDeathEndingTheMatchDropsOnlyTheMatchOver()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 3, DominionStage.SuddenDeath, 5000, Room(3, DominionStage.Over, wins: new[] { 1, 1, 0 }, winner: 1, sd: 5000, sdTeams: new[] { 0, 1 }), Two, null);
            CollectionAssert.AreEqual(new[] { "dominion match over winner team 1 wins team0 1 team1 1" }, notes);
        }

        [Test] public void ANewCircleStartInsideSuddenDeathIsAReplay()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 3, DominionStage.SuddenDeath, 5000, Room(3, DominionStage.SuddenDeath, sd: 9000, sdTeams: new[] { 0, 2 }), Three, null);
            CollectionAssert.AreEqual(new[] { "dominion sudden death replay teams 0,2" }, notes);
        }

        [Test] public void NothingChangedDropsNothing()
        {
            CollectionAssert.IsEmpty(DominionMarkerNotes.ForEdge(true, 2, DominionStage.Round, 0, Room(2, DominionStage.Round), Two, null));
            CollectionAssert.IsEmpty(DominionMarkerNotes.ForEdge(true, 3, DominionStage.SuddenDeath, 5000, Room(3, DominionStage.SuddenDeath, sd: 5000), Two, null), "the same circle start is not a replay");
            CollectionAssert.IsEmpty(DominionMarkerNotes.ForEdge(true, 3, DominionStage.Over, 0, Room(3, DominionStage.Over, winner: 0), Two, null), "an Over that was already Over");
        }

        [Test] public void ARoundCutShortByTheLastTeamStandingIsNotGivenToThePointsLeader()
        {
            // A3: Round to Over with the wins unchanged. Team 0 leads on points, but nobody won the round.
            var notes = DominionMarkerNotes.ForEdge(true, 2, DominionStage.Round, 0, Room(2, DominionStage.Over, new[] { 50, 40, 0 }, new[] { 1, 0, 0 }, winner: 1), Two, new[] { 1, 0, 0 });
            CollectionAssert.AreEqual(new[] { "dominion round 2 end cut short points team0 50 team1 40", "dominion match over winner team 1 wins team0 1 team1 0" }, notes);
        }

        [Test] public void AFinalRoundEndedLevelWithOneWinsLeaderIsTiedNotCutShort()
        {
            // A7: the last round ends level on points; the wins leader takes the match. Wins are 1-0 before and after, exactly as in a cut-short round.
            var notes = DominionMarkerNotes.ForEdge(true, 2, DominionStage.Round, 0,
                Room(2, DominionStage.Over, new[] { 90, 90, 0 }, new[] { 1, 0, 0 }, winner: 0, historyWinners: new[] { 0, -1 }), Two, new[] { 1, 0, 0 });
            Assert.AreEqual("dominion round 2 end tied points team0 90 team1 90", notes[0]);
        }

        [Test] public void TheLastTeamStandingWriteIsCutShortFromTheSameRecordTheTableUses()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 2, DominionStage.Round, 0,
                Room(2, DominionStage.Over, new[] { 90, 90, 0 }, new[] { 1, 0, 0 }, winner: 0, historyWinners: new[] { 0, DominionHistory.CutShort }), Two, new[] { 1, 0, 0 });
            Assert.AreEqual("dominion round 2 end cut short points team0 90 team1 90", notes[0]);
            Assert.IsEmpty(DominionHistory.WinnersOfRound(new[] { 0, 0, 0, 90, 90, 0 }, new[] { 0, DominionHistory.CutShort }, 1), "and the table bolds nobody for it");
        }

        [Test] public void ARecordedWinnerNamesTheRoundsWinner()
        {
            var notes = DominionMarkerNotes.ForEdge(true, 1, DominionStage.Round, 0,
                Room(2, DominionStage.Break, new[] { 80, 120, 0 }, new[] { 0, 1, 0 }, historyWinners: new[] { 1 }), Two, new[] { 0, 0, 0 });
            Assert.AreEqual("dominion round 1 end winner team 1 points team0 80 team1 120", notes[0]);
        }

        [Test] public void TheDirectorHandsTheStoredPreviousWinsToTheMarkerRule()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "OnRoomPropertiesUpdate", typeof(DominionDirector).GetField("lastAppliedWins", flags)),
                "the edge handler reads the wins it stored at the last edge");
            Assert.AreEqual(7, typeof(DominionMarkerNotes).GetMethod(nameof(DominionMarkerNotes.ForEdge)).GetParameters().Length);
            Assert.IsFalse(typeof(DominionMarkerNotes).GetMethod(nameof(DominionMarkerNotes.ForEdge)).GetParameters()[6].IsOptional, "prevWins cannot be forgotten");
        }

        [Test] public void TiedIsOnlyForARoundThatWasScoredAsATie()
        {
            var tied = DominionMarkerNotes.ForEdge(true, 1, DominionStage.Round, 0, Room(2, DominionStage.Break, new[] { 90, 90, 0 }, new[] { 0, 0, 0 }), Two, new[] { 0, 0, 0 });
            Assert.AreEqual("dominion round 1 end tied points team0 90 team1 90", tied[0]);
            var tiedIntoSuddenDeath = DominionMarkerNotes.ForEdge(true, 3, DominionStage.Round, 0, Room(3, DominionStage.SuddenDeath, new[] { 90, 90, 0 }, new[] { 1, 1, 0 }, sd: 5000, sdTeams: new[] { 0, 1 }), Two, new[] { 1, 1, 0 });
            Assert.AreEqual("dominion round 3 end tied points team0 90 team1 90", tiedIntoSuddenDeath[0]);
        }

        [Test] public void TheWinnerOfARoundIsWhoseWinsWentUpNotWhoLedThePoints()
        {
            Assert.AreEqual(1, DominionMarkerNotes.TeamWhoseWinsWentUp(new[] { 1, 0, 0 }, new[] { 1, 1, 0 }));
            Assert.AreEqual(-1, DominionMarkerNotes.TeamWhoseWinsWentUp(new[] { 1, 0, 0 }, new[] { 1, 0, 0 }));
            Assert.AreEqual(0, DominionMarkerNotes.TeamWhoseWinsWentUp(null, new[] { 1, 0, 0 }), "no reading before reads as no wins");
            Assert.AreEqual(-1, DominionMarkerNotes.TeamWhoseWinsWentUp(new[] { 1 }, null));
            Assert.AreEqual("dominion round 2 end cut short points team0 5 team1 6", DominionMarkerNotes.RoundCutShort(2, Two, new[] { 5, 6, 9 }));
        }

        [Test] public void OnlyTheMasterDropsTheMarkers()
        {
            CollectionAssert.IsEmpty(DominionMarkerNotes.ForEdge(false, 1, DominionStage.Round, 0, Room(2, DominionStage.Break, new[] { 120, 80, 0 }), Two, null));
        }

        // ---------------------------------------------------------------- the game calls the rule

        [Test] public void TheDirectorDropsWhatTheRuleSaysWhenTheRoomChanges()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo forEdge = typeof(DominionMarkerNotes).GetMethod(nameof(DominionMarkerNotes.ForEdge));
            MethodInfo drop = typeof(Overpower.Telemetry.MatchTelemetry).GetMethod(nameof(Overpower.Telemetry.MatchTelemetry.DropMarker), flags);
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "OnRoomPropertiesUpdate", typeof(DominionDirector).GetMethod("DropRoundMarkers", flags)), "the room's edge handler calls the dropper");
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "DropRoundMarkers", forEdge), "which asks the rule");
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "DropRoundMarkers", drop), "and drops what it says");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(DominionDirector), "DropRoundMarkers", typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.IsMasterClient)).GetGetMethod()), "the master-only answer comes from the live master check");
            System.Reflection.FieldInfo wins = typeof(DominionDirector).GetField("lastAppliedWins", flags);
            Assert.IsNotNull(wins);
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "OnRoomPropertiesUpdate", wins), "the wins before the edge are read");
            Assert.IsTrue(IlWiring.Stores(typeof(DominionDirector), "OnRoomPropertiesUpdate", wins), "and the new ones kept");
            Assert.IsTrue(IlWiring.Stores(typeof(DominionDirector), "ReadWithoutReacting", wins));
        }

        [Test] public void TheDirectorTakesTheRoomAsItStandsWhenItsSceneStartsInsideTheRoom()
        {
            // The lane scene loads after the room was joined: no joined-room callback comes, so Start must read the room, or a rejoiner sees a fake edge from nothing.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "Start", typeof(DominionDirector).GetMethod("ReadWithoutReacting", flags)));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "OnJoinedRoom", typeof(DominionDirector).GetMethod("ReadWithoutReacting", flags)), "and the ordinary join still does");
        }
    }
}
