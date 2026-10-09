using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 18 Part 0: the review fixes of Task 16 (overtime) and Task 17 (shield leaves the spawn, score bars), and Tudor's A56 (an overtime
    /// team with nobody left drops out of the overtime). Rules are pure with literal numbers; where a component must call a rule, the wiring is read from
    /// the method bodies (IlWiring) and the decision tests also check that the answer is acted on.</summary>
    public class DominionTask18Part0Tests
    {
        private const int Lead = 200;
        private static readonly int[] Three = { 0, 1, 2 };
        private static readonly int[] Two = { 0, 1 };

        private static MethodInfo Rule(System.Type type, string name) => type.GetMethod(name);

        // ---------------------------------------------------------------- 1: the break card's finished round

        private static DominionRoomState Break(int round, int[] history, int[] winners) =>
            new DominionRoomState { HasRound = true, Round = round, Stage = DominionStage.Break, History = history, HistoryWinners = winners, Points = new int[3], Wins = new int[3], Winner = -1 };

        [Test] public void TheBreakAfterRoundOneNamesRoundOnesWinner()
        {
            var state = Break(2, new[] { 540, 620, 0 }, new[] { 1 });
            CollectionAssert.AreEqual(new[] { 1 }, DominionHistory.WinnersOfFinishedRound(state));
        }

        [Test] public void TheBreakAfterRoundTwoNamesRoundTwosWinnerNotRoundOnes()
        {
            var state = Break(3, new[] { 540, 620, 0, 700, 100, 0 }, new[] { 1, 0 });
            CollectionAssert.AreEqual(new[] { 0 }, DominionHistory.WinnersOfFinishedRound(state));
        }

        [Test] public void TheFirstBreakFollowsNoRoundSoItNamesNobody()
        {
            Assert.IsEmpty(DominionHistory.WinnersOfFinishedRound(Break(1, null, null)));
            Assert.IsEmpty(DominionHistory.WinnersOfFinishedRound(Break(1, new[] { 540, 620, 0 }, new[] { 1 })), "even if the room somehow holds a row already");
        }

        [Test] public void ASharedRoundNamesBothWinnersOnTheBreakAfterIt()
        {
            var state = Break(3, new[] { 540, 620, 0, 500, 500, 100 }, new[] { 1, DominionHistory.EncodeWinners(new[] { 0, 1 }) });
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionHistory.WinnersOfFinishedRound(state));
        }

        [Test] public void ARoomFromBeforeTheWinnersRecordFallsBackToThePointsLeaderOfTheFinishedRound()
        {
            var state = Break(3, new[] { 540, 620, 0, 700, 100, 0 }, null);
            CollectionAssert.AreEqual(new[] { 0 }, DominionHistory.WinnersOfFinishedRound(state));
        }

        [Test] public void TheHudHostReadsTheFinishedRoundThroughTheTestedRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(DominionHud), "ReadRoom", Rule(typeof(DominionHistory), nameof(DominionHistory.WinnersOfFinishedRound))));
            Assert.IsFalse(IlWiring.Uses(typeof(DominionHud), "ReadRoom", Rule(typeof(DominionHistory), nameof(DominionHistory.WinnersOfRound))), "no inline Round - 2 any more");
            Assert.IsTrue(IlWiring.Uses(typeof(DominionHistory), nameof(DominionHistory.WinnersOfFinishedRound), Rule(typeof(DominionHistory), nameof(DominionHistory.WinnersOfRound))));
        }

        // ---------------------------------------------------------------- the master's writes (shared by 2 and 3)

        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers
        {
            RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, OvertimeSeconds = 60f, OvertimeLeadPoints = Lead,
            SuddenDeathCountdownSeconds = 5f, SameInstantToleranceMs = 50,
        };

        private static DominionRoomState Room(DominionStage stage, int end, int[] points, int[] otTeams = null, int round = 1) =>
            new DominionRoomState
            {
                HasRound = true, Round = round, Stage = stage, EndMs = end, Points = points, Wins = new[] { 0, 0, 0 }, Winner = -1, OvertimeTeams = otTeams, PointsSeq = 7,
            };

        private static DominionWrite Next(DominionRoomState room, int now, int[] players) =>
            DominionRoomWrites.Next(true, true, now, room, Cfg, Three, players);

        // ---------------------------------------------------------------- 2: the lead check is for overtime only

        [Test] public void ATwoHundredPlusGapBeforeTheBuzzerOfANormalRoundWritesNothing()
        {
            // 500 / 300 / 0 is a full lead (200 and more) but the clock has 20 s left: only the buzzer decides a round, never the overtime's early win
            Assert.IsNull(Next(Room(DominionStage.Round, 100000, new[] { 500, 300, 0 }), 80000, new[] { 3, 3, 3 }));
            Assert.IsNull(Next(Room(DominionStage.Round, 100000, new[] { 900, 100, 0 }, round: 2), 99999, new[] { 3, 3, 3 }), "one ms before the buzzer");
        }

        [Test] public void TheSameGapInOvertimeEndsTheRoundAtOnce()
        {
            DominionWrite w = Next(Room(DominionStage.Overtime, 160000, new[] { 500, 300, 0 }, otTeams: Two), 130000, new[] { 3, 3, 3 });
            Assert.IsNotNull(w, "in an overtime the early win applies");
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
        }

        // ---------------------------------------------------------------- 3: A56, an overtime team that empties drops out

        [Test] public void AnEmptiedTeamIsLeftOutOfTheOvertimeTeams()
        {
            CollectionAssert.AreEqual(new[] { 1 }, DominionRules.OvertimeTeamsPresent(new[] { 0, 1 }, new[] { 0, 3, 3 }));
            CollectionAssert.AreEqual(new[] { 0, 2 }, DominionRules.OvertimeTeamsPresent(new[] { 0, 1, 2 }, new[] { 2, 0, 1 }));
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.OvertimeTeamsPresent(new[] { 0, 1 }, new[] { 1, 1, 0 }), "a team outside the overtime emptying changes nothing");
        }

        [Test] public void WithoutACountTheOvertimeTeamsStayAsTheyWere()
        {
            var teams = new[] { 0, 1 };
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.OvertimeTeamsPresent(teams, null));
            Assert.IsNull(DominionRules.OvertimeTeamsPresent(null, new[] { 3, 3, 3 }), "and no list stays no list");
        }

        [Test] public void ATeamIdBeyondTheCountsHasNobodyAndEveryoneGoneLeavesNone()
        {
            CollectionAssert.AreEqual(new[] { 0 }, DominionRules.OvertimeTeamsPresent(new[] { 0, 5 }, new[] { 2, 2, 2 }));
            Assert.IsEmpty(DominionRules.OvertimeTeamsPresent(new[] { 0, 1 }, new[] { 0, 0, 3 }));
        }

        [Test] public void PresentTeamsNeverEditTheInput()
        {
            var teams = new[] { 0, 1 };
            DominionRules.OvertimeTeamsPresent(teams, new[] { 0, 3, 3 });
            CollectionAssert.AreEqual(new[] { 0, 1 }, teams);
        }

        [Test] public void WhenOneOfTwoOvertimeTeamsEmptiesTheOtherWinsTheRoundAtOnceEvenOnFewerPoints()
        {
            // team 0 leads on points (700 v 600) but nobody of it is left: it cannot win; team 1 wins the round with 30 s of the overtime still to run
            DominionWrite w = Next(Room(DominionStage.Overtime, 160000, new[] { 700, 600, 100 }, otTeams: Two), 130000, new[] { 0, 3, 3 });
            Assert.IsNotNull(w);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
            CollectionAssert.AreEqual(new[] { 1 }, (int[])w.Props[DominionKeys.HistoryWinners]);
            Assert.IsNull(w.Props[DominionKeys.OvertimeTeams], "the overtime's team list goes");
            Assert.AreEqual(7, w.Expected[DominionKeys.PointsSeq]);
        }

        [Test] public void TheLastOvertimeTeamWinsAloneWhenTheClockRunsOutToo()
        {
            DominionWrite w = Next(Room(DominionStage.Overtime, 160000, new[] { 700, 600, 100 }, otTeams: Two), 160000, new[] { 3, 0, 3 });
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, (int[])w.Props[DominionKeys.Wins], "team 1 emptied: team 0 wins it alone, not a shared round");
            CollectionAssert.AreEqual(new[] { 0 }, (int[])w.Props[DominionKeys.HistoryWinners]);
        }

        [Test] public void ThreeOvertimeTeamsAndOneEmptiesTheOtherTwoCarryOnAndShareAtTheEnd()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 500, 500, 500 }, otTeams: Three);
            int[] players = { 3, 0, 3 };
            Assert.IsNull(Next(room, 130000, players), "two teams are still level inside the lead: the overtime runs");
            DominionWrite end = Next(room, 160000, players);
            CollectionAssert.AreEqual(new[] { 1, 0, 1 }, (int[])end.Props[DominionKeys.Wins], "the emptied team gets no win");
            Assert.AreEqual(DominionHistory.EncodeWinners(new[] { 0, 2 }), ((int[])end.Props[DominionKeys.HistoryWinners])[0]);
        }

        [Test] public void ThreeOvertimeTeamsOneEmptiesAndTheLeadAmongTheRestWinsAtOnce()
        {
            DominionWrite w = Next(Room(DominionStage.Overtime, 160000, new[] { 700, 500, 100 }, otTeams: Three), 130000, new[] { 3, 3, 0 });
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
        }

        [Test] public void ThreeOvertimeTeamsWhereTheEmptiedOneWasTheOnlyThingStoppingALeadStillNeedsTheFullLead()
        {
            // team 0 emptied; team 1 leads team 2 by 199: not yet
            Assert.IsNull(Next(Room(DominionStage.Overtime, 160000, new[] { 0, 699, 500 }, otTeams: Three), 130000, new[] { 0, 3, 3 }));
        }

        [Test] public void WhenTheMasterCouldNotCountPlayersTheOvertimeCarriesOnAsBefore()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 0 }, otTeams: Two);
            Assert.IsNull(Next(room, 130000, null));
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])Next(room, 160000, null).Props[DominionKeys.Wins]);
        }

        [Test] public void AllTheOvertimeTeamsGoneWhileAnotherTeamStaysEndsTheMatchForThatTeam()
        {
            DominionWrite w = Next(Room(DominionStage.Overtime, 160000, new[] { 520, 600, 0 }, otTeams: Two), 130000, new[] { 0, 0, 3 });
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage], "the A3 last-team rule comes first");
            Assert.AreEqual(2, w.Props[DominionKeys.Winner]);
        }

        [Test] public void NobodyLeftAnywhereScoresTheRoundForNobodyAndDoesNotThrow()
        {
            DominionWrite w = null;
            Assert.DoesNotThrow(() => w = Next(Room(DominionStage.Overtime, 160000, new[] { 520, 600, 0 }, otTeams: Two), 130000, new[] { 0, 0, 0 }));
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
        }

        [Test] public void ANormalRoundIsUnaffectedByATeamEmptying()
        {
            Assert.IsNull(Next(Room(DominionStage.Round, 100000, new[] { 300, 280, 0 }), 80000, new[] { 0, 3, 3 }));
        }

        [Test] public void TheMasterNarrowsTheOvertimeTeamsThroughTheTestedRuleAndActsOnTheAnswer()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), "Next", Rule(typeof(DominionRules), nameof(DominionRules.OvertimeTeamsPresent))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "Update", Rule(typeof(DominionRoomWrites), nameof(DominionRoomWrites.Next))), "the director asks the same Next");
        }

        // ---------------------------------------------------------------- 4: A55, a 0 lead turns overtime off

        [Test] public void TheOvertimeLeadMayBeZeroAndSaysWhatThatDoes()
        {
            FieldInfo field = typeof(DominionConfig).GetField("overtimeLeadPoints", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            Assert.AreEqual(0f, field.GetCustomAttribute<MinAttribute>().min, "0 is a valid lead: it turns overtime off");
            StringAssert.Contains("0 turns overtime off", field.GetCustomAttribute<TooltipAttribute>().tooltip);
            Assert.IsFalse(DominionRules.OvertimeOn(60f, 0));
        }

        // ---------------------------------------------------------------- 5: tests that could not fail

        private static DominionConfig Configured(System.Action<UnityEditor.SerializedObject> set)
        {
            DominionConfig config = ScriptableObject.CreateInstance<DominionConfig>();
            var so = new UnityEditor.SerializedObject(config);
            set(so);
            so.ApplyModifiedProperties();
            return config;
        }

        [Test] public void EachConfigNumberLandsInItsOwnFlowField()
        {
            DominionConfig config = Configured(so =>
            {
                so.FindProperty("roundsToWin").intValue = 4;
                so.FindProperty("maxRounds").intValue = 7;
                so.FindProperty("roundSeconds").floatValue = 111f;
                so.FindProperty("breakSeconds").floatValue = 22f;
                so.FindProperty("breakCountdownSeconds").floatValue = 3f;
                so.FindProperty("overtimeSeconds").floatValue = 44f;
                so.FindProperty("overtimeLeadPoints").intValue = 333;
                so.FindProperty("centreFirstPayoutSeconds").floatValue = 5.5f;
                so.FindProperty("centrePayoutIntervalSeconds").floatValue = 12f;
                so.FindProperty("sameInstantToleranceSeconds").floatValue = 0.25f;
            });
            try
            {
                DominionFlowNumbers n = DominionDirector.FlowNumbersOf(config, hasCentre: true);
                Assert.AreEqual(4, n.RoundsToWin);
                Assert.AreEqual(7, n.MaxRounds);
                Assert.AreEqual(111f, n.RoundSeconds);
                Assert.AreEqual(22f, n.BreakSeconds);
                Assert.AreEqual(44f, n.OvertimeSeconds, "the overtime's minute is the seconds, not the lead");
                Assert.AreEqual(333, n.OvertimeLeadPoints, "and the lead is the lead, not the seconds");
                Assert.AreEqual(3f, n.SuddenDeathCountdownSeconds);
                Assert.IsTrue(n.HasCentre);
                Assert.AreEqual(5500, n.CentreFirstMs);
                Assert.AreEqual(12000, n.CentreIntervalMs);
                Assert.AreEqual(250, n.SameInstantToleranceMs);
                Assert.IsFalse(DominionDirector.FlowNumbersOf(config, hasCentre: false).HasCentre);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test] public void TheDirectorHandsTheConfigToTheMastersRulesThroughTheTestedHandOver()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "Update", Rule(typeof(DominionDirector), nameof(DominionDirector.FlowNumbersOf))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), nameof(DominionDirector.FlowNumbersOf), typeof(DominionConfig).GetProperty(nameof(DominionConfig.OvertimeSeconds)).GetGetMethod()));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), nameof(DominionDirector.FlowNumbersOf), typeof(DominionConfig).GetProperty(nameof(DominionConfig.OvertimeLeadPoints)).GetGetMethod()));
        }

        [Test] public void TheDirectorActsOnTheStageRulesAnswersItAsks()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionDirector), "RunPoints", Rule(typeof(DominionPointsRules), nameof(DominionPointsRules.PointsRun))), "RunPoints stops unless points run");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionDirector), "WithLatestPoints", Rule(typeof(DominionRules), nameof(DominionRules.IsRoundPlay))));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionDirector), "OnCaptureWritten", Rule(typeof(DominionRules), nameof(DominionRules.IsRoundPlay))));
        }

        // The cards' wording is Tudor's (he edits it in the mode assets), so no test pins it; what is guarded is the rule of A54: the three revised
        // cards exist in both modes, once each, with words and no exact numbers.
        private static GameModeDefinition Mode(string mode)
        {
            var def = UnityEditor.AssetDatabase.LoadAssetAtPath<GameModeDefinition>("Assets/Gameplay/Config/Modes/" + mode + ".asset");
            Assert.IsNotNull(def, mode);
            return def;
        }

        [Test] public void BothModesCarryTheThreeRevisedCardsOnceEachWithNoExactNumbers()
        {
            foreach (string mode in new[] { "Dominion 2v2", "Dominion 3v3v3" })
                foreach (string title in new[] { "Rounds", "Sudden death", "Respawns" })
                {
                    var cards = Mode(mode).InfoCards.Where(c => c.title == title).ToList();
                    Assert.AreEqual(1, cards.Count, $"{mode}: one card titled {title}");
                    string text = cards[0].text;
                    Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{mode} {title}: has words");
                    Assert.IsFalse(text.Any(char.IsDigit), $"{mode} {title}: no exact numbers on a card (A54): {text}");
                }
        }

        // The overtime's buzzer in the points plan: a centre payout due by the overtime's end is paid, the per-second ticks stop.

        private static DominionTickInput OvertimeTick(int now, int lastTick, int centrePayoutMs) => new DominionTickInput
        {
            NowMs = now, LastTickMs = lastTick,
            Room = new DominionRoomState { HasRound = true, Round = 1, Stage = DominionStage.Overtime, EndMs = 160000, ResetFor = 100000, Points = new[] { 10, 10, 0 }, Wins = new int[3], Winner = -1 },
            BasePoints = new[] { 10, 10, 0 }, TeamsInMatch = Three,
            ZoneOwner = new[] { 0, 1, 2, 0 }, ZoneTier = new[] { 1, 1, 1, 2 }, IsSpawnZone = new[] { true, true, true, false }, PointsPerTier = new[] { 99, 5, 7, 99 },
            HasCentre = true, CentreOwner = 1, CentrePoints = 200, BaseCentreMs = centrePayoutMs, CentreFirstMs = 10000, CentreIntervalMs = 10000,
        };

        [Test] public void AtTheOvertimesEndACentrePayoutDueByThenIsPaidAndTheSecondTickIsNot()
        {
            // a points tick would also be due (1.1 s since the last one), but the clock has stopped: only the lump goes out
            DominionTickPlan plan = DominionPointsRules.Plan(OvertimeTick(now: 160000, lastTick: 158900, centrePayoutMs: 160000));
            Assert.IsNotNull(plan.Write);
            Assert.IsTrue(plan.CentrePaid);
            Assert.AreEqual(1, plan.CentreTeam);
            CollectionAssert.AreEqual(new[] { 10, 210, 0 }, (int[])plan.Write.Props[DominionKeys.Points], "team 1 +200, and team 0's zone second is not added");
            Assert.AreEqual((int)DominionStage.Overtime, plan.Write.Expected[DominionKeys.Stage]);
        }

        [Test] public void AtTheOvertimesEndWithNoPayoutDueNothingIsPaidNotEvenTheSecondTick()
        {
            DominionTickPlan plan = DominionPointsRules.Plan(OvertimeTick(now: 160000, lastTick: 158900, centrePayoutMs: 170000));
            Assert.IsNull(plan.Write, "the next payout falls after the end: nothing, and the zone second is not paid either");
            Assert.IsFalse(plan.CentrePaid);
        }

        [Test] public void APayoutThatFellDueAfterTheOvertimesEndIsNeverPaid()
        {
            DominionTickPlan plan = DominionPointsRules.Plan(OvertimeTick(now: 161500, lastTick: 158900, centrePayoutMs: 161000));
            Assert.IsNull(plan.Write);
            Assert.IsFalse(plan.CentrePaid);
        }

        [Test] public void JustBeforeTheOvertimesEndTheTicksStillPay()
        {
            DominionTickPlan plan = DominionPointsRules.Plan(OvertimeTick(now: 159900, lastTick: 158900, centrePayoutMs: 170000));
            CollectionAssert.AreEqual(new[] { 15, 10, 0 }, (int[])plan.Write.Props[DominionKeys.Points]);
        }

        // ---------------------------------------------------------------- 6: the centre label and the overtime's end

        [Test] public void TheCentreLabelHidesWhenTheNextPayoutFallsAfterTheOvertimesEnd()
        {
            Assert.IsTrue(DominionHudText.CentreLabelShown(true, DominionStage.Overtime, 159000, 160000, 150000), "payout inside the overtime");
            Assert.IsTrue(DominionHudText.CentreLabelShown(true, DominionStage.Overtime, 160000, 160000, 150000), "a payout exactly at the end is paid, so it shows");
            Assert.IsFalse(DominionHudText.CentreLabelShown(true, DominionStage.Overtime, 160001, 160000, 150000), "one ms after the end is never paid");
        }

        [Test] public void TheCentreLabelInARoundShowsAsBeforeWhateverTheNextPayoutIs()
        {
            Assert.IsTrue(DominionHudText.CentreLabelShown(true, DominionStage.Round, 170000, 160000, 150000));
        }

        [Test] public void TheCentreLabelNeedsThreeTeamsAPayoutTimeAndAClock()
        {
            Assert.IsFalse(DominionHudText.CentreLabelShown(false, DominionStage.Round, 159000, 160000, 150000), "2v2 has no centre");
            Assert.IsFalse(DominionHudText.CentreLabelShown(true, DominionStage.Round, 0, 160000, 150000), "no payout written");
            Assert.IsFalse(DominionHudText.CentreLabelShown(true, DominionStage.Round, 159000, 160000, 0), "the clock has not synced");
        }

        [Test] public void TheHudHostShowsTheCentreLabelOnlyWhenTheTestedRuleSaysSo()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionHud), "RefreshCentre", Rule(typeof(DominionHudText), nameof(DominionHudText.CentreLabelShown))));
        }

        // ---------------------------------------------------------------- 7 and 8: the shield after it has been cleared

        [Test] public void OnceTheOwnerHasSentAClearTheOwnersShieldIsDownAlthoughThePropertyStillHoldsTheEnd()
        {
            Assert.IsTrue(RespawnShieldRules.OwnerShieldUp(propertyEndMs: 5000, ownEndMs: 5000, nowMs: 1000, clearSent: false));
            Assert.IsFalse(RespawnShieldRules.OwnerShieldUp(propertyEndMs: 5000, ownEndMs: 0, nowMs: 1000, clearSent: true), "the echo of the clear has not come back");
            Assert.IsFalse(RespawnShieldRules.OwnerShieldUp(propertyEndMs: 5000, ownEndMs: 5000, nowMs: 1000, clearSent: true));
        }

        [Test] public void WithNoClearSentTheOwnersShieldFollowsThePropertyAndTheOwnEndAsBefore()
        {
            Assert.IsTrue(RespawnShieldRules.OwnerShieldUp(5000, 0, 1000, false), "the echoed property alone");
            Assert.IsTrue(RespawnShieldRules.OwnerShieldUp(0, 5000, 1000, false), "the own end before the echo");
            Assert.IsFalse(RespawnShieldRules.OwnerShieldUp(5000, 5000, 5000, false), "ended on time");
            Assert.IsFalse(RespawnShieldRules.OwnerShieldUp(0, 0, 1000, false));
        }

        [Test] public void TheLeaveSpawnWatchLooksOnlyForAShieldWhoseClearIsNotSent()
        {
            Assert.IsTrue(RespawnShieldRules.WatchArmed(shieldUp: true, clearSent: false));
            Assert.IsFalse(RespawnShieldRules.WatchArmed(shieldUp: true, clearSent: true), "the drop is written once, not every frame until the echo");
            Assert.IsFalse(RespawnShieldRules.WatchArmed(shieldUp: false, clearSent: false));
            Assert.IsFalse(RespawnShieldRules.WatchArmed(shieldUp: false, clearSent: true));
        }

        private static FieldInfo ClearSentField() => typeof(RespawnShield).GetField("shieldClearSent", BindingFlags.Instance | BindingFlags.NonPublic);

        [Test] public void TheShieldComponentAsksTheTestedRulesAndRemembersItsClear()
        {
            Assert.IsNotNull(ClearSentField());
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "OwnerIsUp", Rule(typeof(RespawnShieldRules), nameof(RespawnShieldRules.OwnerShieldUp))), "the owner's answer is the tested rule");
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "OwnerIsUp", ClearSentField()), "and it is given the remembered clear");
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "Awake", Rule(typeof(RespawnShieldRules), nameof(RespawnShieldRules.WatchArmed))), "the watch is armed by the tested rule");
            Assert.IsTrue(IlWiring.Stores(typeof(RespawnShield), "ClearShield", ClearSentField()), "every clear (a hit on an enemy, leaving the spawn, a death) is remembered");
        }

        [Test] public void AnewShieldResetsTheRememberedClearSoASecondRespawnDropsOnLeavingToo()
        {
            Assert.IsTrue(IlWiring.Stores(typeof(RespawnShield), "StartShield", ClearSentField()), "StartShield sets the flag back to false");
        }

        // ---------------------------------------------------------------- 9: the score bars

        [Test] public void FillsIntoWritesTheSameNumbersAsFillsIntoAReusedBuffer()
        {
            var buffer = new float[] { 9f, 9f, 9f, 9f };
            DominionScoreBarRules.FillsInto(new[] { 0, 1, 2 }, new[] { 20, 80, 40 }, buffer);
            CollectionAssert.AreEqual(new[] { 0.25f, 1f, 0.5f, 9f }, buffer, "three teams written, the spare slot left alone");
            DominionScoreBarRules.FillsInto(new[] { 0, 1 }, new[] { 10, 20, 999 }, buffer);
            Assert.AreEqual(0.5f, buffer[0], 1e-5f);
            Assert.AreEqual(1f, buffer[1], 1e-5f);
            CollectionAssert.AreEqual(DominionScoreBarRules.Fills(new[] { 0, 1, 2 }, new[] { 5 }), Fill(new[] { 0, 1, 2 }, new[] { 5 }));
            Assert.DoesNotThrow(() => DominionScoreBarRules.FillsInto(new[] { 0, 1, 2 }, null, buffer));
            Assert.DoesNotThrow(() => DominionScoreBarRules.FillsInto(null, null, buffer));
            Assert.DoesNotThrow(() => DominionScoreBarRules.FillsInto(new[] { 0, 1, 2, 3, 4 }, new[] { 1, 2 }, new float[2]), "a buffer shorter than the teams never throws");
        }

        private static float[] Fill(int[] teams, int[] points)
        {
            var buffer = new float[teams.Length];
            DominionScoreBarRules.FillsInto(teams, points, buffer);
            return buffer;
        }

        [Test] public void TheBarsRefreshEveryFrameIntoOneKeptBufferAndNeverAllocateAnArray()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "Refresh", Rule(typeof(DominionScoreBarRules), nameof(DominionScoreBarRules.FillsInto))));
            Assert.IsFalse(IlWiring.Uses(typeof(ScoreBars), "Refresh", Rule(typeof(DominionScoreBarRules), nameof(DominionScoreBarRules.Fills))), "the allocating form is not used per frame");
        }

        [Test] public void TheGoldReadoutHeightIsOneNumberForTheHudAndTheBars()
        {
            Assert.AreEqual(58f, DominionScoreBarRules.GoldReadoutHeight(24f), 1e-4f, "two lines of body text plus the padding");
            Assert.IsTrue(IlWiring.Uses(typeof(DominionScoreBarRules), "GoldReadoutTop", Rule(typeof(DominionScoreBarRules), nameof(DominionScoreBarRules.GoldReadoutHeight))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHud), "BuildGoldCorner", Rule(typeof(DominionScoreBarRules), nameof(DominionScoreBarRules.GoldReadoutHeight))), "PlayerHud sizes the readout with the same rule");
        }

        [Test] public void TheBarsScaleWithTheHudLikeTheCornerTheySitOn()
        {
            MethodInfo setScale = typeof(Transform).GetProperty(nameof(Transform.localScale)).GetSetMethod();
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(ScoreBars), "Build", setScale), "the root is scaled");
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "Build", typeof(UiTheme).GetField(nameof(UiTheme.hudScale))), "by the theme's HUD scale");
        }
    }
}
