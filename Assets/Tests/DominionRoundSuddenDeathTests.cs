using ExitGames.Client.Photon;
using NUnit.Framework;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>A shared round never hands out the match (A65): when it would give a sharing team its match-winning round win, the sharing teams play
    /// sudden death for that round; its winner alone takes the round win, then the match is over or the next round's break follows. Every number is a
    /// literal made up for the test (2 wins of 3 rounds, a 200 lead), never the asset's.</summary>
    public class DominionRoundSuddenDeathTests
    {
        private static readonly int[] Two = { 0, 1 };
        private static readonly int[] Three = { 0, 1, 2 };
        private static readonly int[] TwoPlayers = { 2, 2, 0 };
        private const int Now = 110000;

        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers
        {
            RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, OvertimeSeconds = 60f, OvertimeLeadPoints = 200,
            SuddenDeathCountdownSeconds = 4f, SameInstantToleranceMs = 50,
        };

        private static SuddenDeathRules.Player Fell(int team, int stampMs) =>
            new SuddenDeathRules.Player { Team = team, Counts = true, HasAliveFlag = true, AliveFlag = false, HasDeathStamp = true, DeathStampMs = stampMs };

        private static SuddenDeathRules.Player Lives(int team) =>
            new SuddenDeathRules.Player { Team = team, Counts = true, HasAliveFlag = true, AliveFlag = true };

        private static SuddenDeathRules.Tally TallyOf(params SuddenDeathRules.Player[] players)
        {
            var tally = new SuddenDeathRules.Tally(3);
            foreach (SuddenDeathRules.Player p in players) tally.Add(p);
            return tally;
        }

        /// <summary>An overtime whose minute ran out at 100000 ms; the history holds the rounds before it.</summary>
        private static DominionRoomState OvertimeOver(int round, int[] points, int[] wins, int[] overtimeTeams, int[] history, int[] historyWinners) =>
            new DominionRoomState
            {
                HasRound = true, Round = round, Stage = DominionStage.Overtime, EndMs = 100000, Points = points, Wins = wins, Winner = -1,
                OvertimeTeams = overtimeTeams, History = history, HistoryWinners = historyWinners, PointsSeq = 41,
            };

        /// <summary>White 1-0 shared round 2 with Purple (500 to 450): sudden death for round 2, circle started at 100000.</summary>
        private static DominionRoomState RoundTwoSuddenDeath() =>
            new DominionRoomState
            {
                HasRound = true, Round = 2, Stage = DominionStage.SuddenDeath, EndMs = 0, SuddenDeathMs = 100000, SuddenDeathTeams = Two,
                SuddenDeathRound = 2, Points = new[] { 500, 450, 0 }, Wins = new[] { 1, 0, 0 }, Winner = -1,
                History = new[] { 300, 100, 0 }, HistoryWinners = new[] { 0 }, PointsSeq = 41,
            };

        // ---------------------------------------------------------------- the pure rule

        [Test] public void ASharedRoundThatWouldGiveATeamItsMatchWinningWinGoesToSuddenDeathBetweenTheSharers() =>
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.SharedRoundGoesToSuddenDeath(new[] { 1, 0, 0 }, new[] { 1, 0 }, 2));

        [Test] public void ASharedRoundThatHandsNobodyTheMatchIsAWinEach() =>
            Assert.IsNull(DominionRules.SharedRoundGoesToSuddenDeath(new[] { 0, 0, 0 }, new[] { 0, 1 }, 2));

        [Test] public void ARoundWonByOneTeamIsNeverSuddenDeath() =>
            Assert.IsNull(DominionRules.SharedRoundGoesToSuddenDeath(new[] { 1, 1, 0 }, new[] { 0 }, 2));

        // ---------------------------------------------------------------- the round's end

        [Test] public void WhiteOneUpSharingWithPurpleStartsSuddenDeathForTheRoundWithoutAddingWins()
        {
            DominionRoomState room = OvertimeOver(2, new[] { 500, 450, 0 }, new[] { 1, 0, 0 }, Two, new[] { 300, 100, 0 }, new[] { 0 });
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers);
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(Two, (int[])w.Props[DominionKeys.SuddenDeathTeams]);
            Assert.AreEqual(2, w.Props[DominionKeys.SuddenDeathRound], "the room says which round this sudden death decides");
            Assert.AreEqual(Now + 4000, w.Props[DominionKeys.SuddenDeathStart], "the get-ready as for any sudden death (A17)");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Wins), "nobody gets the round win yet");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.History), "the round's points go into the history once, with its verdict");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.HistoryWinners));
            Assert.IsTrue(w.Props.ContainsKey(DominionKeys.OvertimeTeams) && w.Props[DominionKeys.OvertimeTeams] == null, "the overtime's list leaves the room");
            Assert.AreEqual(41, w.Expected[DominionKeys.PointsSeq], "built on the points it judged, like any write that ends a round");
        }

        [Test] public void NilNilSharedIsOneOneAndTheNextBreakWithNoSuddenDeath()
        {
            DominionRoomState room = OvertimeOver(1, new[] { 500, 450, 0 }, new[] { 0, 0, 0 }, Two, null, null);
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.SuddenDeathRound));
        }

        [Test] public void OneOneSharedGoesToSuddenDeathForTheRound()
        {
            DominionRoomState room = OvertimeOver(3, new[] { 500, 450, 0 }, new[] { 1, 1, 0 }, Two, new[] { 300, 100, 0, 100, 300, 0 }, new[] { 0, 1 });
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers);
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(3, w.Props[DominionKeys.SuddenDeathRound]);
            CollectionAssert.AreEqual(Two, (int[])w.Props[DominionKeys.SuddenDeathTeams]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Wins), "two teams never reach the target together any more");
        }

        [Test] public void InThreeTeamsOnlyTheTeamsWithinTheLeadAtTheEndPlayTheRoundsSuddenDeath()
        {
            // White 1, Purple 0, Cyan 0; all three played the overtime, Cyan ended 300 behind White (out), Purple 100 behind (in).
            DominionRoomState room = OvertimeOver(2, new[] { 600, 500, 300 }, new[] { 1, 0, 0 }, Three, new[] { 300, 100, 50 }, new[] { 0 });
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Three, new[] { 2, 2, 2 });
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 0, 1 }, (int[])w.Props[DominionKeys.SuddenDeathTeams], "Cyan waits dead");
        }

        // ---------------------------------------------------------------- the verdict

        [Test] public void TheMatchPointTeamWinningTheRoundsSuddenDeathWinsTheMatchTwoNil()
        {
            DominionRoomState room = RoundTwoSuddenDeath();
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers, TallyOf(Lives(0), Fell(1, 105000)));
            Assert.AreEqual(DominionRoomWrites.WhatSuddenDeathWon, w.What);
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(0, w.Props[DominionKeys.Winner]);
            CollectionAssert.AreEqual(new[] { 2, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
            CollectionAssert.AreEqual(new[] { 300, 100, 0, 500, 450, 0 }, (int[])w.Props[DominionKeys.History]);
            CollectionAssert.AreEqual(new[] { 0, DominionHistory.EncodeSuddenDeathWinner(0) }, (int[])w.Props[DominionKeys.HistoryWinners]);
            Assert.AreEqual(100000, w.Expected[DominionKeys.SuddenDeathStart], "judged on this circle start, like every verdict");
        }

        [Test] public void TheOtherTeamWinningTheRoundsSuddenDeathMakesItOneOneAndTheNextRoundsBreak()
        {
            DominionRoomState room = RoundTwoSuddenDeath();
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers, TallyOf(Fell(0, 105000), Lives(1)));
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(3, w.Props[DominionKeys.Round]);
            Assert.AreEqual(Now + 10000, w.Props[DominionKeys.StageEnd]);
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
            CollectionAssert.AreEqual(new[] { 300, 100, 0, 500, 450, 0 }, (int[])w.Props[DominionKeys.History]);
            CollectionAssert.AreEqual(new[] { 0, DominionHistory.EncodeSuddenDeathWinner(1) }, (int[])w.Props[DominionKeys.HistoryWinners]);
            foreach (string gone in new[] { DominionKeys.SuddenDeathStart, DominionKeys.SuddenDeathTeams, DominionKeys.SuddenDeathRound, DominionKeys.CentrePayout })
                Assert.IsTrue(w.Props.ContainsKey(gone) && w.Props[gone] == null, gone + " leaves the room with the sudden death");
            Assert.AreEqual("sudden death won:1", DominionRoomWrites.SuddenDeathVerdictKey(w), "held by the settle beat like any verdict, per team");
        }

        [Test] public void OneOneSuddenDeathForTheRoundGivesItsWinnerTheMatch()
        {
            DominionRoomState room = RoundTwoSuddenDeath();
            room.Round = 3; room.SuddenDeathRound = 3; room.Wins = new[] { 1, 1, 0 };
            room.History = new[] { 300, 100, 0, 100, 300, 0 }; room.HistoryWinners = new[] { 0, 1 };
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers, TallyOf(Fell(0, 105000), Lives(1)));
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(1, w.Props[DominionKeys.Winner]);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, (int[])w.Props[DominionKeys.Wins]);
        }

        [Test] public void ALastRoundSuddenDeathWonByTheTeamOffMatchPointLeadsToSuddenDeathForTheMatch()
        {
            // White 1-0 after a tied round 2; White and Purple shared round 3; Purple wins its sudden death: 1-1 with no rounds left.
            DominionRoomState room = RoundTwoSuddenDeath();
            room.Round = 3; room.SuddenDeathRound = 3;
            room.History = new[] { 300, 100, 0, 200, 200, 0 }; room.HistoryWinners = new[] { 0, -1 };
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers, TallyOf(Fell(0, 105000), Lives(1)));
            Assert.AreNotEqual((int)DominionStage.Over, w.Props.ContainsKey(DominionKeys.Stage) ? w.Props[DominionKeys.Stage] : (int)DominionStage.SuddenDeath, "still sudden death");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Stage) && (int)w.Props[DominionKeys.Stage] != (int)DominionStage.SuddenDeath, "still sudden death");
            Assert.AreEqual(Now + 4000, w.Props[DominionKeys.SuddenDeathStart], "a new circle: every client sees a sudden-death start");
            CollectionAssert.AreEqual(Two, (int[])w.Props[DominionKeys.SuddenDeathTeams]);
            Assert.IsTrue(w.Props.ContainsKey(DominionKeys.SuddenDeathRound) && w.Props[DominionKeys.SuddenDeathRound] == null, "this one is for the match");
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
            Assert.AreEqual(9, ((int[])w.Props[DominionKeys.History]).Length);
            Assert.AreEqual(DominionRoomWrites.WhatSuddenDeathWon, w.What, "held by the settle beat");
        }

        [Test] public void AReplayInsideARoundsSuddenDeathKeepsDecidingThatRound()
        {
            DominionRoomState room = RoundTwoSuddenDeath();
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers, TallyOf(Fell(0, 105000), Fell(1, 105000)));
            Assert.AreEqual(DominionRoomWrites.WhatSuddenDeathReplay, w.What);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.SuddenDeathRound), "the round it decides stays");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Wins));
            Assert.AreEqual(100000, w.Expected[DominionKeys.SuddenDeathStart]);
        }

        [Test] public void TheLastRoundSuddenDeathForTheMatchIsUnchanged()
        {
            DominionRoomState room = RoundTwoSuddenDeath();
            room.Round = 3; room.SuddenDeathRound = 0; room.Wins = new[] { 1, 1, 0 };
            room.History = new[] { 300, 100, 0, 100, 300, 0, 200, 200, 0 }; room.HistoryWinners = new[] { 0, 1, -1 };
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, TwoPlayers, TallyOf(Lives(0), Fell(1, 105000)));
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(0, w.Props[DominionKeys.Winner]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Wins), "the match's sudden death adds no round win");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.History), "every round is already in the history");
        }

        [Test] public void ANewMasterCarriesOnFromTheRoom()
        {
            DominionRoomState before = RoundTwoSuddenDeath();
            var props = new Hashtable
            {
                { DominionKeys.Round, 2 }, { DominionKeys.Stage, (int)DominionStage.SuddenDeath }, { DominionKeys.StageEnd, 0 },
                { DominionKeys.SuddenDeathStart, 100000 }, { DominionKeys.SuddenDeathTeams, Two }, { DominionKeys.SuddenDeathRound, 2 },
                { DominionKeys.Points, new[] { 500, 450, 0 } }, { DominionKeys.Wins, new[] { 1, 0, 0 } }, { DominionKeys.Winner, -1 },
                { DominionKeys.History, new[] { 300, 100, 0 } }, { DominionKeys.HistoryWinners, new[] { 0 } },
            };
            DominionRoomState read = DominionRoomState.Read(props);
            Assert.AreEqual(2, read.SuddenDeathRound);
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, read, Cfg, Two, TwoPlayers, TallyOf(Fell(0, 105000), Lives(1)));
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage], "the new master gives the round to its sudden-death winner as the old one would");
            Assert.AreEqual(0, DominionRoomState.Read(new Hashtable()).SuddenDeathRound, "absent = a sudden death for the match");
            Assert.AreEqual(before.SuddenDeathRound, read.SuddenDeathRound);
        }

        [Test] public void TheLastTeamStandingInARoundsSuddenDeathKeepsTheRoundsPointsAsCutShort()
        {
            DominionRoomState room = RoundTwoSuddenDeath();
            DominionWrite w = DominionRoomWrites.Next(true, true, Now, room, Cfg, Two, new[] { 2, 0, 0 });
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, (int[])w.Props[DominionKeys.Wins], "nobody won the round");
            CollectionAssert.AreEqual(new[] { 300, 100, 0, 500, 450, 0 }, (int[])w.Props[DominionKeys.History]);
            CollectionAssert.AreEqual(new[] { 0, DominionHistory.CutShort }, (int[])w.Props[DominionKeys.HistoryWinners]);
        }

        // ---------------------------------------------------------------- the edge into the break

        [Test] public void LeavingSuddenDeathIntoABreakIsTheBreaksFreshStart()
        {
            Assert.AreEqual(DominionEdge.BreakStarted, DominionRoomWrites.EdgeBetween(2, DominionStage.SuddenDeath, 3, DominionStage.Break));
            Assert.AreEqual(DominionEdge.None, DominionRoomWrites.EdgeBetween(3, DominionStage.SuddenDeath, 3, DominionStage.Over));
        }

        // ---------------------------------------------------------------- how it is remembered and shown

        [Test] public void ARoundWonInSuddenDeathIsRecordedAsItsOneWinner()
        {
            int entry = DominionHistory.EncodeSuddenDeathWinner(1);
            CollectionAssert.AreEqual(new[] { 1 }, DominionHistory.DecodeWinners(entry));
            Assert.IsTrue(DominionHistory.WonInSuddenDeath(entry));
            Assert.IsFalse(DominionHistory.WonInSuddenDeath(DominionHistory.EncodeWinners(new[] { 0, 1 })));
            Assert.IsFalse(DominionHistory.WonInSuddenDeath(1));
            CollectionAssert.AreEqual(new[] { 0, 2 }, DominionHistory.DecodeWinners(DominionHistory.EncodeWinners(new[] { 0, 2 })), "a shared round still reads as shared");
        }

        [Test] public void TheBreakCardKnowsTheRoundBeforeWasWonInSuddenDeath()
        {
            var state = new DominionRoomState { Round = 3, HistoryWinners = new[] { 0, DominionHistory.EncodeSuddenDeathWinner(1) } };
            Assert.IsTrue(DominionHistory.FinishedRoundWonInSuddenDeath(state));
            CollectionAssert.AreEqual(new[] { 1 }, DominionHistory.WinnersOfFinishedRound(state));
            Assert.IsFalse(DominionHistory.FinishedRoundWonInSuddenDeath(new DominionRoomState { Round = 3, HistoryWinners = new[] { 0, 1 } }));
            Assert.IsFalse(DominionHistory.FinishedRoundWonInSuddenDeath(new DominionRoomState { Round = 1 }));
        }

        [Test] public void AMatchWonByReachingTheTargetShowsItsScoreEvenAfterASuddenDeath()
        {
            Assert.IsFalse(DominionHudText.WonInSuddenDeath(0, 100000, new[] { 2, 0, 0 }, 2), "2-0: the score says it");
            Assert.IsTrue(DominionHudText.WonInSuddenDeath(1, 100000, new[] { 1, 1, 0 }, 2), "level after the last round: the circle decided the match");
            Assert.IsFalse(DominionHudText.WonInSuddenDeath(1, 0, new[] { 1, 1, 0 }, 2), "no circle was ever written");
        }

        [Test] public void TheHudAsksTheTestedRuleForTheSuddenDeathLine() =>
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.DominionHud), typeof(DominionHistory).GetMethod(nameof(DominionHistory.FinishedRoundWonInSuddenDeath))));

        // ---------------------------------------------------------------- the match log

        [Test] public void TheMatchLogSaysTheSharedRoundWentToSuddenDeath()
        {
            DominionRoomState room = RoundTwoSuddenDeath();
            var notes = DominionMarkerNotes.ForEdge(true, 2, DominionStage.Overtime, 0, room, Two, new[] { 1, 0, 0 });
            CollectionAssert.Contains(notes, DominionMarkerNotes.RoundToSuddenDeath(2, Two, Two, room.Points));
            CollectionAssert.Contains(notes, DominionMarkerNotes.SuddenDeathStart(Two));
            CollectionAssert.DoesNotContain(notes, DominionMarkerNotes.RoundEnd(2, -1, Two, room.Points), "not a tied round");
        }

        [Test] public void TheMatchLogSaysWhoWonTheRoundInSuddenDeath()
        {
            var room = new DominionRoomState
            {
                HasRound = true, Round = 3, Stage = DominionStage.Break, Points = new[] { 500, 450, 0 }, Wins = new[] { 1, 1, 0 }, Winner = -1,
                HistoryWinners = new[] { 0, DominionHistory.EncodeSuddenDeathWinner(1) },
            };
            var notes = DominionMarkerNotes.ForEdge(true, 2, DominionStage.SuddenDeath, 100000, room, Two, new[] { 1, 0, 0 });
            CollectionAssert.Contains(notes, DominionMarkerNotes.RoundWonInSuddenDeath(2, 1, Two, room.Points));
            CollectionAssert.Contains(notes, DominionMarkerNotes.BreakStart(3));
        }

        [Test] public void AMatchSuddenDeathAfterARoundsSuddenDeathIsLoggedAsAStartNotAReplay()
        {
            DominionRoomState room = RoundTwoSuddenDeath();
            room.Round = 3; room.SuddenDeathRound = 0; room.SuddenDeathMs = 114000; room.Wins = new[] { 1, 1, 0 };
            var notes = DominionMarkerNotes.ForEdge(true, 3, DominionStage.SuddenDeath, 100000, room, Two, new[] { 1, 0, 0 });
            CollectionAssert.Contains(notes, DominionMarkerNotes.SuddenDeathStart(Two));
            CollectionAssert.DoesNotContain(notes, DominionMarkerNotes.SuddenDeathReplay(Two));
        }
    }
}
