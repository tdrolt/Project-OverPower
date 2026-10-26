using ExitGames.Client.Photon;
using NUnit.Framework;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 2: what the master writes now, from the room's values and the clock. Every number is a literal made up for the
    /// test (a 100 s round, a 10 s break), never the asset's.</summary>
    public class DominionRoomWritesTests
    {
        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers { RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f };
        private static readonly int[] Three = { 0, 1, 2 };
        private static readonly int[] Everyone = { 3, 3, 3 };

        private static DominionRoomState Room(int round, DominionStage stage, int endMs, int[] points = null, int[] wins = null) =>
            new DominionRoomState
            {
                HasRound = true, Round = round, Stage = stage, EndMs = endMs,
                Points = points ?? new[] { 0, 0, 0 }, Wins = wins ?? new[] { 0, 0, 0 }, Winner = -1,
            };

        private static DominionWrite Next(DominionRoomState room, int now, int[] players = null, bool dominion = true, bool live = true) =>
            DominionRoomWrites.Next(dominion, live, now, room, Cfg, Three, players ?? Everyone);

        /// <summary>A write that scores a round also expects the points sequence it built on (null while the room has had no points write).</summary>
        private static void AssertExpectsWhatItWasComputedFrom(DominionWrite w, DominionRoomState room, bool scoresRound = false)
        {
            Assert.AreEqual((int)room.Stage, w.Expected[DominionKeys.Stage]);
            Assert.AreEqual(room.Round, w.Expected[DominionKeys.Round]);
            Assert.AreEqual(room.EndMs, w.Expected[DominionKeys.StageEnd]);
            if (!scoresRound) { Assert.AreEqual(3, w.Expected.Count); return; }
            Assert.AreEqual(4, w.Expected.Count);
            Assert.IsTrue(w.Expected.ContainsKey(DominionKeys.PointsSeq));
            Assert.AreEqual(room.PointsSeq == 0 ? null : (object)room.PointsSeq, w.Expected[DominionKeys.PointsSeq]);
        }

        // ---- the start

        [Test] public void ALiveDominionRoomWithNoRoundWritesTheBreakBeforeRoundOneExpectingNoRound()
        {
            DominionWrite w = Next(default, 5000);
            Assert.IsNotNull(w);
            Assert.AreEqual(1, w.Props[DominionKeys.Round]);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage], "every match opens with the pick break");
            Assert.AreEqual(15000, w.Props[DominionKeys.StageEnd], "the break's length, not the round's");
            Assert.AreEqual(5000 + 10000, w.Props[DominionKeys.ZonesResetFor], "going live already reset the zones: the break needs no second reset");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.CentrePayout), "the centre starts paying when the round starts, not in the break");
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, (int[])w.Props[DominionKeys.Points]);
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
            Assert.AreEqual(-1, w.Props[DominionKeys.Winner]);
            Assert.AreEqual(1, w.Expected.Count);
            Assert.IsTrue(w.Expected.ContainsKey(DominionKeys.Round));
            Assert.IsNull(w.Expected[DominionKeys.Round]);
        }

        [Test] public void TheBreakBeforeRoundOneIsFollowedByRoundOneAndItsFreshZones()
        {
            DominionRoomState afterGoLive = DominionRoomState.Read(Next(default, 5000).Props);
            Assert.IsFalse(DominionRoomWrites.ZoneResetDue(afterGoLive), "no second reset on the first break");
            DominionWrite start = Next(afterGoLive, 15000);
            Assert.AreEqual((int)DominionStage.Round, start.Props[DominionKeys.Stage]);
            Assert.IsFalse(start.Props.ContainsKey(DominionKeys.Round), "still round 1");
            DominionRoomState inRound = afterGoLive;
            inRound.Stage = DominionStage.Round; inRound.EndMs = (int)start.Props[DominionKeys.StageEnd];
            Assert.IsTrue(DominionRoomWrites.ZoneResetDue(inRound), "the round's start resets the zones again, wiping anything done in the break");
        }

        [Test] public void AConquestRoomWritesNothing() => Assert.IsNull(Next(default, 5000, dominion: false));
        [Test] public void ARoomThatIsNotLiveWritesNothing() => Assert.IsNull(Next(default, 5000, live: false));
        [Test] public void AServerClockOfZeroWritesNothing()
        {
            Assert.IsNull(Next(default, 0));
            Assert.IsNull(Next(Room(1, DominionStage.Round, 100), 0));
        }

        // ---- a round ends

        [Test] public void ARoundEndedWithOneTeamOnTopGivesThatTeamTheWinAndStartsABreakForTheNextRound()
        {
            var room = Room(1, DominionStage.Round, 105000, points: new[] { 50, 200, 10 });
            DominionWrite w = Next(room, 105000);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(2, w.Props[DominionKeys.Round]);
            Assert.AreEqual(115000, w.Props[DominionKeys.StageEnd]);
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Points), "the round's final points stay for the break card");
            AssertExpectsWhatItWasComputedFrom(w, room, scoresRound: true);
        }

        [Test] public void ATiedRoundGivesNoWin()
        {
            var room = Room(1, DominionStage.Round, 105000, points: new[] { 90, 90, 10 }, wins: new[] { 1, 0, 0 });
            DominionWrite w = Next(room, 105500);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(2, w.Props[DominionKeys.Round]);
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
            AssertExpectsWhatItWasComputedFrom(w, room, scoresRound: true);
        }

        [Test] public void ATeamReachingTheTargetWinsTheMatchAndRoundThreeIsNotPlayed()
        {
            var room = Room(2, DominionStage.Round, 300000, points: new[] { 10, 300, 0 }, wins: new[] { 0, 1, 0 });
            DominionWrite w = Next(room, 300000);
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(1, w.Props[DominionKeys.Winner]);
            CollectionAssert.AreEqual(new[] { 0, 2, 0 }, (int[])w.Props[DominionKeys.Wins]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Round), "the round number does not move");
            AssertExpectsWhatItWasComputedFrom(w, room, scoresRound: true);
        }

        [Test] public void AfterTheLastRoundWithTopTeamsLevelSuddenDeathStartsWithNoClock()
        {
            var room = Room(3, DominionStage.Round, 500000, points: new[] { 5, 5, 0 }, wins: new[] { 1, 1, 0 });
            DominionWrite w = Next(room, 500000);
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(0, w.Props[DominionKeys.StageEnd]);
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Winner));
            AssertExpectsWhatItWasComputedFrom(w, room, scoresRound: true);
        }

        [Test] public void AfterTheLastRoundALoneLeaderOnRoundWinsWinsTheMatch()
        {
            // 1-0-0 after two tied rounds, round three tied too: nobody level with the leader, so no sudden death (A7).
            var room = Room(3, DominionStage.Round, 500000, points: new[] { 7, 7, 7 }, wins: new[] { 1, 0, 0 });
            DominionWrite w = Next(room, 500000);
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(0, w.Props[DominionKeys.Winner]);
        }

        [Test] public void AThirdRoundWinForTheTrailingTeamLevelsAllThreeIntoSuddenDeath()
        {
            var room = Room(3, DominionStage.Round, 500000, points: new[] { 0, 0, 40 }, wins: new[] { 1, 1, 0 });
            DominionWrite w = Next(room, 500000);
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, (int[])w.Props[DominionKeys.Wins]);
        }

        [Test] public void ScoringARoundExpectsTheRoomToStillHoldThePointsSequenceItWasScoredOn()
        {
            var room = Room(1, DominionStage.Round, 105000, points: new[] { 50, 200, 10 });
            room.PointsSeq = 41;
            DominionWrite w = Next(room, 105000);
            Assert.AreEqual(41, w.Expected[DominionKeys.PointsSeq], "a points write refused in between must make this stage write refused too");
            AssertExpectsWhatItWasComputedFrom(w, room, scoresRound: true);
        }

        [Test] public void EveryWayAScoredRoundCanEndExpectsThePointsSequence()
        {
            var matchWon = Room(2, DominionStage.Round, 300000, points: new[] { 10, 300, 0 }, wins: new[] { 0, 1, 0 });
            matchWon.PointsSeq = 7;
            var suddenDeath = Room(3, DominionStage.Round, 500000, points: new[] { 5, 5, 0 }, wins: new[] { 1, 1, 0 });
            suddenDeath.PointsSeq = 8;
            Assert.AreEqual(7, Next(matchWon, 300000).Expected[DominionKeys.PointsSeq]);
            Assert.AreEqual(8, Next(suddenDeath, 500000).Expected[DominionKeys.PointsSeq]);
        }

        // ---- a break ends

        [Test] public void ABreakThatEndedStartsTheRoundWithZeroedPointsAndANewEndTime()
        {
            var room = Room(2, DominionStage.Break, 115000, points: new[] { 50, 200, 10 }, wins: new[] { 0, 1, 0 });
            DominionWrite w = Next(room, 115200);
            Assert.AreEqual((int)DominionStage.Round, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(215200, w.Props[DominionKeys.StageEnd]);
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, (int[])w.Props[DominionKeys.Points]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Round));
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Wins));
            AssertExpectsWhatItWasComputedFrom(w, room);
        }

        // ---- nothing yet / nothing more

        [Test] public void NothingIsWrittenBeforeTheStageEnds()
        {
            Assert.IsNull(Next(Room(1, DominionStage.Round, 105000), 104999));
            Assert.IsNull(Next(Room(2, DominionStage.Break, 115000), 114999));
        }

        [Test] public void SuddenDeathAndOverAreNotAdvancedByTheClock()
        {
            Assert.IsNull(Next(Room(3, DominionStage.SuddenDeath, 0, wins: new[] { 1, 1, 0 }), 900000));
            Assert.IsNull(Next(Room(2, DominionStage.Over, 300000, wins: new[] { 0, 2, 0 }), 900000));
        }

        [Test] public void TheClockComparisonSurvivesTheIntWrap()
        {
            int end = unchecked(int.MaxValue - 500 + 1000); // wrapped to a large negative number
            Assert.Less(end, 0);
            var room = Room(1, DominionStage.Round, end, points: new[] { 1, 0, 0 });
            Assert.IsNull(Next(room, unchecked(end - 1)));
            DominionWrite w = Next(room, end);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(unchecked(end + 10000), w.Props[DominionKeys.StageEnd]);
        }

        // ---- A3: a team that empties keeps the match going; the last team with players wins

        [Test] public void OnlyOneTeamWithPlayersLeftEndsTheMatchWithThatTeam()
        {
            var room = Room(1, DominionStage.Round, 105000, wins: new[] { 0, 0, 0 });
            DominionWrite w = Next(room, 50000, players: new[] { 0, 2, 0 });
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(1, w.Props[DominionKeys.Winner]);
            AssertExpectsWhatItWasComputedFrom(w, room);
        }

        [Test] public void TheLastTeamStandingWinsInABreakAndInSuddenDeathToo()
        {
            Assert.AreEqual(2, Next(Room(2, DominionStage.Break, 115000), 100000, players: new[] { 0, 0, 1 }).Props[DominionKeys.Winner]);
            Assert.AreEqual(0, Next(Room(3, DominionStage.SuddenDeath, 0), 100000, players: new[] { 1, 0, 0 }).Props[DominionKeys.Winner]);
        }

        [Test] public void AnEmptyTeamAloneDoesNotEndTheMatch()
        {
            Assert.IsNull(Next(Room(1, DominionStage.Round, 105000), 50000, players: new[] { 0, 2, 3 }));
        }

        [Test] public void NobodyAtAllDecidesNothing()
        {
            Assert.IsNull(Next(Room(1, DominionStage.Round, 105000), 50000, players: new[] { 0, 0, 0 }));
        }

        [Test] public void AFinishedMatchWritesNothingEvenWithOneTeamLeft()
        {
            Assert.IsNull(Next(Room(2, DominionStage.Over, 300000, wins: new[] { 0, 2, 0 }), 400000, players: new[] { 0, 2, 0 }));
        }

        [Test] public void ATeamOutsideTheMatchIsNotCounted()
        {
            // Two-team match: team 2 is not in it, so a player there changes nothing; team 1 has nobody, so team 0 wins.
            DominionWrite w = DominionRoomWrites.Next(true, true, 50000, Room(1, DominionStage.Round, 105000), Cfg, new[] { 0, 1 }, new[] { 2, 0, 3 });
            Assert.AreEqual(0, w.Props[DominionKeys.Winner]);
        }

        // ---- reading the room

        [Test] public void TheRoomStateIsReadFromTheKeys()
        {
            var props = new Hashtable
            {
                { DominionKeys.Round, 2 }, { DominionKeys.Stage, (int)DominionStage.Break }, { DominionKeys.StageEnd, 777 },
                { DominionKeys.Points, new[] { 1, 2, 3 } }, { DominionKeys.Wins, new[] { 0, 1, 0 } }, { DominionKeys.Winner, -1 },
            };
            DominionRoomState s = DominionRoomState.Read(props);
            Assert.IsTrue(s.HasRound);
            Assert.AreEqual(2, s.Round);
            Assert.AreEqual(DominionStage.Break, s.Stage);
            Assert.AreEqual(777, s.EndMs);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, s.Points);
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, s.Wins);
            Assert.AreEqual(-1, s.Winner);
        }

        [Test] public void AnEmptyRoomHasNoRoundAndSafeArrays()
        {
            DominionRoomState s = DominionRoomState.Read(new Hashtable());
            Assert.IsFalse(s.HasRound);
            Assert.AreEqual(DominionStage.None, s.Stage);
            Assert.AreEqual(3, s.Points.Length);
            Assert.AreEqual(3, s.Wins.Length);
            Assert.AreEqual(-1, s.Winner);
        }

        // ---- the edges the clients react to

        [Test] public void AStageChangeIsAnEdgeOnlyAfterTheFirstRead()
        {
            Assert.AreEqual(DominionEdge.None, DominionRoomWrites.EdgeBetween(0, DominionStage.None, 1, DominionStage.Break), "the break before round 1: go-live already did the fresh start");
            Assert.AreEqual(DominionEdge.RoundStarted, DominionRoomWrites.EdgeBetween(1, DominionStage.Break, 1, DominionStage.Round), "round 1 starts after its break like any other");
            Assert.AreEqual(DominionEdge.BreakStarted, DominionRoomWrites.EdgeBetween(1, DominionStage.Round, 2, DominionStage.Break));
            Assert.AreEqual(DominionEdge.RoundStarted, DominionRoomWrites.EdgeBetween(2, DominionStage.Break, 2, DominionStage.Round));
            Assert.AreEqual(DominionEdge.None, DominionRoomWrites.EdgeBetween(2, DominionStage.Round, 2, DominionStage.Round));
            Assert.AreEqual(DominionEdge.None, DominionRoomWrites.EdgeBetween(3, DominionStage.Round, 3, DominionStage.SuddenDeath));
            Assert.AreEqual(DominionEdge.None, DominionRoomWrites.EdgeBetween(2, DominionStage.Break, 2, DominionStage.Over));
            Assert.AreEqual(DominionEdge.None, DominionRoomWrites.EdgeBetween(0, DominionStage.None, 2, DominionStage.Break), "a break that does not follow a round is no edge");
        }

        [Test] public void TheScoreboardKeepsCountingThroughTheBreakAndTheNextRound()
        {
            Assert.IsTrue(DominionRoomWrites.KeepsScoreboard(DominionEdge.BreakStarted), "the break's fresh start must not zero kills, deaths and damage");
            Assert.IsTrue(DominionRoomWrites.KeepsScoreboard(DominionEdge.RoundStarted));
        }
    }
}
