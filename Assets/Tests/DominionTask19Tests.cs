using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 19: a 0-0 round counts for nobody, a team that drops out of an overtime stays out, the mode info page's column count.
    /// Every number is a literal made up for the test, never the asset's.</summary>
    public class DominionTask19Tests
    {
        private const int Lead = 200;
        private static readonly int[] Three = { 0, 1, 2 };
        private static readonly int[] Two = { 0, 1 };

        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers
        {
            RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, OvertimeSeconds = 60f, OvertimeLeadPoints = Lead,
            SuddenDeathCountdownSeconds = 5f, SameInstantToleranceMs = 50,
        };

        private static DominionRoomState Room(DominionStage stage, int end, int[] points, int[] otTeams = null, int round = 1) =>
            new DominionRoomState
            {
                HasRound = true, Round = round, Stage = stage, EndMs = end, Points = points, Wins = new[] { 0, 0, 0 }, Winner = -1,
                OvertimeTeams = otTeams, PointsSeq = 7,
            };

        private static DominionWrite Next(DominionRoomState room, int now, int[] teams, int[] players, DominionFlowNumbers? cfg = null) =>
            DominionRoomWrites.Next(true, true, now, room, cfg ?? Cfg, teams, players);

        // ---------------------------------------------------------------- 1: a 0-0 round counts for nobody

        [Test] public void ANilNilBuzzerHasNoOvertimeAndNoWinner()
        {
            DominionRules.BuzzerResult r = DominionRules.AtBuzzer(new[] { 0, 0, 0 }, Three, Lead, true);
            Assert.IsNull(r.OvertimeTeams);
            Assert.IsEmpty(r.Winners);
        }

        [Test] public void ANilNilBuzzerOfATwoTeamMatchIgnoresTheUnusedSlot()
        {
            DominionRules.BuzzerResult r = DominionRules.AtBuzzer(new[] { 0, 0, 999 }, Two, Lead, true);
            Assert.IsNull(r.OvertimeTeams, "team 2 is not in this match, so its slot does not make the round a points round");
            Assert.IsEmpty(r.Winners);
        }

        [Test] public void OneTeamOnTenAndTheOthersOnNothingIsStillOvertime()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, DominionRules.AtBuzzer(new[] { 10, 0, 0 }, Three, Lead, true).OvertimeTeams);
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.AtBuzzer(new[] { 0, 10 }, Two, Lead, true).OvertimeTeams);
        }

        [Test] public void WithOvertimeOffANilNilBuzzerIsUnchangedANoWinnerRound()
        {
            DominionRules.BuzzerResult r = DominionRules.AtBuzzer(new[] { 0, 0, 0 }, Three, Lead, false);
            Assert.IsNull(r.OvertimeTeams);
            Assert.IsEmpty(r.Winners);
        }

        [Test] public void TheMasterEndsANilNilRoundInTheBreakWithNoWinAndATiedHistoryEntry()
        {
            DominionWrite w = Next(Room(DominionStage.Round, 100000, new[] { 0, 0, 0 }), 100000, Three, new[] { 3, 3, 3 });
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage], "no overtime stage");
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, (int[])w.Props[DominionKeys.Wins], "and nobody gets a round win");
            CollectionAssert.AreEqual(new[] { DominionHistory.EncodeWinners(new int[0]) }, (int[])w.Props[DominionKeys.HistoryWinners]);
            Assert.AreEqual(2, w.Props[DominionKeys.Round], "the next round follows");
        }

        // ---------------------------------------------------------------- 2: a team that drops out of an overtime stays out

        [Test] public void WhenATeamLeavesTheRunningOvertimeTheMasterWritesTheNarrowedList()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 540 }, otTeams: Three);
            DominionWrite w = Next(room, 130000, Three, new[] { 3, 3, 0 });
            Assert.IsNotNull(w);
            Assert.AreEqual(DominionRoomWrites.WhatOvertimeNarrowed, w.What);
            CollectionAssert.AreEqual(new[] { 0, 1 }, (int[])w.Props[DominionKeys.OvertimeTeams]);
            Assert.AreEqual(1, w.Props.Count, "only the team list changes");
            Assert.AreEqual((int)DominionStage.Overtime, w.Expected[DominionKeys.Stage], "guarded like every stage write: the stage, round and end it judged");
            Assert.AreEqual(1, w.Expected[DominionKeys.Round]);
            Assert.AreEqual(160000, w.Expected[DominionKeys.StageEnd]);
        }

        [Test] public void ARejoiningPlayerOfTheDroppedTeamDoesNotBringTheTeamBack()
        {
            var narrowed = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 540 }, otTeams: Two);
            Assert.IsNull(Next(narrowed, 131000, Three, new[] { 3, 3, 1 }), "team 2 is back in the room, but the stored list no longer has it: nothing to write");
            // and its points still cannot stop the others, nor win: the overtime is judged on the stored teams only
            var ahead = Room(DominionStage.Overtime, 160000, new[] { 520, 760, 9999 }, otTeams: Two);
            DominionWrite w = Next(ahead, 131000, Three, new[] { 3, 3, 1 });
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, (int[])w.Props[DominionKeys.Wins], "team 1 won it, a lead ahead of the other overtime team");
        }

        [Test] public void ARoomWithoutAStoredListNarrowsFromTheMatchTeams()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 540 }, otTeams: null);
            DominionWrite w = Next(room, 130000, Three, new[] { 0, 3, 3 });
            CollectionAssert.AreEqual(new[] { 1, 2 }, (int[])w.Props[DominionKeys.OvertimeTeams]);
        }

        [Test] public void NoNarrowingWriteWhenEveryOvertimeTeamIsStillPresent() =>
            Assert.IsNull(Next(Room(DominionStage.Overtime, 160000, new[] { 520, 600, 540 }, otTeams: Three), 130000, Three, new[] { 3, 3, 3 }));

        [Test] public void ALeadAmongTheRemainingTeamsWinsTheRoundInsteadOfNarrowing()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 760, 540 }, otTeams: Three);
            DominionWrite w = Next(room, 130000, Three, new[] { 3, 3, 0 });
            Assert.AreNotEqual(DominionRoomWrites.WhatOvertimeNarrowed, w.What);
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
        }

        [Test] public void NarrowingNeverWritesAnEmptyOrOneTeamList()
        {
            Assert.IsFalse(DominionRules.OvertimeNeedsNarrowing(Three, new int[0]));
            Assert.IsFalse(DominionRules.OvertimeNeedsNarrowing(Three, new[] { 1 }), "one team left has simply won");
            Assert.IsFalse(DominionRules.OvertimeNeedsNarrowing(Three, null));
            Assert.IsFalse(DominionRules.OvertimeNeedsNarrowing(Three, Three));
            Assert.IsTrue(DominionRules.OvertimeNeedsNarrowing(Three, Two));
        }

        [Test] public void NoPresentTeamAtAllFallsThroughToTheClockInsteadOfScoringAnEmptyRound()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 540 }, otTeams: Three);
            Assert.IsNull(Next(room, 130000, Three, new[] { 0, 0, 0 }), "before the clock, nothing");
        }

        [Test] public void TheMasterAsksTheNarrowingRuleAndActsOnTheAnswer()
        {
            MethodInfo rule = typeof(DominionRules).GetMethod(nameof(DominionRules.OvertimeNeedsNarrowing));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionRoomWrites), nameof(DominionRoomWrites.Next), rule));
        }

        [Test] public void OnlyTheTeamsStillInTheOvertimeAreShownDuringIt()
        {
            CollectionAssert.AreEqual(Two, DominionRules.TeamsShownIn(DominionStage.Overtime, Three, Two));
            CollectionAssert.AreEqual(Three, DominionRules.TeamsShownIn(DominionStage.Overtime, Three, null), "no stored list: all of them");
            CollectionAssert.AreEqual(Three, DominionRules.TeamsShownIn(DominionStage.Overtime, Three, new int[0]));
            CollectionAssert.AreEqual(Three, DominionRules.TeamsShownIn(DominionStage.Round, Three, Two), "a leftover list outside overtime is ignored");
        }

        [Test] public void TheHudShowsTheTeamsThroughTheTestedRule()
        {
            MethodInfo rule = typeof(DominionRules).GetMethod(nameof(DominionRules.TeamsShownIn));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionHud), "Update", rule));
        }

        // ---------------------------------------------------------------- 3: the info page's column count

        [Test] public void AModeWithItsOwnColumnCountUsesItAndTheOthersUseTheThemes()
        {
            Assert.AreEqual(4, ModeInfoPanel.ColumnsFor(7, 3, 4), "the mode's own count");
            Assert.AreEqual(3, ModeInfoPanel.ColumnsFor(7, 3, 0), "0 = the theme's count");
            Assert.AreEqual(2, ModeInfoPanel.ColumnsFor(2, 3, 4), "never more columns than cards");
            Assert.AreEqual(1, ModeInfoPanel.ColumnsFor(5, 0, 0), "never fewer than one column");
        }

        [Test] public void ThePanelBuildsItsRowsFromTheColumnRule()
        {
            MethodInfo rule = typeof(ModeInfoPanel).GetMethod(nameof(ModeInfoPanel.ColumnsFor));
            Assert.IsTrue(IlWiring.Uses(typeof(ModeInfoPanel), "BuildCards", rule), "the cards are laid out with the tested rule");
        }
    }
}
