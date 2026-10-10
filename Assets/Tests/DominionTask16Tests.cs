using ExitGames.Client.Photon;
using System.Linq;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 16 (Tudor A50, A54): a close round gets an extra minute, the first overtime team a lead ahead wins the round at once, an
    /// overtime that runs out is shared by every team still in it, a team a lead behind at the buzzer is out. Every number is a literal made up for
    /// the test (a lead of 200, 60 s of overtime), never the asset's.</summary>
    public class DominionTask16Tests
    {
        private const int Lead = 200;
        private static readonly int[] Three = { 0, 1, 2 };
        private static readonly int[] Two = { 0, 1 };

        // ---------------------------------------------------------------- the buzzer

        [Test] public void WithinTheLeadAtTheBuzzerTheTopTeamsPlayOvertimeAndNobodyHasWonYet()
        {
            DominionRules.BuzzerResult r = DominionRules.AtBuzzer(new[] { 500, 400, 100 }, Three, Lead, true);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, r.OvertimeTeams, "the top two are within the lead, so every team of the match plays");
            Assert.IsNull(r.Winners);
        }

        [Test] public void ALeadOfTheFullLeadOrMoreAtTheBuzzerWinsTheRoundOutright()
        {
            DominionRules.BuzzerResult r = DominionRules.AtBuzzer(new[] { 500, 300, 0 }, Three, Lead, true);
            CollectionAssert.AreEqual(new[] { 0 }, r.Winners, "exactly 200 ahead of everyone is enough");
            Assert.IsNull(r.OvertimeTeams);
        }

        [Test] public void OnePointShortOfTheLeadIsStillOvertime()
        {
            DominionRules.BuzzerResult r = DominionRules.AtBuzzer(new[] { 499, 300, 0 }, Three, Lead, true);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, r.OvertimeTeams);
        }

        [Test] public void ATeamTheFullLeadBehindTheTopIsNotWithinTheLead()
        {
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.TeamsWithinLead(new[] { 500, 450, 300 }, Three, Lead), "300 is 200 behind: out");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, DominionRules.TeamsWithinLead(new[] { 500, 450, 301 }, Three, Lead), "301 is 199 behind: in");
        }

        [Test] public void AnEqualTopIsOvertimeNotATiedRound() =>
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, DominionRules.AtBuzzer(new[] { 300, 300, 40 }, Three, Lead, true).OvertimeTeams);

        [Test] public void OnlyTheTeamsOfTheMatchCountAtTheBuzzer()
        {
            // A 2v2 room keeps team 2's slot at 0; it must neither be in overtime nor stop a clear lead from being clear.
            CollectionAssert.AreEqual(new[] { 0 }, DominionRules.AtBuzzer(new[] { 250, 0, 0 }, Two, Lead, true).Winners);
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.AtBuzzer(new[] { 10, 0, 0 }, Two, Lead, true).OvertimeTeams);
        }

        [Test] public void WithOvertimeOffTheTopTeamWinsAndATieCountsForNobody()
        {
            CollectionAssert.AreEqual(new[] { 1 }, DominionRules.AtBuzzer(new[] { 100, 130, 0 }, Three, Lead, false).Winners);
            DominionRules.BuzzerResult tie = DominionRules.AtBuzzer(new[] { 130, 130, 0 }, Three, Lead, false);
            Assert.IsEmpty(tie.Winners, "a tied round counts for nobody, as before overtime existed");
            Assert.IsNull(tie.OvertimeTeams);
        }

        [Test] public void OvertimeIsOnOnlyWithSecondsAndALead()
        {
            Assert.IsTrue(DominionRules.OvertimeOn(60f, 200));
            Assert.IsFalse(DominionRules.OvertimeOn(0f, 200));
            Assert.IsFalse(DominionRules.OvertimeOn(60f, 0));
        }

        // ---------------------------------------------------------------- inside overtime

        [Test] public void AnOvertimeTeamAheadOfTheOtherOvertimeTeamsByTheLeadWinsAtOnce() =>
            Assert.AreEqual(1, DominionRules.OvertimeLeader(new[] { 520, 720, 0 }, Two, Lead));

        [Test] public void OnePointShortOfTheLeadInOvertimeWinsNothingYet() =>
            Assert.AreEqual(-1, DominionRules.OvertimeLeader(new[] { 520, 719, 0 }, Two, Lead));

        [Test] public void TheLeadIsMeasuredAgainstEveryOtherOvertimeTeam() =>
            Assert.AreEqual(-1, DominionRules.OvertimeLeader(new[] { 700, 480, 560 }, Three, Lead), "200 ahead of one, only 140 ahead of the other");

        [Test] public void ATeamOutsideOvertimeNeitherWinsNorBlocksTheWin()
        {
            Assert.AreEqual(-1, DominionRules.OvertimeLeader(new[] { 300, 250, 900 }, Two, Lead), "team 2 is not in overtime: it cannot win the round");
            Assert.AreEqual(0, DominionRules.OvertimeLeader(new[] { 700, 400, 650 }, Two, Lead), "and its points do not stop an overtime team from getting the lead");
        }

        [Test] public void AnOvertimeThatRunsOutIsSharedByEveryTeamStillInIt() =>
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.AtOvertimeEnd(new[] { 520, 600, 0 }, Two, Lead));

        [Test] public void AnOvertimeThatRunsOutWithALeadAtTheFinalPointsIsWonNotShared() =>
            CollectionAssert.AreEqual(new[] { 1 }, DominionRules.AtOvertimeEnd(new[] { 520, 720, 0 }, Two, Lead));

        [Test] public void ASharedRoundGivesEachWinnerOneWinAndLeavesTheInputAlone()
        {
            int[] before = { 1, 0, 1 };
            int[] after = DominionRules.WinsAfterRound(before, new[] { 0, 1 });
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, after);
            CollectionAssert.AreEqual(new[] { 1, 0, 1 }, before);
        }

        [Test] public void NoWinnersAddNothing() => CollectionAssert.AreEqual(new[] { 1, 0, 1 }, DominionRules.WinsAfterRound(new[] { 1, 0, 1 }, new int[0]));

        // ---------------------------------------------------------------- after a round: a share on match point (A65 replaced "two reach the target together")

        [Test] public void TwoTeamsOnMatchPointSharingPlaySuddenDeathForTheRoundInsteadOfBothReachingTheTarget() =>
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.SharedRoundGoesToSuddenDeath(new[] { 1, 1, 0 }, new[] { 0, 1 }, 2));

        [Test] public void ThreeTeamsOnMatchPointSharingAllPlaySuddenDeathForTheRound() =>
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, DominionRules.SharedRoundGoesToSuddenDeath(new[] { 1, 1, 1 }, new[] { 2, 0, 1 }, 2));

        [Test] public void OneTeamAtTheTargetStillWinsTheMatchAtOnce()
        {
            RoundOutcome o = DominionRules.AfterRound(2, new[] { 2, 1, 1 }, 2, 3, Three);
            Assert.AreEqual(DominionStage.Over, o.Next);
            Assert.AreEqual(0, o.Winner);
        }

        [Test] public void AfterTheLastRoundASingleLeaderOnRoundWinsStillTakesTheMatchAndLevelTeamsPlaySuddenDeath()
        {
            // A7 unchanged by shared rounds.
            Assert.AreEqual(DominionStage.Over, DominionRules.AfterRound(3, new[] { 1, 0, 0 }, 2, 3, Three).Next);
            Assert.AreEqual(0, DominionRules.AfterRound(3, new[] { 1, 0, 0 }, 2, 3, Three).Winner);
            RoundOutcome level = DominionRules.AfterRound(3, new[] { 1, 1, 0 }, 2, 3, Three);
            Assert.AreEqual(DominionStage.SuddenDeath, level.Next);
            CollectionAssert.AreEqual(new[] { 0, 1 }, level.SuddenDeathTeams);
        }

        // ---------------------------------------------------------------- how a shared round is remembered (dHistW)

        [Test] public void OneWinnerIsStoredAsItsTeamIdLikeBefore() => Assert.AreEqual(2, DominionHistory.EncodeWinners(new[] { 2 }));

        [Test] public void NoWinnerIsStoredAsMinusOne() => Assert.AreEqual(-1, DominionHistory.EncodeWinners(new int[0]));

        [Test] public void SharedWinnersAreStoredAsTheSharedFlagPlusAMaskOfTheirTeams()
        {
            Assert.AreEqual(DominionHistory.SharedFlag | 0b101, DominionHistory.EncodeWinners(new[] { 0, 2 }));
            CollectionAssert.AreEqual(new[] { 0, 2 }, DominionHistory.DecodeWinners(DominionHistory.EncodeWinners(new[] { 0, 2 })));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, DominionHistory.DecodeWinners(DominionHistory.EncodeWinners(new[] { 2, 1, 0 })));
        }

        [Test] public void TheOldValuesStillReadAsTheyDid()
        {
            CollectionAssert.AreEqual(new[] { 0 }, DominionHistory.DecodeWinners(0));
            CollectionAssert.AreEqual(new[] { 2 }, DominionHistory.DecodeWinners(2));
            Assert.IsEmpty(DominionHistory.DecodeWinners(-1), "a tied round");
            Assert.IsEmpty(DominionHistory.DecodeWinners(DominionHistory.CutShort), "a round cut short");
        }

        [Test] public void TheWinnersOfARoundComeFromTheRoomsRecordAndFallBackToThePointsLeader()
        {
            int[] history = { 540, 620, 0, 500, 500, 100, 710, 700, 0 };
            int[] winners = { 1, DominionHistory.EncodeWinners(new[] { 0, 1 }) };
            CollectionAssert.AreEqual(new[] { 1 }, DominionHistory.WinnersOfRound(history, winners, 0));
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionHistory.WinnersOfRound(history, winners, 1), "shared");
            CollectionAssert.AreEqual(new[] { 0 }, DominionHistory.WinnersOfRound(history, winners, 2), "round 3 is not in the list: the points leader");
            Assert.IsEmpty(DominionHistory.WinnersOfRound(history, null, 1), "a room from before dHistW: a tie is a tie");
            Assert.IsEmpty(DominionHistory.WinnersOfRound(history, winners, 9), "a round that was not played");
        }

        // ---------------------------------------------------------------- the master's writes

        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers
        {
            RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, OvertimeSeconds = 60f, OvertimeLeadPoints = Lead,
            SuddenDeathCountdownSeconds = 5f, SameInstantToleranceMs = 50,
        };

        private static DominionRoomState Room(DominionStage stage, int end, int[] points, int[] wins = null, int round = 1, int[] otTeams = null, int[] history = null, int pointsSeq = 7) =>
            new DominionRoomState
            {
                HasRound = true, Round = round, Stage = stage, EndMs = end, Points = points, Wins = wins ?? new[] { 0, 0, 0 }, Winner = -1,
                OvertimeTeams = otTeams, History = history, PointsSeq = pointsSeq,
            };

        private static DominionWrite Next(DominionRoomState room, int now, DominionFlowNumbers? cfg = null, int[] players = null) =>
            DominionRoomWrites.Next(true, true, now, room, cfg ?? Cfg, Three, players ?? new[] { 3, 3, 3 });

        [Test] public void AClosePointsRoundAtTheBuzzerWritesOvertimeForEveryTeamOfTheMatch()
        {
            var room = Room(DominionStage.Round, 100000, new[] { 500, 400, 100 });
            DominionWrite w = Next(room, 100000);
            Assert.AreEqual((int)DominionStage.Overtime, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(160000, w.Props[DominionKeys.StageEnd], "now + the overtime minute");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, (int[])w.Props[DominionKeys.OvertimeTeams], "every team plays, the far-behind third too");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Wins), "nobody has won the round");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.History), "the round is not in the history yet");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Round), "still the same round");
            Assert.AreEqual(7, w.Expected[DominionKeys.PointsSeq], "built on the points the room has: refused if a points write got in between");
            Assert.AreEqual((int)DominionStage.Round, w.Expected[DominionKeys.Stage]);
        }

        [Test] public void ATwoHundredLeadAtTheBuzzerStillEndsTheRoundNormally()
        {
            DominionWrite w = Next(Room(DominionStage.Round, 100000, new[] { 500, 300, 0 }), 100000);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
            CollectionAssert.AreEqual(new[] { 0 }, (int[])w.Props[DominionKeys.HistoryWinners]);
        }

        [Test] public void NothingIsWrittenBeforeTheBuzzer() => Assert.IsNull(Next(Room(DominionStage.Round, 100000, new[] { 500, 400, 100 }), 99000));

        [Test] public void DuringOvertimeWithNoLeadAndTimeLeftNothingIsWritten() =>
            Assert.IsNull(Next(Room(DominionStage.Overtime, 160000, new[] { 520, 600, 100 }, otTeams: Two), 130000));

        [Test] public void TheMomentAnOvertimeTeamHasTheLeadTheRoundIsWonAtOnceAndOvertimeIsCleared()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 720, 100 }, otTeams: Two, history: null, round: 1);
            DominionWrite w = Next(room, 130000); // 30 s left on the overtime clock
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
            CollectionAssert.AreEqual(new[] { 1 }, (int[])w.Props[DominionKeys.HistoryWinners]);
            CollectionAssert.AreEqual(new[] { 520, 720, 100 }, (int[])w.Props[DominionKeys.History]);
            Assert.IsTrue(w.Props.ContainsKey(DominionKeys.OvertimeTeams));
            Assert.IsNull(w.Props[DominionKeys.OvertimeTeams], "null removes dOtT");
            Assert.AreEqual(7, w.Expected[DominionKeys.PointsSeq]);
            Assert.AreEqual(160000, w.Expected[DominionKeys.StageEnd]);
        }

        [Test] public void OnePointShortOfTheLeadInOvertimeWritesNothing() =>
            Assert.IsNull(Next(Room(DominionStage.Overtime, 160000, new[] { 520, 719, 100 }, otTeams: Two), 130000));

        [Test] public void WhenOvertimeRunsOutEveryTeamStillInItGetsARoundWin()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 100 }, otTeams: Two);
            DominionWrite w = Next(room, 160000);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(2, w.Props[DominionKeys.Round]);
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])w.Props[DominionKeys.Wins]);
            Assert.AreEqual(DominionHistory.EncodeWinners(new[] { 0, 1 }), ((int[])w.Props[DominionKeys.HistoryWinners])[0], "recorded as shared");
            Assert.IsNull(w.Props[DominionKeys.OvertimeTeams]);
        }

        [Test] public void ATeamOutsideOvertimeNeverGetsTheSharedWin()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 900 }, otTeams: Two);
            DominionWrite w = Next(room, 160000);
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])w.Props[DominionKeys.Wins], "team 2 has the most points now but was out at the buzzer");
        }

        [Test] public void AnOvertimeRoomWithoutItsTeamListPlaysBetweenTheMatchTeamsAndSharesToThoseWithinTheLead()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 0 }, otTeams: null);
            DominionWrite w = Next(room, 160000);
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, (int[])w.Props[DominionKeys.Wins], "team 2 is 600 behind: no share");
        }

        [Test] public void ASharedRoundAtOneOneSendsThoseTwoTeamsToSuddenDeathForTheRoundWithoutAddingWins()
        {
            // A65 replaced "2-2 after two shared rounds, then sudden death for the match": the share at 1-1 never hands out the wins.
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 0 }, wins: new[] { 1, 1, 0 }, round: 2, otTeams: Two, history: new[] { 400, 450, 0 });
            DominionWrite w = Next(room, 160000);
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Wins));
            CollectionAssert.AreEqual(new[] { 0, 1 }, (int[])w.Props[DominionKeys.SuddenDeathTeams]);
            Assert.IsNull(w.Props[DominionKeys.OvertimeTeams]);
        }

        [Test] public void ThreeTeamsOnMatchPointSharingARoundAllPlayItsSuddenDeath()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 300, 300, 300 }, wins: new[] { 1, 1, 1 }, round: 2, otTeams: Three, history: new[] { 400, 450, 0 });
            DominionWrite w = Next(room, 160000);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, (int[])w.Props[DominionKeys.SuddenDeathTeams]);
        }

        [Test] public void AnOvertimeWonByOneTeamCanTakeTheMatch()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 760, 0 }, wins: new[] { 0, 1, 0 }, round: 2, otTeams: Two);
            DominionWrite w = Next(room, 130000);
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(1, w.Props[DominionKeys.Winner]);
        }

        [Test] public void ALastTeamStandingDuringOvertimeCutsTheRoundShortAndClearsOvertime()
        {
            var room = Room(DominionStage.Overtime, 160000, new[] { 520, 600, 0 }, otTeams: Two);
            DominionWrite w = Next(room, 130000, players: new[] { 0, 3, 0 });
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(DominionHistory.CutShort, ((int[])w.Props[DominionKeys.HistoryWinners])[^1]);
            CollectionAssert.AreEqual(new[] { 520, 600, 0 }, (int[])w.Props[DominionKeys.History]);
            Assert.IsNull(w.Props[DominionKeys.OvertimeTeams]);
        }

        [Test] public void WithOvertimeSecondsAtZeroAClosePointsRoundIsDecidedAsBefore()
        {
            DominionFlowNumbers off = Cfg;
            off.OvertimeSeconds = 0f;
            DominionWrite w = Next(Room(DominionStage.Round, 100000, new[] { 500, 400, 100 }), 100000, off);
            Assert.AreEqual((int)DominionStage.Break, w.Props[DominionKeys.Stage]);
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, (int[])w.Props[DominionKeys.Wins]);
        }

        [Test] public void TheZonesAreNotResetForOvertime() =>
            Assert.IsFalse(DominionRoomWrites.ZoneResetDue(Room(DominionStage.Overtime, 160000, new[] { 1, 1, 0 }, otTeams: Two)));

        [Test] public void OvertimeEndingInABreakIsABreakStartAndOvertimeStartingIsNoEdge()
        {
            Assert.AreEqual(DominionEdge.BreakStarted, DominionRoomWrites.EdgeBetween(1, DominionStage.Overtime, 2, DominionStage.Break));
            Assert.AreEqual(DominionEdge.None, DominionRoomWrites.EdgeBetween(1, DominionStage.Round, 1, DominionStage.Overtime));
        }

        [Test] public void LateJoinersMayStillPickWhileOvertimeRuns() =>
            Assert.IsTrue(DominionShopRules.LateJoinerWindowOpen(DominionStage.Overtime));

        [Test] public void TheShopStaysClosedInOvertime() =>
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.Overtime, false));

        [Test] public void ARespawnIsAllowedInOvertime() =>
            Assert.IsTrue(DominionRules.RespawnAllowed(true, DominionStage.Overtime));

        [Test] public void TheNewStageIsAppendedWithoutRenumbering()
        {
            Assert.AreEqual(1, (int)DominionStage.Break);
            Assert.AreEqual(2, (int)DominionStage.Round);
            Assert.AreEqual(3, (int)DominionStage.SuddenDeath);
            Assert.AreEqual(4, (int)DominionStage.Over);
            Assert.AreEqual(5, (int)DominionStage.Overtime);
        }

        [Test] public void TheRoomReadsTheOvertimeTeams()
        {
            var props = new ExitGames.Client.Photon.Hashtable { { DominionKeys.Round, 1 }, { DominionKeys.Stage, 5 }, { DominionKeys.OvertimeTeams, new[] { 0, 2 } } };
            DominionRoomState room = DominionRoomState.Read(props);
            Assert.AreEqual(DominionStage.Overtime, room.Stage);
            CollectionAssert.AreEqual(new[] { 0, 2 }, room.OvertimeTeams);
            Assert.IsNull(DominionRoomState.Read(new ExitGames.Client.Photon.Hashtable { { DominionKeys.Round, 1 } }).OvertimeTeams);
        }

        // ---------------------------------------------------------------- points keep paying in overtime

        private static DominionTickInput Tick(DominionStage stage, int resetFor)
        {
            return new DominionTickInput
            {
                NowMs = 131000, LastTickMs = 130000,
                Room = new DominionRoomState { HasRound = true, Round = 1, Stage = stage, EndMs = 160000, ResetFor = resetFor, Points = new[] { 10, 10, 0 }, Wins = new int[3], Winner = -1 },
                BasePoints = new[] { 10, 10, 0 }, TeamsInMatch = Three,
                ZoneOwner = new[] { 0, 1, 2, 0 }, ZoneTier = new[] { 1, 1, 1, 2 }, IsSpawnZone = new[] { true, true, true, false }, PointsPerTier = new[] { 99, 5, 7, 99 },
            };
        }

        [Test] public void AZoneHeldInOvertimeKeepsPayingEvenThoughTheZonesWereNotResetForIt()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Tick(DominionStage.Overtime, resetFor: 100000));
            Assert.IsNotNull(p.Write, "dRz still names the round's end, not the overtime's");
            CollectionAssert.AreEqual(new[] { 15, 10, 0 }, (int[])p.Write.Props[DominionKeys.Points]);
            Assert.AreEqual((int)DominionStage.Overtime, p.Write.Expected[DominionKeys.Stage], "and the write expects the overtime stage");
        }

        [Test] public void NothingPaysInABreakOrSuddenDeath()
        {
            Assert.IsNull(DominionPointsRules.Plan(Tick(DominionStage.Break, resetFor: 160000)).Write);
            Assert.IsNull(DominionPointsRules.Plan(Tick(DominionStage.SuddenDeath, resetFor: 160000)).Write);
        }

        [Test] public void ARoundStillWaitsForItsZoneReset() =>
            Assert.IsNull(DominionPointsRules.Plan(Tick(DominionStage.Round, resetFor: 5)).Write);

        // ---------------------------------------------------------------- the match log

        [Test] public void OvertimeStartNamesTheRoundAndTheTeams() =>
            Assert.AreEqual("dominion round 2 overtime start teams 0,1", DominionMarkerNotes.OvertimeStart(2, new[] { 0, 1 }));

        [Test] public void ASharedRoundEndNamesTheTeamsThatShareItAndTheFinalPoints() =>
            Assert.AreEqual("dominion round 2 end shared teams 0,1 points team0 520 team1 600 team2 0",
                DominionMarkerNotes.RoundEndShared(2, new[] { 0, 1 }, Three, new[] { 520, 600, 0 }));

        [Test] public void RoundToOvertimeDropsTheOvertimeMarkerAndNoRoundEnd()
        {
            DominionRoomState room = Room(DominionStage.Overtime, 160000, new[] { 500, 400, 100 }, otTeams: Two);
            var notes = DominionMarkerNotes.ForEdge(true, 1, DominionStage.Round, 0, room, Three, new[] { 0, 0, 0 });
            CollectionAssert.AreEqual(new[] { "dominion round 1 overtime start teams 0,1" }, notes);
        }

        [Test] public void OvertimeSharedDropsTheSharedRoundEndThenTheBreak()
        {
            DominionRoomState room = Room(DominionStage.Break, 170000, new[] { 520, 600, 0 }, wins: new[] { 1, 1, 0 }, round: 2);
            room.HistoryWinners = new[] { DominionHistory.EncodeWinners(new[] { 0, 1 }) };
            var notes = DominionMarkerNotes.ForEdge(true, 1, DominionStage.Overtime, 0, room, Three, new[] { 0, 0, 0 });
            CollectionAssert.AreEqual(new[] { "dominion round 1 end shared teams 0,1 points team0 520 team1 600 team2 0", "dominion break start before round 2" }, notes);
        }

        [Test] public void OvertimeWonDropsTheOrdinaryWinnerLine()
        {
            DominionRoomState room = Room(DominionStage.Break, 170000, new[] { 520, 760, 0 }, wins: new[] { 0, 1, 0 }, round: 2);
            room.HistoryWinners = new[] { 1 };
            var notes = DominionMarkerNotes.ForEdge(true, 1, DominionStage.Overtime, 0, room, Three, new[] { 0, 0, 0 });
            Assert.AreEqual("dominion round 1 end winner team 1 points team0 520 team1 760 team2 0", notes[0]);
        }

        [Test] public void OvertimeThatEndsTheMatchDropsTheRoundEndBeforeMatchOver()
        {
            DominionRoomState room = Room(DominionStage.Over, 160000, new[] { 520, 760, 0 }, wins: new[] { 0, 2, 0 }, round: 2);
            room.HistoryWinners = new[] { 1, 1 };
            room.Winner = 1;
            var notes = DominionMarkerNotes.ForEdge(true, 2, DominionStage.Overtime, 0, room, Three, new[] { 0, 1, 0 });
            Assert.AreEqual(2, notes.Count);
            StringAssert.StartsWith("dominion round 2 end winner team 1", notes[0]);
            StringAssert.StartsWith("dominion match over winner team 1", notes[1]);
        }

        // ---------------------------------------------------------------- the screens' words

        private static readonly string[] Names = { "White", "Purple", "Cyan" };

        [Test] public void TheResultHeadlineAfterSharedRoundsStillReadsTheWinnersWinsFirst()
        {
            // Round 1 shared by white and purple (1-1-0), round 2 won by purple: PURPLE WINS 2-1-0 in a three-team match.
            Assert.AreEqual("PURPLE WINS 2–1–0", DominionHudText.ResultHeadline(1, new[] { 1, 2, 0 }, Three, Names, "{0} WINS {1}", "{0} WINS IN SUDDEN DEATH", "–", false));
        }

        // ---------------------------------------------------------------- the wiring: the game calls the tested rules

        private static System.Reflection.MethodInfo Rule(System.Type type, string name) => type.GetMethod(name);

        [Test] public void TheMasterDecidesTheBuzzerThroughTheTestedRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), "Next", Rule(typeof(DominionRules), nameof(DominionRules.AtBuzzer))));

        [Test] public void TheMasterEndsTheOvertimeThroughTheTestedRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), "Next", Rule(typeof(DominionRules), nameof(DominionRules.OvertimeLeader))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), "Next", Rule(typeof(DominionRules), nameof(DominionRules.AtOvertimeEnd))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), "ScoreRound", Rule(typeof(DominionRules), nameof(DominionRules.SharedRoundGoesToSuddenDeath))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), "ScoredRound", Rule(typeof(DominionRules), nameof(DominionRules.WinsAfterRound))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionRoomWrites), "ScoreRound", Rule(typeof(DominionHistory), nameof(DominionHistory.EncodeWinners))));
        }

        // The director's hand-over of the config numbers and its acting on the stage rules' answers: DominionTask18Part0Tests (they could not fail here).

        [Test] public void ThePointsPlanAsksTheSameStageRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(DominionPointsRules), "Plan", Rule(typeof(DominionPointsRules), nameof(DominionPointsRules.PointsRun))));

        [Test] public void TheRoundBarSaysTheThemesOvertimeWordAndTheHudHostGivesItTheOvertimeClock()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.RoundHud), "Refresh", typeof(Overpower.UI.UiTheme).GetField(nameof(Overpower.UI.UiTheme.dominionBarOvertimeText))));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.DominionHud), "Update", typeof(Overpower.UI.RoundHud).GetMethod(nameof(Overpower.UI.RoundHud.Refresh))));
        }

        [Test] public void TheBreakCardNamesTheWinnersTheRoomRecordedThroughTheTestedRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.DominionHud), "ReadRoom", Rule(typeof(DominionHistory), nameof(DominionHistory.WinnersOfFinishedRound))));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.BreakCard), Rule(typeof(DominionHudText), nameof(DominionHudText.SharedRoundLine))));
        }

        [Test] public void TheResultTableBoldsEveryRecordedWinner() =>
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.DominionResultPanel), Rule(typeof(DominionHistory), nameof(DominionHistory.WinnersOfRound))));

        [Test] public void TheDirectorDropsTheOvertimeMarkersThroughTheTestedNotes() =>
            Assert.IsTrue(IlWiring.Uses(typeof(DominionMarkerNotes), "ForEdge", Rule(typeof(DominionMarkerNotes), nameof(DominionMarkerNotes.OvertimeStart))));
    }
}
