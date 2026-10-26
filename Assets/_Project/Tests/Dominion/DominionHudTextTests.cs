using NUnit.Framework;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 9: the words and numbers of the round bar, break card, centre label, sudden-death banner and result. Every format and
    /// number here is a literal made up for the test, never the UiTheme's or the DominionConfig's.</summary>
    public class DominionHudTextTests
    {
        private static readonly string[] Names = { "White", "Purple", "Cyan" };

        // ---- the clock and the countdown

        [Test] public void AClockReadsMinutesAndTwoDigitSeconds()
        {
            Assert.AreEqual("1:42", DominionHudText.Clock(102));
            Assert.AreEqual("0:58", DominionHudText.Clock(58));
            Assert.AreEqual("3:00", DominionHudText.Clock(180));
            Assert.AreEqual("0:05", DominionHudText.Clock(5));
            Assert.AreEqual("0:00", DominionHudText.Clock(-3), "a clock that ran past its end never shows a minus");
        }

        [Test] public void SecondsLeftRoundsUpSoTheLastSecondReadsOne()
        {
            Assert.AreEqual(14, DominionHudText.SecondsLeft(endMs: 14100, nowMs: 100));
            Assert.AreEqual(1, DominionHudText.SecondsLeft(endMs: 5001, nowMs: 5000), "1 ms left is still second 1");
            Assert.AreEqual(2, DominionHudText.SecondsLeft(endMs: 6001, nowMs: 5000));
            Assert.AreEqual(0, DominionHudText.SecondsLeft(endMs: 5000, nowMs: 5000), "at the end it is 0");
            Assert.AreEqual(0, DominionHudText.SecondsLeft(endMs: 4000, nowMs: 5000), "past the end never goes negative");
        }

        [Test] public void NoEndTimeOrNoServerClockMeansNoSecondsLeft()
        {
            Assert.AreEqual(0, DominionHudText.SecondsLeft(endMs: 0, nowMs: 5000), "sudden death has no clock");
            Assert.AreEqual(0, DominionHudText.SecondsLeft(endMs: 9000, nowMs: 0), "the server clock has not synced: show nothing rather than a guess");
        }

        [Test] public void SecondsLeftSurvivesTheIntWrap()
        {
            int now = unchecked(int.MaxValue - 500);
            int end = unchecked(now + 3000);   // wrapped to negative
            Assert.AreEqual(3, DominionHudText.SecondsLeft(end, now));
        }

        [Test] public void TheRoundLabelPutsTheRoundAndTheRoundCountIn() =>
            Assert.AreEqual("ROUND 2 OF 3", DominionHudText.RoundLabel("ROUND {0} OF {1}", 2, 3));

        // ---- the break card

        [Test] public void ASingleLeaderWinsTheRoundByNameInCapitals() =>
            Assert.AreEqual("PURPLE WINS", DominionHudText.BreakHeadline(1, Names, "{0} WINS", "TIED"));

        [Test] public void ATiedRoundSaysTied() =>
            Assert.AreEqual("TIED", DominionHudText.BreakHeadline(-1, Names, "{0} WINS", "TIED"));

        [Test] public void TheBreakCountdownIsSmallUntilTheLastFewSecondsThenBig()
        {
            Assert.AreEqual("ROUND 2 STARTS IN 14", DominionHudText.BreakCountdown("ROUND {0} STARTS IN {1}", "Round {0} starts in {1}…", 2, 14, 5, out bool big));
            Assert.IsFalse(big);
            Assert.AreEqual("ROUND 2 STARTS IN 6", DominionHudText.BreakCountdown("ROUND {0} STARTS IN {1}", "Round {0} starts in {1}…", 2, 6, 5, out big));
            Assert.IsFalse(big, "6 is still the small line");
            Assert.AreEqual("Round 2 starts in 5…", DominionHudText.BreakCountdown("ROUND {0} STARTS IN {1}", "Round {0} starts in {1}…", 2, 5, 5, out big));
            Assert.IsTrue(big, "the last five seconds read big");
            Assert.AreEqual("Round 2 starts in 1…", DominionHudText.BreakCountdown("ROUND {0} STARTS IN {1}", "Round {0} starts in {1}…", 2, 1, 5, out big));
            Assert.IsTrue(big);
        }

        [Test] public void ZeroSecondsLeftIsNeverTheBigCountdown() => Assert.IsFalse(DominionHudText.IsBigCountdown(0, 5), "0 is the round starting, not a number to read out");

        private static OpensTexts Texts() =>
            new OpensTexts("Round {0} opens: {1}", "Round 1: free abilities", "a weapon family", "a weapon upgrade", "one armor upgrade", "{0} armor upgrades", " and ", "nothing new");

        [Test] public void RoundTwoOpensAWeaponFamilyAndOneArmorUpgrade() =>
            Assert.AreEqual("Round 2 opens: <b>a weapon family</b> and <b>one armor upgrade</b>",
                DominionHudText.OpensLine(2, new[] { 0, 1, 2 }, new[] { 0, 1, 2 }, Texts()));

        [Test] public void RoundThreeOpensAWeaponUpgradeAndOneMoreArmorUpgrade() =>
            Assert.AreEqual("Round 3 opens: <b>a weapon upgrade</b> and <b>one armor upgrade</b>",
                DominionHudText.OpensLine(3, new[] { 0, 1, 2 }, new[] { 0, 1, 2 }, Texts()));

        [Test] public void RoundOneHasItsOwnLine() =>
            Assert.AreEqual("Round 1: free abilities", DominionHudText.OpensLine(1, new[] { 0, 1, 2 }, new[] { 0, 1, 2 }, Texts()));

        [Test] public void TheOpensLineFollowsTheTablesNotAFixedSentence()
        {
            Assert.AreEqual("Round 2 opens: <b>2 armor upgrades</b>", DominionHudText.OpensLine(2, new[] { 0, 0, 1 }, new[] { 0, 2, 2 }, Texts()), "only armour, two at once");
            Assert.AreEqual("Round 3 opens: nothing new", DominionHudText.OpensLine(3, new[] { 0, 1, 1 }, new[] { 0, 1, 1 }, Texts()));
            Assert.AreEqual("Round 4 opens: nothing new", DominionHudText.OpensLine(4, new[] { 0, 1, 2 }, new[] { 0, 1, 2 }, Texts()), "past the table the last entry carries on");
        }

        // ---- under the minimap

        [Test] public void TheCentreLineCountsDownToThePayout() =>
            Assert.AreEqual("CENTRE +200 IN 12", DominionHudText.CentreLine("CENTRE +{0} IN {1}", 200, 12));

        [Test] public void TheCentreSaysWhoHoldsItOrThatNobodyDoes()
        {
            Assert.AreEqual("Cyan holds it", DominionHudText.HolderLine(2, Names, "{0} holds it", "Nobody holds it"));
            Assert.AreEqual("Nobody holds it", DominionHudText.HolderLine(-1, Names, "{0} holds it", "Nobody holds it"));
        }

        [Test] public void TheShrinkLineCountsTheCircleDownAndGoesWhenItStops()
        {
            Assert.AreEqual("CIRCLE SHRINKS · 0:41", DominionHudText.ShrinkLine("CIRCLE SHRINKS · {0}", 40.2f));
            Assert.AreEqual("CIRCLE SHRINKS · 1:00", DominionHudText.ShrinkLine("CIRCLE SHRINKS · {0}", 60f));
            Assert.AreEqual("", DominionHudText.ShrinkLine("CIRCLE SHRINKS · {0}", 0f), "once it has stopped there is nothing to count");
        }

        [Test] public void ABrokenFormatShowsTheRawTextInsteadOfThrowing()
        {
            Assert.DoesNotThrow(() => DominionHudText.RoundLabel("ROUND {0} OF {1} {2}", 1, 3));
            Assert.AreEqual("ROUND {0} OF {1} {2}", DominionHudText.RoundLabel("ROUND {0} OF {1} {2}", 1, 3));
        }

        // ---- sudden-death banner

        [Test] public void TheBannerIsFullForTheFirstSecondsThenSmall()
        {
            Assert.IsTrue(DominionHudText.BannerIsFull(-3000, 8f), "still in the get-ready beat");
            Assert.IsTrue(DominionHudText.BannerIsFull(7999, 8f));
            Assert.IsFalse(DominionHudText.BannerIsFull(8000, 8f));
            Assert.IsFalse(DominionHudText.BannerIsFull(20000, 8f));
        }

        // ---- the centre payout flash

        [Test] public void TheHolderOfTheCentreIsFlashedWhenTheNextPayoutMovedAndTheirPointsJumpedByThePayout()
        {
            Assert.AreEqual(2, DominionHudText.CentrePayoutTeam(30000, 60000, new[] { 400, 300, 520 }, new[] { 400, 300, 720 }, 200, holder: 2));
            Assert.AreEqual(1, DominionHudText.CentrePayoutTeam(30000, 60000, new[] { 400, 300, 520 }, new[] { 400, 505, 520 }, 200, holder: 1), "the payout and a few ticks in one write");
        }

        [Test] public void OnlyTheHolderFlashesEvenWhenAnotherTeamsPointsRoseMoreInTheSameWrite()
        {
            // Cyan (2) holds the centre and was paid 200; white (0) rose by 330 from zones in the same write. The biggest rise is not the payout.
            Assert.AreEqual(2, DominionHudText.CentrePayoutTeam(30000, 60000, new[] { 400, 300, 520 }, new[] { 730, 300, 720 }, 200, holder: 2));
        }

        [Test] public void AHolderWhosePointsDidNotRiseByThePayoutIsNotFlashed() =>
            Assert.AreEqual(-1, DominionHudText.CentrePayoutTeam(30000, 60000, new[] { 400, 300, 520 }, new[] { 730, 300, 530 }, 200, holder: 2),
                "this client's copy says cyan holds it, but the room did not pay cyan: nothing to flash");

        [Test] public void WithNoKnownHolderTheBiggestRiseByAtLeastThePayoutIsFlashed()
        {
            Assert.AreEqual(2, DominionHudText.CentrePayoutTeam(30000, 60000, new[] { 400, 300, 520 }, new[] { 400, 300, 720 }, 200, holder: -1), "this client's territory copy has not caught up");
            Assert.AreEqual(-1, DominionHudText.CentrePayoutTeam(30000, 60000, new[] { 0, 0, 0 }, new[] { 10, 5, 0 }, 200, holder: -1));
        }

        [Test] public void OrdinaryTicksANoPayoutAndAJoinersFirstReadNeverFlash()
        {
            Assert.AreEqual(-1, DominionHudText.CentrePayoutTeam(30000, 30000, new[] { 0, 0, 0 }, new[] { 5, 5, 5 }, 200, holder: 1), "the next payout time did not move");
            Assert.AreEqual(-1, DominionHudText.CentrePayoutTeam(30000, 60000, new[] { 0, 0, 0 }, new[] { 10, 5, 0 }, 200, holder: -1), "nobody held it: the time moved, nobody was paid");
            Assert.AreEqual(-1, DominionHudText.CentrePayoutTeam(0, 60000, new[] { 0, 0, 0 }, new[] { 400, 0, 0 }, 200, holder: 0), "the first read of a joiner is no payout");
            Assert.AreEqual(-1, DominionHudText.CentrePayoutTeam(30000, 60000, new[] { 0, 0, 0 }, new[] { 200, 0, 0 }, 0, holder: 0), "a payout of 0 is nothing to flash");
        }

        [Test] public void TheFlashNamesThePointsAndTheTeamInCapitals() =>
            Assert.AreEqual("+200 CYAN", DominionHudText.FlashText("+{0} {1}", 200, Names, 2));

        // ---- the result

        [Test] public void TheHeadlineNamesTheWinnerAndTheirRoundWinsFirst() =>
            Assert.AreEqual("PURPLE WINS 2–1", DominionHudText.ResultHeadline(1, new[] { 1, 2, 0 }, new[] { 0, 1 }, Names, "{0} WINS {1}", "{0} WINS IN SUDDEN DEATH", "–", false));

        [Test] public void AThreeTeamScoreListsTheOthersHighestFirst() =>
            Assert.AreEqual("2–1–0", DominionHudText.ScoreLine(2, new[] { 0, 1, 2 }, new[] { 0, 1, 2 }, "–"));

        [Test] public void AMatchWonInSuddenDeathSaysSoInsteadOfAScore() =>
            Assert.AreEqual("CYAN WINS IN SUDDEN DEATH", DominionHudText.ResultHeadline(2, new[] { 1, 1, 1 }, new[] { 0, 1, 2 }, Names, "{0} WINS {1}", "{0} WINS IN SUDDEN DEATH", "–", true));

        [Test] public void AMatchIsWonInSuddenDeathOnlyWhenTheRoomEverHadASuddenDeathStart()
        {
            Assert.IsTrue(DominionHudText.WonInSuddenDeath(2, 123456, new[] { 1, 1, 1 }, 2), "the circle was written, so the circle decided it");
            Assert.IsFalse(DominionHudText.WonInSuddenDeath(1, 0, new[] { 0, 1, 0 }, 2), "no dSd was ever written");
            Assert.IsFalse(DominionHudText.WonInSuddenDeath(-1, 123456, new[] { 1, 1, 1 }, 2), "no winner is not a sudden-death win");
        }

        [Test] public void AMatchEndedAfterOneRoundWithOneTeamLeftIsNotCalledSuddenDeath()
        {
            // A7: 1-0-0 after round 1, a team won by the others leaving. The old inference (fewer wins than needed) called this sudden death.
            bool sudden = DominionHudText.WonInSuddenDeath(1, 0, new[] { 0, 1, 0 }, 2);
            Assert.AreEqual("PURPLE WINS 1–0", DominionHudText.ResultHeadline(1, new[] { 0, 1, 0 }, new[] { 0, 1 }, Names, "{0} WINS {1}", "{0} WINS IN SUDDEN DEATH", "–", sudden));
        }

        [Test] public void AMatchWonByTheLastTeamStandingMidRoundIsNotCalledSuddenDeath()
        {
            // A3: the last team with anyone in the room wins at once, wins as they stand (0-0).
            bool sudden = DominionHudText.WonInSuddenDeath(0, 0, new[] { 0, 0, 0 }, 2);
            Assert.AreEqual("WHITE WINS 0–0", DominionHudText.ResultHeadline(0, new[] { 0, 0, 0 }, new[] { 0, 1 }, Names, "{0} WINS {1}", "{0} WINS IN SUDDEN DEATH", "–", sudden));
        }

        [Test] public void TheModeLineNamesTheSizeOfTheMatch()
        {
            Assert.AreEqual("DOMINION 2v2", DominionHudText.ModeLine("DOMINION {0}", 2));
            Assert.AreEqual("DOMINION 3v3v3", DominionHudText.ModeLine("DOMINION {0}", 3));
        }

        // ---- each round's points (dHist)

        [Test] public void AppendingARoundAddsThreeCellsAndLeavesTheOldHistoryAlone()
        {
            int[] one = DominionHistory.Append(null, new[] { 540, 620 });
            CollectionAssert.AreEqual(new[] { 540, 620, 0 }, one, "a 2v2 round pads the missing team with 0");
            int[] two = DominionHistory.Append(one, new[] { 710, 655, 0 });
            CollectionAssert.AreEqual(new[] { 540, 620, 0, 710, 655, 0 }, two);
            CollectionAssert.AreEqual(new[] { 540, 620, 0 }, one, "a Photon array is shared: appending never edits the old one");
            Assert.AreEqual(2, DominionHistory.RoundCount(two));
        }

        [Test] public void AHistoryCellIsReadByRoundAndTeamAndAMissingCellIsZero()
        {
            int[] history = { 540, 620, 0, 710, 655, 0 };
            Assert.AreEqual(710, DominionHistory.PointsOf(history, 1, 0));
            Assert.AreEqual(655, DominionHistory.PointsOf(history, 1, 1));
            Assert.AreEqual(0, DominionHistory.PointsOf(history, 2, 0), "round 3 is not there yet");
            Assert.AreEqual(0, DominionHistory.PointsOf(null, 0, 0));
        }

        [Test] public void TheResultTableMarksEachRoundsWinnerAndNoOneForATiedRound()
        {
            int[] history = { 540, 620, 0, 710, 655, 0, 500, 500, 0 };
            Assert.AreEqual(1, DominionHistory.WinnerOfRound(history, 0), "purple took round 1");
            Assert.AreEqual(0, DominionHistory.WinnerOfRound(history, 1), "white took round 2");
            Assert.AreEqual(-1, DominionHistory.WinnerOfRound(history, 2), "a tied round has no bold name");
            Assert.AreEqual(-1, DominionHistory.WinnerOfRound(history, 3), "a round that was not played has none either");
        }

        // ---- the master writes the history with the score (the wiring: Next must use it)

        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers { RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f };

        private static DominionWrite RoundEndsWith(int round, int[] points, int[] wins, int[] history) =>
            DominionRoomWrites.Next(true, true, 200000,
                new DominionRoomState { HasRound = true, Round = round, Stage = DominionStage.Round, EndMs = 100000, Points = points, Wins = wins, Winner = -1, History = history },
                Cfg, new[] { 0, 1, 2 }, new[] { 3, 3, 3 });

        [Test] public void ARoundThatEndsIntoABreakWritesItsPointsIntoTheHistory()
        {
            DominionWrite w = RoundEndsWith(1, new[] { 540, 620, 100 }, new[] { 0, 0, 0 }, null);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 540, 620, 100 }, (int[])w.Props[DominionKeys.History]);
        }

        [Test] public void AnEarlierRoundsPointsStayInTheHistoryWhenTheNextIsAppended()
        {
            DominionWrite w = RoundEndsWith(2, new[] { 710, 655, 0 }, new[] { 0, 1, 0 }, new[] { 540, 620, 100 });
            CollectionAssert.AreEqual(new[] { 540, 620, 100, 710, 655, 0 }, (int[])w.Props[DominionKeys.History]);
        }

        [Test] public void TheRoundThatDecidesTheMatchIsInTheHistoryToo()
        {
            DominionWrite w = RoundEndsWith(2, new[] { 100, 700, 0 }, new[] { 0, 1, 0 }, new[] { 540, 620, 100 });
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 540, 620, 100, 100, 700, 0 }, (int[])w.Props[DominionKeys.History], "the last round is a row of the result table");
        }

        [Test] public void TheRoundThatSendsTheMatchToSuddenDeathIsInTheHistoryToo()
        {
            DominionWrite w = RoundEndsWith(3, new[] { 300, 300, 100 }, new[] { 1, 1, 0 }, new[] { 540, 620, 0, 700, 655, 0 });
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(9, ((int[])w.Props[DominionKeys.History]).Length);
        }

        [Test] public void TheHistoryIsReadFromTheRoomLikeEveryOtherKey()
        {
            var props = new ExitGames.Client.Photon.Hashtable { { DominionKeys.History, new[] { 1, 2, 3 } } };
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, DominionRoomState.Read(props).History);
            Assert.IsNull(DominionRoomState.Read(new ExitGames.Client.Photon.Hashtable()).History);
        }

        // ---- the match ends with one team left: the unfinished round is in the table (Task 9 review, default A34)

        private static DominionWrite LastTeamLeft(DominionStage stage, int[] points, int[] history) =>
            DominionRoomWrites.Next(true, true, 200000,
                new DominionRoomState { HasRound = true, Round = 2, Stage = stage, EndMs = 300000, Points = points, Wins = new[] { 1, 0, 0 }, Winner = -1, History = history },
                Cfg, new[] { 0, 1 }, new[] { 2, 0, 0 });

        [Test] public void WhenOneTeamIsLeftMidRoundTheAbandonedRoundsPointsGoIntoTheHistory()
        {
            DominionWrite w = LastTeamLeft(DominionStage.Round, new[] { 210, 340, 0 }, new[] { 540, 620, 0 });
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.IsTrue(w.Props.ContainsKey(DominionKeys.History), "the result table needs the round that was cut short");
            CollectionAssert.AreEqual(new[] { 540, 620, 0, 210, 340, 0 }, (int[])w.Props[DominionKeys.History]);
        }

        [Test] public void WhenOneTeamIsLeftInABreakNoRoundIsAddedToTheHistory()
        {
            DominionWrite w = LastTeamLeft(DominionStage.Break, new[] { 540, 620, 0 }, new[] { 540, 620, 0 });
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.History), "the break's points are round 1's, already in the history");
        }

        // ---- the room records each round's winner (dHistW): a round cut short has none (Task 14b, default A49)

        [Test] public void AScoredRoundBoldsTheWinnerTheRoomRecorded()
        {
            int[] history = { 540, 620, 0, 710, 655, 0 };
            int[] winners = { 1, 0 };
            CollectionAssert.AreEqual(new[] { 1 }, DominionHistory.WinnersOfRound(history, winners, 0));
            CollectionAssert.AreEqual(new[] { 0 }, DominionHistory.WinnersOfRound(history, winners, 1));
        }

        [Test] public void ATiedRoundAndACutShortRoundBoldNobody()
        {
            int[] history = { 500, 500, 0, 210, 340, 0 };
            int[] winners = { -1, DominionHistory.CutShort };
            Assert.IsEmpty(DominionHistory.WinnersOfRound(history, winners, 0), "tied");
            Assert.IsEmpty(DominionHistory.WinnersOfRound(history, winners, 1), "cut short: purple led on points but won nothing");
        }

        [Test] public void ARoomFromBeforeTheWinnersKeyFallsBackToThePointsLeader()
        {
            int[] history = { 540, 620, 0, 500, 500, 0 };
            CollectionAssert.AreEqual(new[] { 1 }, DominionHistory.WinnersOfRound(history, null, 0));
            Assert.IsEmpty(DominionHistory.WinnersOfRound(history, null, 1), "a tie stays a tie");
            CollectionAssert.AreEqual(new[] { 1 }, DominionHistory.WinnersOfRound(history, new int[0], 0), "a round the list does not reach also falls back");
        }

        [Test] public void AppendingAWinnerAddsOneEntryAndLeavesTheOldListAlone()
        {
            int[] one = DominionHistory.AppendWinner(null, 1);
            CollectionAssert.AreEqual(new[] { 1 }, one);
            int[] two = DominionHistory.AppendWinner(one, -1);
            CollectionAssert.AreEqual(new[] { 1, -1 }, two);
            CollectionAssert.AreEqual(new[] { 1 }, one, "a Photon array is shared: appending never edits the old one");
        }

        [Test] public void TheWinnersAreReadFromTheRoomLikeEveryOtherKey()
        {
            var props = new ExitGames.Client.Photon.Hashtable { { DominionKeys.HistoryWinners, new[] { 1, -1 } } };
            CollectionAssert.AreEqual(new[] { 1, -1 }, DominionRoomState.Read(props).HistoryWinners);
            Assert.IsNull(DominionRoomState.Read(new ExitGames.Client.Photon.Hashtable()).HistoryWinners);
        }

        [Test] public void ARoundThatEndsIntoABreakWritesItsWinnerWithItsPoints()
        {
            DominionWrite w = RoundEndsWith(1, new[] { 540, 620, 100 }, new[] { 0, 0, 0 }, null);
            CollectionAssert.AreEqual(new[] { 1 }, (int[])w.Props[DominionKeys.HistoryWinners]);
        }

        [Test] public void ATiedRoundWritesNoWinnerAndAnEarlierWinnerStays()
        {
            DominionWrite w = RoundEndsWith(2, new[] { 400, 400, 0 }, new[] { 0, 1, 0 }, new[] { 540, 620, 100 });
            // winners list as the room holds it after round 1
            DominionWrite tied = DominionRoomWrites.Next(true, true, 200000,
                new DominionRoomState { HasRound = true, Round = 2, Stage = DominionStage.Round, EndMs = 100000, Points = new[] { 400, 400, 0 }, Wins = new[] { 0, 1, 0 },
                    Winner = -1, History = new[] { 540, 620, 100 }, HistoryWinners = new[] { 1 } },
                Cfg, new[] { 0, 1, 2 }, new[] { 3, 3, 3 });
            CollectionAssert.AreEqual(new[] { 1, -1 }, (int[])tied.Props[DominionKeys.HistoryWinners]);
            Assert.IsNotNull(w);
        }

        [Test] public void TheRoundThatDecidesTheMatchWritesItsWinnerToo()
        {
            DominionWrite w = RoundEndsWith(2, new[] { 100, 700, 0 }, new[] { 0, 1, 0 }, new[] { 540, 620, 100 });
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.IsTrue(w.Props.ContainsKey(DominionKeys.HistoryWinners));
            Assert.AreEqual(1, ((int[])w.Props[DominionKeys.HistoryWinners])[^1]);
        }

        [Test] public void TheRoundThatSendsTheMatchToSuddenDeathWritesItsWinnerToo()
        {
            DominionWrite w = RoundEndsWith(3, new[] { 300, 300, 100 }, new[] { 1, 1, 0 }, new[] { 540, 620, 0, 700, 655, 0 });
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(-1, ((int[])w.Props[DominionKeys.HistoryWinners])[^1], "the last round was tied");
        }

        [Test] public void ARoundCutShortByLastTeamStandingIsWrittenWithNoWinner()
        {
            DominionWrite w = LastTeamLeft(DominionStage.Round, new[] { 210, 340, 0 }, new[] { 540, 620, 0 });
            Assert.IsTrue(w.Props.ContainsKey(DominionKeys.HistoryWinners));
            Assert.AreEqual(DominionHistory.CutShort, ((int[])w.Props[DominionKeys.HistoryWinners])[^1], "purple led on points, but the round counted for nobody");
        }

        [Test] public void WhenOneTeamIsLeftInABreakNoWinnerIsAdded()
        {
            DominionWrite w = LastTeamLeft(DominionStage.Break, new[] { 540, 620, 0 }, new[] { 540, 620, 0 });
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.HistoryWinners));
        }

        // ---- the wiring: the screens call the tested words, and the result is reached from both result paths (method bodies are read, not run)

        private static System.Reflection.MethodInfo Text(string name) => typeof(DominionHudText).GetMethod(name);
        private static System.Reflection.MethodInfo Hist(string name) => typeof(DominionHistory).GetMethod(name);

        [Test] public void TheRoundBarDrawsTheTestedClockLabelAndFlash()
        {
            System.Type bar = typeof(Overpower.UI.RoundHud);
            Assert.IsTrue(IlWiring.Uses(bar, Text(nameof(DominionHudText.Clock))));
            Assert.IsTrue(IlWiring.Uses(bar, Text(nameof(DominionHudText.RoundLabel))));
            Assert.IsTrue(IlWiring.Uses(bar, Text(nameof(DominionHudText.FlashText))));
        }

        [Test] public void TheBreakCardDrawsTheTestedHeadlineCountdownAndOpensLine()
        {
            System.Type card = typeof(Overpower.UI.BreakCard);
            Assert.IsTrue(IlWiring.Uses(card, Text(nameof(DominionHudText.BreakHeadline))));
            Assert.IsTrue(IlWiring.Uses(card, Text(nameof(DominionHudText.BreakCountdown))));
            Assert.IsTrue(IlWiring.Uses(card, Text(nameof(DominionHudText.OpensLine))));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.DominionHud), Hist(nameof(DominionHistory.WinnersOfFinishedRound))), "the card is given the winners the master recorded, not the points leader");
        }

        [Test] public void TheCentreLabelDrawsTheTestedLines()
        {
            System.Type label = typeof(Overpower.UI.CentrePayoutLabel);
            Assert.IsTrue(IlWiring.Uses(label, Text(nameof(DominionHudText.CentreLine))));
            Assert.IsTrue(IlWiring.Uses(label, Text(nameof(DominionHudText.HolderLine))));
        }

        [Test] public void TheSuddenDeathOverlayDrawsTheTestedBannerChoiceAndShrinkLine()
        {
            System.Type overlay = typeof(Overpower.UI.SuddenDeathOverlay);
            Assert.IsTrue(IlWiring.Uses(overlay, Text(nameof(DominionHudText.BannerIsFull))));
            Assert.IsTrue(IlWiring.Uses(overlay, Text(nameof(DominionHudText.ShrinkLine))));
        }

        [Test] public void TheResultCardIsTheLobbyResultCardWithTheTestedHeadlineAndTable()
        {
            System.Type panel = typeof(Overpower.UI.DominionResultPanel);
            Assert.IsTrue(IlWiring.Uses(panel, typeof(Overpower.UI.LobbyUiKit).GetMethod(nameof(Overpower.UI.LobbyUiKit.ResultCard))), "the same look as the spectator card");
            Assert.IsTrue(IlWiring.Uses(panel, Text(nameof(DominionHudText.ResultHeadline))));
            Assert.IsTrue(IlWiring.Uses(panel, Text(nameof(DominionHudText.WonInSuddenDeath))));
            Assert.IsTrue(IlWiring.Uses(panel, Text(nameof(DominionHudText.ModeLine))));
            Assert.IsTrue(IlWiring.Uses(panel, Hist(nameof(DominionHistory.PointsOf))));
        }

        [Test] public void TheHudHostReadsTheClockAndTheCentrePayoutThroughTheTestedRules()
        {
            System.Type hud = typeof(Overpower.UI.DominionHud);
            Assert.IsTrue(IlWiring.Uses(hud, Text(nameof(DominionHudText.SecondsLeft))));
            Assert.IsTrue(IlWiring.Uses(hud, Text(nameof(DominionHudText.CentrePayoutTeam))));
            Assert.IsTrue(IlWiring.Uses(hud, typeof(DominionRoomState).GetMethod(nameof(DominionRoomState.Read))));
        }

        [Test] public void TheHudPassesTheCentresHolderToTheFlashRuleAndTheResultTheRoomsCircleStart()
        {
            System.Type hud = typeof(Overpower.UI.DominionHud);
            Assert.IsTrue(IlWiring.Uses(hud, "ReadRoom", typeof(Overpower.UI.DominionHud).GetMethod("CentreHolder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)),
                "the flash is the holder's, so ReadRoom must ask who holds the centre");
            Assert.IsTrue(IlWiring.Uses(hud, "ShowResult", typeof(DominionRoomState).GetField(nameof(DominionRoomState.SuddenDeathMs))),
                "the result decides sudden death from the room's dSd");
        }

        [Test] public void ASpectatorWhoLeavesTakesTheDominionResultCardDown() =>
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Lobby.SpectatorSeatView), "End",
                typeof(Overpower.UI.DominionHud).GetMethod(nameof(Overpower.UI.DominionHud.HideResult))));

        [Test] public void BothResultPathsAskTheDominionHudBeforeTheirOwnPanel()
        {
            System.Reflection.MethodInfo show = typeof(Overpower.UI.DominionHud).GetMethod(nameof(Overpower.UI.DominionHud.ShowResult));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUI), show), "a player's result");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Lobby.SpectatorSeatView), show), "a spectator's result");
        }

        private static System.Reflection.MethodInfo HistWinnerOfRound() => typeof(DominionHistory).GetMethod(nameof(DominionHistory.WinnersOfRound));

        [Test] public void TheResultTableBoldsThroughTheRecordedWinnersAndTheResultReadsThem()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.DominionResultPanel), HistWinnerOfRound()), "bold = the recorded winner");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.DominionHud), "ShowResult", typeof(DominionRoomState).GetField(nameof(DominionRoomState.HistoryWinners))),
                "the result card is given the room's dHistW");
        }

        [Test] public void TheMasterAppendsTheWinnerThroughTheTestedHelper() =>
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), Hist(nameof(DominionHistory.AppendWinner))));

        [Test] public void TheMasterAppendsTheRoundToTheHistoryThroughTheTestedHelper() =>
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), Hist(nameof(DominionHistory.Append))));
    }
}
