using ExitGames.Client.Photon;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Task 8b: Tudor's sudden-death answers (A29 no healing, A30 the circle ignores Invulnerability, A31 only the exact same moment replays, judged by
    /// the death stamps) and the Task 8 review fixes (the A3 write is not held back, a late joiner is dead, what counts as alive). Every number is a literal
    /// made up for the test, never the asset's. The components call these functions; the recorder rows in the report name the proof of each call site.
    /// </summary>
    public class DominionTask8bTests
    {
        private static readonly int[] TiedTwo = { 0, 1 };
        private static readonly int[] TiedThree = { 0, 1, 2 };
        private const int Tolerance = 50;

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

        // ---- A31: who wins when everyone has fallen

        [Test] public void LastPlayersFallingAtTheSameStampReplaySuddenDeath()
        {
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 90000), Fell(1, 90000)), TiedTwo, Tolerance);
            Assert.AreEqual(SuddenDeathState.Replay, r.State);
            Assert.AreEqual(-1, r.Team);
        }

        [Test] public void FallsWithinTheToleranceCountAsTheSameMoment()
        {
            Assert.AreEqual(SuddenDeathState.Replay, SuddenDeathRules.Judge(TallyOf(Fell(0, 90000), Fell(1, 90050)), TiedTwo, Tolerance).State, "exactly the tolerance apart");
            Assert.AreEqual(SuddenDeathState.Replay, SuddenDeathRules.Judge(TallyOf(Fell(0, 90030), Fell(1, 90000)), TiedTwo, Tolerance).State, "either order");
        }

        [Test] public void TheTeamWhoseLastPlayerFellLaterWinsEvenByOneMillisecond()
        {
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 90000), Fell(1, 90001)), TiedTwo, 0);
            Assert.AreEqual(SuddenDeathState.Won, r.State, "1 ms later is beyond a zero tolerance");
            Assert.AreEqual(1, r.Team);
        }

        [Test] public void FallsJustBeyondTheToleranceAreNotTheSameMoment()
        {
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 90051), Fell(1, 90000)), TiedTwo, Tolerance);
            Assert.AreEqual(SuddenDeathState.Won, r.State);
            Assert.AreEqual(0, r.Team, "team 0 fell 51 ms after team 1: it lasted longer");
        }

        [Test] public void FallsAThirdOfASecondApartGoToTheLaterTeam()
        {
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 90300), Fell(1, 90000)), TiedTwo, Tolerance);
            Assert.AreEqual(SuddenDeathState.Won, r.State);
            Assert.AreEqual(0, r.Team);
        }

        [Test] public void ADeadPlayersMissingStampMeansWaitNotAGuess()
        {
            var noStamp = new SuddenDeathRules.Player { Team = 1, Counts = true, HasAliveFlag = true, AliveFlag = false };
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 90000), noStamp), TiedTwo, Tolerance);
            Assert.AreEqual(SuddenDeathState.Ongoing, r.State);
        }

        [Test] public void TheLastPlayerOfATeamIsTheLatestStampNotTheLastCounted()
        {
            // team 0 has two players, the one who fell at 95000 is its last; counted in either order
            SuddenDeathResult a = SuddenDeathRules.Judge(TallyOf(Fell(0, 95000), Fell(0, 80000), Fell(1, 90000)), TiedTwo, Tolerance);
            SuddenDeathResult b = SuddenDeathRules.Judge(TallyOf(Fell(0, 80000), Fell(0, 95000), Fell(1, 90000)), TiedTwo, Tolerance);
            Assert.AreEqual(0, a.Team);
            Assert.AreEqual(0, b.Team);
            Assert.AreEqual(SuddenDeathState.Won, a.State);
        }

        [Test] public void AnEarlierTeamDoesNotStopTheLastTwoReplayingTogether()
        {
            SuddenDeathResult same = SuddenDeathRules.Judge(TallyOf(Fell(0, 70000), Fell(1, 90000), Fell(2, 90010)), TiedThree, Tolerance);
            Assert.AreEqual(SuddenDeathState.Replay, same.State, "the two that fell last fell together");
            CollectionAssert.AreEqual(new[] { 1, 2 }, same.ReplayTeams, "A33: the team that fell earlier stays out of the replay");
            SuddenDeathResult later = SuddenDeathRules.Judge(TallyOf(Fell(0, 70000), Fell(1, 90300), Fell(2, 90000)), TiedThree, Tolerance);
            Assert.AreEqual(SuddenDeathState.Won, later.State);
            Assert.AreEqual(1, later.Team);
        }

        [Test] public void TheStampComparisonSurvivesTheServerClockWrap()
        {
            int justBeforeWrap = unchecked(int.MaxValue - 100);
            int justAfterWrap = unchecked(justBeforeWrap + 300); // wraps to a negative number, but is 300 ms later
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, justBeforeWrap), Fell(1, justAfterWrap)), TiedTwo, Tolerance);
            Assert.AreEqual(SuddenDeathState.Won, r.State);
            Assert.AreEqual(1, r.Team);
        }

        [Test] public void AnyoneStillAliveIsDecidedByTheCountAsBefore()
        {
            SuddenDeathResult won = SuddenDeathRules.Judge(TallyOf(Fell(0, 90000), Lives(1)), TiedTwo, Tolerance);
            Assert.AreEqual(SuddenDeathState.Won, won.State);
            Assert.AreEqual(1, won.Team);
            Assert.AreEqual(SuddenDeathState.Ongoing, SuddenDeathRules.Judge(TallyOf(Lives(0), Lives(1)), TiedTwo, Tolerance).State);
        }

        [Test] public void NobodyEverThereReplaysAndNullsDecideNothing()
        {
            Assert.AreEqual(SuddenDeathState.Replay, SuddenDeathRules.Judge(TallyOf(), TiedTwo, Tolerance).State);
            Assert.AreEqual(SuddenDeathState.Ongoing, SuddenDeathRules.Judge(null, TiedTwo, Tolerance).State);
            Assert.AreEqual(SuddenDeathState.Ongoing, SuddenDeathRules.Judge(TallyOf(Fell(0, 1)), new int[0], Tolerance).State);
        }

        [Test] public void ATeamsLastDeathIsTheLatestStampAcrossTheClockWrap()
        {
            int before = unchecked(int.MaxValue - 100);
            int after = unchecked(before + 300);
            SuddenDeathRules.Tally forward = TallyOf(Fell(0, before), Fell(0, after));
            SuddenDeathRules.Tally backward = TallyOf(Fell(0, after), Fell(0, before));
            Assert.AreEqual(after, forward.LastDeathMs[0], "the later stamp wins although it wrapped to a smaller number");
            Assert.AreEqual(after, backward.LastDeathMs[0], "in either order");
        }

        // ---- A33: only the teams whose last players fell together play again

        [Test] public void AReplayNamesOnlyTheTeamsThatFellTogether()
        {
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 70000), Fell(1, 90010), Fell(2, 90000)), TiedThree, Tolerance);
            Assert.AreEqual(SuddenDeathState.Replay, r.State);
            CollectionAssert.AreEqual(new[] { 1, 2 }, r.ReplayTeams);
        }

        [Test] public void AllThreeFallingTogetherReplayAllThree()
        {
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 90000), Fell(1, 90010), Fell(2, 90020)), TiedThree, Tolerance);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, r.ReplayTeams);
        }

        [Test] public void NobodyEverThereReplaysTheTiedTeams()
        {
            CollectionAssert.AreEqual(TiedTwo, SuddenDeathRules.Judge(TallyOf(), TiedTwo, Tolerance).ReplayTeams);
        }

        [Test] public void TheReplayWriteStoresTheNarrowedTeamsInTheRoom()
        {
            // 3v3v3, all three level in sudden death; team 0 fell earlier, teams 1 and 2 fell together
            DominionRoomState room = SuddenDeathRoom();
            room.Wins = new[] { 1, 1, 1 };
            room.SuddenDeathTeams = TiedThree;
            DominionWrite w = DominionRoomWrites.Next(true, true, 160000, room, Cfg, TiedThree, new[] { 2, 2, 2 }, TallyOf(Fell(0, 140000), Fell(1, 150000), Fell(2, 150020)));
            Assert.AreEqual(DominionRoomWrites.WhatSuddenDeathReplay, w.What);
            CollectionAssert.AreEqual(new[] { 1, 2 }, (int[])w.Props[DominionKeys.SuddenDeathTeams]);
        }

        [Test] public void AfterAReplayTheRoomsNarrowedTeamsDecideNotTheRoundWins()
        {
            // the wins say teams 0 and 1 are level, but the room's stored teams are 1 and 2: team 0 (alive) is not playing, so team 2 (fell last) wins
            DominionRoomState room = SuddenDeathRoom();
            room.SuddenDeathTeams = new[] { 1, 2 };
            DominionWrite w = DominionRoomWrites.Next(true, true, 160000, room, Cfg, TiedThree, new[] { 2, 2, 2 }, TallyOf(Lives(0), Fell(1, 150000), Fell(2, 150300)));
            Assert.AreEqual(DominionRoomWrites.WhatSuddenDeathWon, w.What);
            Assert.AreEqual(2, w.Props[DominionKeys.Winner]);
        }

        [Test] public void ARoomWithoutStoredTeamsFallsBackToTheTeamsLevelOnWins()
        {
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.TeamsPlayingSuddenDeath(null, new[] { 1, 1, 0 }, TiedThree));
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.TeamsPlayingSuddenDeath(new int[0], new[] { 1, 1, 0 }, TiedThree));
            CollectionAssert.AreEqual(new[] { 2 }, DominionRules.TeamsPlayingSuddenDeath(new[] { 2 }, new[] { 1, 1, 0 }, TiedThree));
        }

        [Test] public void SuddenDeathsStartStoresTheTeamsLevelOnWins()
        {
            // round 3 ends 0-0-0 in points: nobody wins it, the wins stay 1-1-0 -> sudden death for teams 0 and 1
            var room = new DominionRoomState { HasRound = true, Round = 3, Stage = DominionStage.Round, EndMs = 105000, Points = new[] { 0, 0, 0 }, Wins = new[] { 1, 1, 0 }, Winner = -1 };
            DominionWrite w = DominionRoomWrites.Next(true, true, 105000, room, Cfg, TiedThree, new[] { 2, 2, 2 });
            Assert.AreEqual("sudden death", w.What);
            CollectionAssert.AreEqual(new[] { 0, 1 }, (int[])w.Props[DominionKeys.SuddenDeathTeams]);
        }

        [Test] public void TheRoomStateReadsTheStoredTeams()
        {
            var props = new Hashtable { { DominionKeys.SuddenDeathTeams, new[] { 1, 2 } } };
            CollectionAssert.AreEqual(new[] { 1, 2 }, DominionRoomState.Read(props).SuddenDeathTeams);
            Assert.IsNull(DominionRoomState.Read(new Hashtable()).SuddenDeathTeams);
        }

        // ---- review item 2 (Task 8b): arriving dead never stamps "now"

        [Test] public void ALateArrivalGetsTheEarliestMomentNotNow()
        {
            int earliest = DominionRoomWrites.EarliestFallMs(100000, 4f, Tolerance);
            Assert.AreEqual(100000 - 4000 - Tolerance - 1, earliest);
            Assert.AreEqual(earliest, DominionRoomWrites.ArrivalDeathStamp(DominionStage.SuddenDeath, false, false, 0, earliest, 150000));
        }

        [Test] public void APlayerAlreadyDeadKeepsTheirRealStampWhenTheirBodyLoads()
        {
            Assert.AreEqual(123000, DominionRoomWrites.ArrivalDeathStamp(DominionStage.SuddenDeath, true, true, 123000, 95000, 150000));
        }

        [Test] public void ADeadPlayerWithNoStampYetGetsTheEarliestAndOutsideSuddenDeathItIsNow()
        {
            Assert.AreEqual(95000, DominionRoomWrites.ArrivalDeathStamp(DominionStage.SuddenDeath, true, false, 0, 95000, 150000));
            Assert.AreEqual(150000, DominionRoomWrites.ArrivalDeathStamp(DominionStage.Round, false, false, 0, 95000, 150000));
            Assert.AreEqual(150000, DominionRoomWrites.ArrivalDeathStamp(DominionStage.SuddenDeath, false, false, 0, 0, 150000), "no circle start known: the time now");
        }

        [Test] public void TheEarliestMomentIsNeverWithinTheToleranceOfARealFall()
        {
            int start = 100000;
            int earliest = DominionRoomWrites.EarliestFallMs(start, 4f, Tolerance);
            int firstPossibleFall = start - 4000; // the stage is written then; nobody falls before it
            Assert.Greater(firstPossibleFall - earliest, Tolerance);
            Assert.AreEqual(0, DominionRoomWrites.EarliestFallMs(0, 4f, Tolerance), "no circle start: none");
        }

        // ---- review item 5: the zone sort the circle uses

        [Test] public void TheCircleSortsEachZoneByTierCapitalAndTeamCount()
        {
            const int centreTier = 4;
            Assert.AreEqual(SuddenDeathRules.CircleZone.Centre, SuddenDeathRules.ClassifyZone(4, false, false, true, centreTier));
            Assert.AreEqual(SuddenDeathRules.CircleZone.Skip, SuddenDeathRules.ClassifyZone(4, false, false, false, centreTier), "a two-team match has no playing centre");
            Assert.AreEqual(SuddenDeathRules.CircleZone.Scoring, SuddenDeathRules.ClassifyZone(3, false, false, true, centreTier));
            Assert.AreEqual(SuddenDeathRules.CircleZone.Scoring, SuddenDeathRules.ClassifyZone(2, false, false, false, centreTier));
            Assert.AreEqual(SuddenDeathRules.CircleZone.Skip, SuddenDeathRules.ClassifyZone(1, false, false, true, centreTier), "a Tier 1 zone is a spawn");
            Assert.AreEqual(SuddenDeathRules.CircleZone.Skip, SuddenDeathRules.ClassifyZone(3, true, false, true, centreTier), "a capital");
            Assert.AreEqual(SuddenDeathRules.CircleZone.Skip, SuddenDeathRules.ClassifyZone(3, false, true, true, centreTier), "out of play");
        }

        // ---- A31 through what the master writes

        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers
        {
            RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, SuddenDeathCountdownSeconds = 4f, SameInstantToleranceMs = Tolerance,
        };

        private static DominionRoomState SuddenDeathRoom() =>
            new DominionRoomState
            {
                HasRound = true, Round = 3, Stage = DominionStage.SuddenDeath, SuddenDeathMs = 100000,
                Points = new[] { 0, 0, 0 }, Wins = new[] { 1, 1, 0 }, Winner = -1,
            };

        private static DominionWrite Write(SuddenDeathRules.Tally tally, int[] playersPerTeam = null) =>
            DominionRoomWrites.Next(true, true, 160000, SuddenDeathRoom(), Cfg, TiedThree, playersPerTeam ?? new[] { 2, 2, 2 }, tally);

        [Test] public void TheMasterReplaysOnlyForTheSameMoment()
        {
            DominionWrite w = Write(TallyOf(Fell(0, 150000), Fell(1, 150020)));
            Assert.AreEqual(DominionRoomWrites.WhatSuddenDeathReplay, w.What);
            Assert.AreEqual(164000, w.Props[DominionKeys.SuddenDeathStart]);
        }

        [Test] public void TheMasterCrownsTheTeamThatFellLater()
        {
            DominionWrite w = Write(TallyOf(Fell(0, 150300), Fell(1, 150000)));
            Assert.AreEqual(DominionRoomWrites.WhatSuddenDeathWon, w.What);
            Assert.AreEqual(0, w.Props[DominionKeys.Winner]);
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
        }

        [Test] public void TheMasterWritesNothingWhileAStampIsMissing()
        {
            var noStamp = new SuddenDeathRules.Player { Team = 1, Counts = true, HasAliveFlag = true, AliveFlag = false };
            Assert.IsNull(Write(TallyOf(Fell(0, 150000), noStamp)));
        }

        // ---- the tally: what the director reads from the room, through the tested rule

        [Test] public void APlayerWithNoFlagYetCountsAsAliveInTheTally()
        {
            SuddenDeathRules.Tally t = TallyOf(new SuddenDeathRules.Player { Team = 0, Counts = true });
            Assert.AreEqual(1, t.Alive[0]);
        }

        [Test] public void APlayerWhoDroppedForGoodCountsForNothing()
        {
            var gone = new SuddenDeathRules.Player { Team = 1, Counts = false, HasAliveFlag = true, AliveFlag = true };
            SuddenDeathRules.Tally t = TallyOf(gone, Fell(0, 5));
            Assert.AreEqual(0, t.Alive[1]);
            Assert.IsFalse(t.HasDeath[1]);
            Assert.IsFalse(t.DeathStampMissing[1]);
        }

        [Test] public void ATeamIdOutsideTheSlotsIsIgnoredAndClearEmptiesTheTally()
        {
            SuddenDeathRules.Tally t = TallyOf(Lives(7), Lives(-1), Lives(2), Fell(1, 9));
            Assert.AreEqual(1, t.Alive[2]);
            Assert.AreEqual(9, t.LastDeathMs[1]);
            t.Clear();
            Assert.AreEqual(0, t.Alive[2]);
            Assert.IsFalse(t.HasDeath[1]);
        }

        // ---- review item 1: the A3 write is not held back while sudden death is judged

        [Test] public void TheLastTeamWithPlayersWinsInSuddenDeathEvenWithEveryoneOfTheTiedPairGone()
        {
            // both tied teams (0 and 1) left the room; team 2 alone remains. The A3 write must be the one the master sends.
            DominionWrite w = Write(TallyOf(), new[] { 0, 0, 2 });
            Assert.AreEqual("last team standing", w.What);
            Assert.AreEqual(2, w.Props[DominionKeys.Winner]);
        }

        [Test] public void AThreeWayWriteForTheLastTeamIsNeverHeldBack()
        {
            DominionWrite w = Write(TallyOf(), new[] { 0, 0, 2 });
            var settle = new SuddenDeathVerdictSettle();
            Assert.IsTrue(settle.ShouldWrite(w, judgingSuddenDeath: true, nowSeconds: 10f, settleSeconds: 0.5f), "sent on the very first look, not after a wait");
        }

        [Test] public void ASuddenDeathVerdictIsStillHeldForTheSettleTime()
        {
            DominionWrite w = Write(TallyOf(Fell(0, 150300), Fell(1, 150000)));
            var settle = new SuddenDeathVerdictSettle();
            Assert.IsFalse(settle.ShouldWrite(w, true, 10f, 0.5f));
            Assert.IsFalse(settle.ShouldWrite(w, true, 10.25f, 0.5f));
            Assert.IsTrue(settle.ShouldWrite(w, true, 10.5f, 0.5f));
        }

        [Test] public void NothingToWriteSendsNothingAndForgetsTheVerdict()
        {
            DominionWrite w = Write(TallyOf(Fell(0, 150300), Fell(1, 150000)));
            var settle = new SuddenDeathVerdictSettle();
            settle.ShouldWrite(w, true, 10f, 0.5f);
            Assert.IsFalse(settle.ShouldWrite(null, true, 10.25f, 0.5f));
            Assert.IsFalse(settle.ShouldWrite(w, true, 10.6f, 0.5f), "seen again from scratch, not continuously since 10");
        }

        [Test] public void AWriteOutsideSuddenDeathIsSentAtOnce()
        {
            var roundEnd = new DominionRoomState { HasRound = true, Round = 1, Stage = DominionStage.Round, EndMs = 105000, Points = new[] { 1, 0, 0 }, Wins = new[] { 0, 0, 0 }, Winner = -1 };
            DominionWrite w = DominionRoomWrites.Next(true, true, 105000, roundEnd, Cfg, TiedThree, new[] { 3, 3, 3 });
            Assert.IsTrue(new SuddenDeathVerdictSettle().ShouldWrite(w, false, 10f, 0.5f));
            Assert.IsFalse(new SuddenDeathVerdictSettle().ShouldWrite(null, false, 10f, 0.5f));
        }

        // ---- review item 2: a late joiner on a team seat in sudden death is dead from the seat write

        [Test] public void ALateJoinerOnATeamSeatInSuddenDeathIsWrittenDeadWithAStamp()
        {
            Hashtable props = DominionRoomWrites.LateJoinerPlayerProps(DominionStage.SuddenDeath, takingTeamSeat: true, stampMs: 123456);
            Assert.IsNotNull(props);
            Assert.AreEqual(false, props[PlayerLifecycle.AliveKey]);
            Assert.AreEqual(123456, props[PlayerLifecycle.LastStandAtKey]);
        }

        [Test] public void ALateJoinerWithoutASyncedClockIsDeadButHasNoStampYet()
        {
            Hashtable props = DominionRoomWrites.LateJoinerPlayerProps(DominionStage.SuddenDeath, true, 0);
            Assert.AreEqual(false, props[PlayerLifecycle.AliveKey]);
            Assert.IsFalse(props.ContainsKey(PlayerLifecycle.LastStandAtKey), "the master waits for the body's own stamp rather than a made-up one");
        }

        [Test] public void OnlyATeamSeatInSuddenDeathMakesTheJoinerDead()
        {
            Assert.IsNull(DominionRoomWrites.LateJoinerPlayerProps(DominionStage.SuddenDeath, takingTeamSeat: false, stampMs: 5), "a spectator seat");
            Assert.IsNull(DominionRoomWrites.LateJoinerPlayerProps(DominionStage.Round, true, 5));
            Assert.IsNull(DominionRoomWrites.LateJoinerPlayerProps(DominionStage.Break, true, 5));
        }

        [Test] public void AJoinerWrittenDeadNeverCountsAsAliveForATiedTeam()
        {
            int earliest = DominionRoomWrites.EarliestFallMs(100000, 4f, Tolerance);
            Hashtable props = DominionRoomWrites.LateJoinerPlayerProps(DominionStage.SuddenDeath, true, DominionRoomWrites.ArrivalDeathStamp(DominionStage.SuddenDeath, false, false, 0, earliest, 150000));
            var joiner = new SuddenDeathRules.Player
            {
                Team = 1, Counts = true, HasAliveFlag = true, AliveFlag = (bool)props[PlayerLifecycle.AliveKey],
                HasDeathStamp = true, DeathStampMs = (int)props[PlayerLifecycle.LastStandAtKey],
            };
            // team 0's last player fell later (150000); team 1 has only the joiner, dead from the seat write with the EARLIEST stamp: it never beats a team that fell later
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 150000), joiner), TiedTwo, Tolerance);
            Assert.AreEqual(SuddenDeathState.Won, r.State);
            Assert.AreEqual(0, r.Team);
            Assert.AreEqual(0, TallyOf(joiner).Alive[1], "not counted alive while the body loads");
        }

        // ---- A29: no healing in sudden death

        [Test] public void NoHealingAtAllInSuddenDeath()
        {
            Assert.AreEqual(0f, DominionHealRules.RateInStage(DominionStage.SuddenDeath, 10f), "the spawn's rate");
            Assert.AreEqual(0f, DominionHealRules.RateInStage(DominionStage.SuddenDeath, 3f), "an owned zone's rate");
        }

        [Test] public void HealingIsUntouchedInEveryOtherStage()
        {
            Assert.AreEqual(10f, DominionHealRules.RateInStage(DominionStage.Round, 10f));
            Assert.AreEqual(4f, DominionHealRules.RateInStage(DominionStage.Break, 4f));
            Assert.AreEqual(3f, DominionHealRules.RateInStage(DominionStage.None, 3f), "a Conquest room has no Dominion stage");
            Assert.AreEqual(0f, DominionHealRules.RateInStage(DominionStage.Round, 0f));
        }

        // ---- A30: the circle ignores Invulnerability

        private sealed class FakeShield : IArmedShield
        {
            public bool Armed;
            public int Asked;
            public bool TryConsume(float damageAmount) { Asked++; bool was = Armed; Armed = false; return was; }
        }

        [Test] public void OnlyTheCircleGoesThroughInvulnerability()
        {
            Assert.IsTrue(HitVerdictRule.IgnoresInvulnerability(DamageSource.SuddenDeath));
            foreach (DamageSource other in new[] { DamageSource.Projectile, DamageSource.Splash, DamageSource.Burn, DamageSource.Zone, DamageSource.Contact })
                Assert.IsFalse(HitVerdictRule.IgnoresInvulnerability(other), other.ToString());
        }

        [Test] public void TheCirclesHitLandsThroughARunningInvulnerability()
        {
            var shield = new FakeShield();
            Assert.AreEqual(HitVerdict.Lands, HitVerdictRule.Classify(false, false, alreadyInvulnerable: true, shield, 5f, ignoresInvulnerability: true));
        }

        [Test] public void TheCirclesHitDoesNotUseUpAnArmedTrap()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.Lands, HitVerdictRule.Classify(false, false, false, shield, 5f, ignoresInvulnerability: true));
            Assert.AreEqual(0, shield.Asked, "the trap was never asked");
            Assert.IsTrue(shield.Armed, "still armed for the next enemy hit");
        }

        [Test] public void AnEnemyHitIsStillStoppedByInvulnerability()
        {
            var running = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.Shielded, HitVerdictRule.Classify(false, false, true, running, 5f, ignoresInvulnerability: false));
            var armed = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.Shielded, HitVerdictRule.Classify(false, false, false, armed, 5f, ignoresInvulnerability: false));
        }

        [Test] public void TheCircleStillCountsAsCombatLikeAnyLandedHit()
        {
            HitVerdict verdict = HitVerdictRule.Classify(false, false, true, null, 5f, ignoresInvulnerability: true);
            Assert.AreEqual(HitVerdict.Lands, verdict, "with the A30 exemption reverted this is Shielded, which also counts as combat");
            Assert.IsTrue(HitVerdictRule.CountsAsCombat(verdict));
        }

        // ---- review item 6: a death after Over never respawns

        [Test] public void NoRespawnOnceTheMatchIsOverButRoundsAndBreaksStillRespawn()
        {
            Assert.IsFalse(DominionRules.RespawnAllowed(true, DominionStage.Over));
            Assert.IsFalse(DominionRules.RespawnAllowed(true, DominionStage.SuddenDeath));
            Assert.IsTrue(DominionRules.RespawnAllowed(true, DominionStage.Round));
            Assert.IsTrue(DominionRules.RespawnAllowed(true, DominionStage.Break));
            Assert.IsTrue(DominionRules.RespawnAllowed(false, DominionStage.Over), "a Conquest room never has a Dominion stage that matters");
        }
    }
}
