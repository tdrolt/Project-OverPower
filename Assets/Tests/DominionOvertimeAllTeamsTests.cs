using NUnit.Framework;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>Overtime for every team: a close top starts it, all teams of the match play, a team ahead of every other by the lead wins at once,
    /// and the minute running out shares the round among the teams within the lead of the top. Literal numbers (a lead of 200), never the asset's.</summary>
    public class DominionOvertimeAllTeamsTests
    {
        private const int Lead = 200;
        private static readonly int[] Three = { 0, 1, 2 };
        private static readonly int[] Two = { 0, 1 };

        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers
        {
            RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, OvertimeSeconds = 60f, OvertimeLeadPoints = Lead,
            SuddenDeathCountdownSeconds = 5f, SameInstantToleranceMs = 50,
        };

        private static DominionRoomState Room(DominionStage stage, int[] points, int[] otTeams = null) => new DominionRoomState
        {
            HasRound = true, Round = 1, Stage = stage, EndMs = stage == DominionStage.Round ? 100000 : 160000, Points = points,
            Wins = new[] { 0, 0, 0 }, Winner = -1, OvertimeTeams = otTeams, PointsSeq = 7,
        };

        private static DominionWrite Next(DominionRoomState room, int now, int[] teamsInMatch, int[] players) =>
            DominionRoomWrites.Next(true, true, now, room, Cfg, teamsInMatch, players);

        // ---------------------------------------------------------------- the rules

        [Test] public void WhiteEightHundredPurpleSevenHundredCyanFourFiftyAtTheBuzzerSendsAllThreeTeamsToOvertime()
        {
            DominionRules.BuzzerResult r = DominionRules.AtBuzzer(new[] { 800, 700, 450 }, Three, Lead, true);
            CollectionAssert.AreEqual(Three, r.OvertimeTeams);
            Assert.IsNull(r.Winners);
        }

        [Test] public void WhiteTwoHundredAheadOfPurpleAndFarAheadOfCyanWinsAtOnceInOvertime() =>
            Assert.AreEqual(0, DominionRules.OvertimeLeader(new[] { 900, 700, 450 }, Three, Lead));

        [Test] public void WhiteTwoHundredAheadOfPurpleButNotOfCyanHasNoInstantWin() =>
            Assert.AreEqual(-1, DominionRules.OvertimeLeader(new[] { 900, 700, 750 }, Three, Lead), "only 150 ahead of Cyan");

        [Test] public void CyanClimbingWithinTheLeadSharesTheRoundWhenTheMinuteRunsOut() =>
            CollectionAssert.AreEqual(Three, DominionRules.AtOvertimeEnd(new[] { 800, 700, 601 }, Three, Lead));

        [Test] public void CyanStayingAFullLeadBehindDoesNotShare()
        {
            CollectionAssert.AreEqual(Two, DominionRules.AtOvertimeEnd(new[] { 800, 700, 450 }, Three, Lead));
            CollectionAssert.AreEqual(Two, DominionRules.AtOvertimeEnd(new[] { 800, 700, 600 }, Three, Lead), "exactly a lead behind is out, as at the buzzer");
        }

        [Test] public void AtTheEndATeamAheadOfEveryOtherByTheLeadWinsAloneAndIsNotShared() =>
            CollectionAssert.AreEqual(new[] { 0 }, DominionRules.AtOvertimeEnd(new[] { 900, 700, 450 }, Three, Lead));

        [Test] public void ADroppedTeamIsNotInTheListSoItNeverSharesWhateverItsPoints() =>
            CollectionAssert.AreEqual(Two, DominionRules.AtOvertimeEnd(new[] { 800, 700, 790 }, Two, Lead));

        [Test] public void ATwoTeamBuzzerAndOvertimeEndBehaveAsBefore()
        {
            CollectionAssert.AreEqual(Two, DominionRules.AtBuzzer(new[] { 500, 450, 0 }, Two, Lead, true).OvertimeTeams);
            CollectionAssert.AreEqual(Two, DominionRules.AtOvertimeEnd(new[] { 520, 600, 0 }, Two, Lead));
            CollectionAssert.AreEqual(new[] { 1 }, DominionRules.AtOvertimeEnd(new[] { 520, 720, 0 }, Two, Lead));
        }

        // ---------------------------------------------------------------- the master's writes

        [Test] public void TheBuzzerWriteOfTheExampleStartsOvertimeWithAllThreeTeams()
        {
            DominionWrite w = Next(Room(DominionStage.Round, new[] { 800, 700, 450 }), 100000, Three, new[] { 3, 3, 3 });
            Assert.AreEqual((int)DominionStage.Overtime, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(Three, (int[])w.Props[DominionKeys.OvertimeTeams]);
        }

        [Test] public void WhitePullingTheLeadAheadOfBothWinsTheRoundAtOnce()
        {
            DominionWrite w = Next(Room(DominionStage.Overtime, new[] { 900, 700, 450 }, Three), 130000, Three, new[] { 3, 3, 3 });
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
        }

        [Test] public void WhiteAheadOfPurpleByTheLeadButNotOfCyanWritesNothingWhileTimeIsLeft() =>
            Assert.IsNull(Next(Room(DominionStage.Overtime, new[] { 900, 700, 750 }, Three), 130000, Three, new[] { 3, 3, 3 }));

        [Test] public void CyanWithinTheLeadSharesAtTheEndAndCyanFarBehindDoesNot()
        {
            DominionWrite shared = Next(Room(DominionStage.Overtime, new[] { 800, 700, 601 }, Three), 160000, Three, new[] { 3, 3, 3 });
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, (int[])shared.Props[DominionKeys.Wins]);
            DominionWrite far = Next(Room(DominionStage.Overtime, new[] { 800, 700, 450 }, Three), 160000, Three, new[] { 3, 3, 3 });
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])far.Props[DominionKeys.Wins]);
        }

        [Test] public void ADroppedCyanIsNarrowedOutAndDoesNotShareEvenWhenItsPointsAreCloseAtTheEnd()
        {
            DominionWrite narrowed = Next(Room(DominionStage.Overtime, new[] { 800, 700, 790 }, Three), 130000, Three, new[] { 3, 3, 0 });
            Assert.AreEqual(DominionRoomWrites.WhatOvertimeNarrowed, narrowed.What);
            CollectionAssert.AreEqual(Two, (int[])narrowed.Props[DominionKeys.OvertimeTeams]);
            DominionWrite end = Next(Room(DominionStage.Overtime, new[] { 800, 700, 790 }, Two), 160000, Three, new[] { 3, 3, 0 });
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])end.Props[DominionKeys.Wins]);
        }

        [Test] public void ATwoTeamRoomStillStartsAndEndsOvertimeAsBefore()
        {
            int[] players = { 3, 3, 0 };
            DominionWrite start = Next(Room(DominionStage.Round, new[] { 500, 450, 0 }), 100000, Two, players);
            CollectionAssert.AreEqual(Two, (int[])start.Props[DominionKeys.OvertimeTeams]);
            DominionWrite win = Next(Room(DominionStage.Overtime, new[] { 520, 720, 0 }, Two), 130000, Two, players);
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, (int[])win.Props[DominionKeys.Wins]);
            DominionWrite shared = Next(Room(DominionStage.Overtime, new[] { 520, 600, 0 }, Two), 160000, Two, players);
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])shared.Props[DominionKeys.Wins]);
        }
    }
}
