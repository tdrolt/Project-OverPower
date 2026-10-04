using ExitGames.Client.Photon;
using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 3: what a points tick adds, the clock that paces it, the centre's lump and the bounty, from plain arrays. Every
    /// number is made up for the test (a table of 99/5/7/99 so a rule that skipped capitals or the centre would show), never the asset's.</summary>
    public class DominionPointsRulesTests
    {
        // Zones: 0..2 the capitals of teams 0..2, 3 a Tier 2 zone, 4 a Tier 3 zone, 5 the centre (Tier 4).
        private static readonly int[] Tier = { 1, 1, 1, 2, 3, 4 };
        private static readonly bool[] Spawn = { true, true, true, false, false, false };
        private static readonly int[] Table = { 99, 5, 7, 99 };
        private static readonly int[] Three = { 0, 1, 2 };
        private const int RoundEnd = 200000;

        private static DominionTickInput Input(int[] owner, int now = 11000, int last = 10000, DominionStage stage = DominionStage.Round,
                                               int[] basePts = null, int[] teams = null, bool centre = false, int centreOwner = -1, int baseCtr = 0, int resetFor = RoundEnd)
        {
            return new DominionTickInput
            {
                NowMs = now, LastTickMs = last,
                Room = new DominionRoomState { HasRound = true, Round = 2, Stage = stage, EndMs = RoundEnd, ResetFor = resetFor, Points = basePts ?? new[] { 0, 0, 0 }, Wins = new int[3], Winner = -1 },
                BasePoints = basePts ?? new[] { 0, 0, 0 },
                BaseCentreMs = baseCtr,
                TeamsInMatch = teams ?? Three,
                ZoneOwner = owner, ZoneTier = Tier, IsSpawnZone = Spawn, PointsPerTier = Table,
                HasCentre = centre, CentreOwner = centreOwner, CentrePoints = 200, CentreFirstMs = 30000, CentreIntervalMs = 30000,
            };
        }

        private static int[] Owners(int z3 = -1, int z4 = -1, int z5 = -1) => new[] { 0, 1, 2, z3, z4, z5 };
        private static int[] Pts(DominionTickPlan p) => (int[])p.Write.Props[DominionKeys.Points];

        // ---- what a tick adds

        [Test] public void ATickAddsEachOwnedZonesTableValueToItsOwner()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0, z4: 0)));
            CollectionAssert.AreEqual(new[] { 12, 0, 0 }, Pts(p)); // 5 + 7
            p = DominionPointsRules.Plan(Input(Owners(z3: 1, z4: 2)));
            CollectionAssert.AreEqual(new[] { 0, 5, 7 }, Pts(p));
        }

        [Test] public void CapitalsAndTheCentreAddNothingWhateverTheTableSays()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z5: 1)));
            Assert.IsNull(p.Write, "own capitals + the centre only: nothing to write");
        }

        [Test] public void ATierOneZoneNobodySpawnsAtAndASpawnThatIsNotTierOneEachAddNothing()
        {
            // Zone 0: Tier 1 but not flagged as a spawn. Zone 1: flagged as a spawn but Tier 2. Zone 2: an ordinary Tier 3 zone (pays 7).
            // Either rule alone must keep the first two out: the tier, and the flag.
            int[] owner = { 0, 0, 0 };
            int[] tier = { 1, 2, 3 };
            bool[] spawn = { false, true, false };
            Assert.AreEqual(7, DominionRules.PointsThisTick(owner, tier, spawn, 0, Table), "only the Tier 3 zone pays");
        }

        [Test] public void NeutralZonesAddNothing() => Assert.IsNull(DominionPointsRules.Plan(Input(Owners())).Write);

        [Test] public void ATeamNotInTheMatchGetsNothing()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 2, z4: 0), teams: new[] { 0, 1 }));
            CollectionAssert.AreEqual(new[] { 7, 0, 0 }, Pts(p));
        }

        [Test] public void ATickBuildsOnThePointsItWasGiven()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 1), basePts: new[] { 10, 20, 30 }));
            CollectionAssert.AreEqual(new[] { 10, 25, 30 }, Pts(p));
        }

        [Test] public void AWriteExpectsTheStageAndRoundItWasComputedFromAndNothingElse()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 1)));
            Assert.AreEqual((int)DominionStage.Round, p.Write.Expected[DominionKeys.Stage]);
            Assert.AreEqual(2, p.Write.Expected[DominionKeys.Round]);
            Assert.AreEqual(RoundEnd, p.Write.Expected[DominionKeys.StageEnd]);
            Assert.AreEqual(4, p.Write.Expected.Count, "stage, round, end time and the points sequence");
            Assert.AreEqual(2, p.Write.Props.Count, "only dPts and the sequence, no dCtr when the centre is not in play");
            Assert.IsFalse(p.Write.Props.ContainsKey(DominionKeys.CentrePayout));
        }

        [Test] public void TheWrittenPointsAreACopyNotTheBaseArray()
        {
            int[] basePts = { 1, 2, 3 };
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), basePts: basePts));
            Assert.AreNotSame(basePts, Pts(p));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, basePts);
        }

        // ---- when points count

        [TestCase(DominionStage.Break)]
        [TestCase(DominionStage.Over)]
        [TestCase(DominionStage.SuddenDeath)]
        [TestCase(DominionStage.None)]
        public void NoPointsAndNoClockOutsideARound(DominionStage stage)
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), stage: stage, centre: true, centreOwner: 0, baseCtr: 1));
            Assert.IsNull(p.Write);
            Assert.AreEqual(0, p.NewLastTickMs);
        }

        [Test] public void NoPointsOnceTheRoundsTimeIsUp()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), now: RoundEnd, last: RoundEnd - 1000));
            Assert.IsNull(p.Write);
        }

        [Test] public void NothingAccruesUntilTheZonesAreResetForThisRound()
        {
            DominionTickInput input = Input(Owners(z3: 0), resetFor: 12345, centre: true, centreOwner: 0, baseCtr: 1);
            input.PendingBounty = new[] { 0, 150, 0 };
            DominionTickPlan p = DominionPointsRules.Plan(input);
            Assert.IsNull(p.Write);
            Assert.AreEqual(0, p.NewLastTickMs, "the clock has not started either");
            Assert.IsNull(DominionPointsRules.Plan(Input(Owners(z3: 0), resetFor: 0)).Write, "never reset");
        }

        // ---- the zone reset (a new master finishes it)

        private static DominionRoomState RoomAt(DominionStage stage, int end, int resetFor) =>
            new DominionRoomState { HasRound = true, Round = 2, Stage = stage, EndMs = end, ResetFor = resetFor, Points = new int[3], Wins = new int[3], Winner = -1 };

        [Test] public void AZoneResetIsDueInARoundOrABreakWhoseEndIsNotTheOneRecorded()
        {
            Assert.IsTrue(DominionRoomWrites.ZoneResetDue(RoomAt(DominionStage.Round, 5000, 0)));
            Assert.IsTrue(DominionRoomWrites.ZoneResetDue(RoomAt(DominionStage.Break, 9000, 5000)));
            Assert.IsFalse(DominionRoomWrites.ZoneResetDue(RoomAt(DominionStage.Break, 9000, 9000)));
            Assert.IsFalse(DominionRoomWrites.ZoneResetDue(RoomAt(DominionStage.Round, 5000, 5000)));
        }

        [Test] public void NoZoneResetBeforeRoundOneOrInSuddenDeathOrAfterTheEnd()
        {
            Assert.IsFalse(DominionRoomWrites.ZoneResetDue(default));
            Assert.IsFalse(DominionRoomWrites.ZoneResetDue(RoomAt(DominionStage.SuddenDeath, 0, 7)));
            Assert.IsFalse(DominionRoomWrites.ZoneResetDue(RoomAt(DominionStage.Over, 5000, 0)));
        }

        [Test] public void TheResetDoneWriteRecordsTheEndAndExpectsTheStageItSaw()
        {
            DominionRoomState room = RoomAt(DominionStage.Break, 9000, 5000);
            DominionWrite w = DominionRoomWrites.ZonesResetDone(room);
            Assert.AreEqual(9000, w.Props[DominionKeys.ZonesResetFor]);
            Assert.AreEqual(1, w.Props.Count);
            Assert.AreEqual((int)DominionStage.Break, w.Expected[DominionKeys.Stage]);
            Assert.AreEqual(2, w.Expected[DominionKeys.Round]);
            Assert.AreEqual(9000, w.Expected[DominionKeys.StageEnd]);
        }

        [Test] public void TheRoomReadsDrz()
        {
            var props = new Hashtable { { DominionKeys.ZonesResetFor, 777 } };
            Assert.AreEqual(777, DominionRoomState.Read(props).ResetFor);
        }

        // ---- the clock

        [Test] public void TheClockStartsWithoutPayingWhenItHasNotRun()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), now: 50000, last: 0));
            Assert.IsNull(p.Write);
            Assert.AreEqual(50000, p.NewLastTickMs);
        }

        [Test] public void NothingIsPaidBeforeASecondHasPassed()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), now: 10999, last: 10000));
            Assert.IsNull(p.Write);
            Assert.AreEqual(10000, p.NewLastTickMs);
        }

        [Test] public void ASecondPassedPaysOneTickAndMovesTheClockOneSecond()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), now: 11000, last: 10000));
            CollectionAssert.AreEqual(new[] { 5, 0, 0 }, Pts(p));
            Assert.AreEqual(11000, p.NewLastTickMs);
        }

        [Test] public void ALateFrameWithinTwoSecondsPaysOneTickAndKeepsTheBeat()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), now: 11900, last: 10000));
            CollectionAssert.AreEqual(new[] { 5, 0, 0 }, Pts(p));
            Assert.AreEqual(11000, p.NewLastTickMs, "the next look at 12000 pays the catch-up second");
        }

        [Test] public void AHitchPaysOneTickNotABurst()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), now: 40000, last: 10000));
            CollectionAssert.AreEqual(new[] { 5, 0, 0 }, Pts(p));
            Assert.AreEqual(40000, p.NewLastTickMs);
        }

        [Test] public void TheClockIsWrapSafe()
        {
            int last = int.MaxValue - 500;
            int now = unchecked(last + 1100);
            Assert.IsTrue(now < 0);
            Assert.IsTrue(DominionPointsRules.TickDue(now, last, out int newLast));
            Assert.AreEqual(unchecked(last + 1000), newLast);
            Assert.IsFalse(DominionPointsRules.TickDue(unchecked(last + 999), last, out _));
        }

        // ---- the centre

        [Test] public void TheCentrePaysTheHolderAtThePayoutTimeAndMovesTheClockForward()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z5: 1), now: 30000, last: 29500, centre: true, centreOwner: 1, baseCtr: 30000));
            Assert.IsTrue(p.CentrePaid);
            Assert.AreEqual(1, p.CentreTeam);
            Assert.AreEqual(200, p.CentrePoints);
            CollectionAssert.AreEqual(new[] { 0, 200, 0 }, Pts(p));
            Assert.AreEqual(60000, p.Write.Props[DominionKeys.CentrePayout]);
        }

        [Test] public void TheCentreDoesNotPayBeforeItsTime()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z5: 1), now: 29999, last: 29500, centre: true, centreOwner: 1, baseCtr: 30000));
            Assert.IsNull(p.Write);
            Assert.IsFalse(p.CentrePaid);
        }

        [Test] public void NobodyHoldingTheCentreMeansNobodyIsPaidButTheClockMovesOn()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(), now: 30000, last: 29500, centre: true, centreOwner: -1, baseCtr: 30000));
            Assert.IsTrue(p.CentrePaid);
            Assert.AreEqual(-1, p.CentreTeam);
            Assert.IsFalse(p.Write.Props.ContainsKey(DominionKeys.Points));
            Assert.AreEqual(60000, p.Write.Props[DominionKeys.CentrePayout]);
        }

        [Test] public void ALateCentrePaysOnceAndLandsOnTheNextGridTimeAfterNow()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z5: 2), now: 95000, last: 94500, centre: true, centreOwner: 2, baseCtr: 30000));
            CollectionAssert.AreEqual(new[] { 0, 0, 200 }, Pts(p));
            Assert.AreEqual(120000, p.Write.Props[DominionKeys.CentrePayout]);
        }

        [Test] public void ATickAndAPayoutShareOneWrite()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 1, z5: 1), now: 30000, last: 29000, centre: true, centreOwner: 1, baseCtr: 30000));
            CollectionAssert.AreEqual(new[] { 0, 205, 0 }, Pts(p));
            Assert.AreEqual(3, p.Write.Props.Count, "dPts, dCtr and the sequence");
        }

        [Test] public void AMapWithoutACentreNeverPaysOrWritesDctr()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 0), now: 30000, last: 29000, centre: false, centreOwner: 0, baseCtr: 30000));
            Assert.IsFalse(p.CentrePaid);
            Assert.IsFalse(p.Write.Props.ContainsKey(DominionKeys.CentrePayout));
        }

        [Test] public void AMissingDctrIsScheduledFromNowByTheFirstDelay()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(), now: 50000, last: 49500, centre: true, baseCtr: 0));
            Assert.IsFalse(p.CentrePaid);
            Assert.AreEqual(80000, p.Write.Props[DominionKeys.CentrePayout]);
        }

        [Test] public void TheFirstPayoutIsTheFirstDelayAfterRoundStartThenEveryInterval()
        {
            Assert.AreEqual(35000, DominionRules.NextCentrePayoutMs(5000, 5000, 30000, 30000));
            Assert.AreEqual(65000, DominionRules.NextCentrePayoutMs(35000, 35000, 0, 30000), "the next one is an interval after the one that just paid");
        }

        // ---- the buzzer: a payout due by the round's end still counts (Tudor, A18)

        [Test] public void APayoutDueExactlyAtTheRoundsEndIsPaidToTheHolderAndCountsOnce()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z5: 1), now: RoundEnd, last: RoundEnd - 1000, centre: true, centreOwner: 1, baseCtr: RoundEnd, basePts: new[] { 50, 60, 70 }));
            Assert.IsTrue(p.CentrePaid);
            Assert.AreEqual(1, p.CentreTeam);
            CollectionAssert.AreEqual(new[] { 50, 260, 70 }, Pts(p), "the centre's lump only: no zone tick once the time is up");
            Assert.AreEqual(RoundEnd + 30000, p.Write.Props[DominionKeys.CentrePayout], "the clock moves on, so a second look pays nothing");

            DominionTickInput again = Input(Owners(z5: 1), now: RoundEnd + 100, last: RoundEnd - 1000, centre: true, centreOwner: 1, baseCtr: (int)p.Write.Props[DominionKeys.CentrePayout]);
            Assert.IsNull(DominionPointsRules.Plan(again).Write, "paid once");
        }

        [Test] public void ALateLookAfterTheBuzzerStillPaysAPayoutThatWasDueBeforeIt()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z5: 2), now: RoundEnd + 40, last: RoundEnd - 1000, centre: true, centreOwner: 2, baseCtr: RoundEnd - 500));
            CollectionAssert.AreEqual(new[] { 0, 0, 200 }, Pts(p));
        }

        [Test] public void APayoutDueAfterTheRoundsEndIsNotPaid()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z5: 1), now: RoundEnd + 2000, last: RoundEnd - 1000, centre: true, centreOwner: 1, baseCtr: RoundEnd + 1000));
            Assert.IsNull(p.Write);
            Assert.IsFalse(p.CentrePaid);
        }

        [Test] public void NothingElseIsWrittenAtTheBuzzer()
        {
            DominionTickInput input = Input(Owners(z3: 0), now: RoundEnd, last: RoundEnd - 1000, centre: true, centreOwner: -1, baseCtr: RoundEnd + 30000);
            Assert.IsNull(DominionPointsRules.Plan(input).Write, "no zone tick at the buzzer, and the next payout is not due");
            Assert.IsNull(DominionPointsRules.Plan(Input(Owners(), now: RoundEnd, last: RoundEnd - 1000, centre: true, baseCtr: 0)).Write, "no scheduling of a missing dCtr either");
        }

        [Test] public void TheBuzzerPayoutIsInThePointsTheRoundIsScoredOn()
        {
            // Team 0 leads 100 to 90; the buzzer payout of 200 goes to team 1, so team 1 wins the round once the stage writer sees it.
            var cfg = new DominionFlowNumbers { RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 180f, BreakSeconds = 20f };
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z5: 1), now: RoundEnd, last: RoundEnd - 1000, centre: true, centreOwner: 1, baseCtr: RoundEnd, basePts: new[] { 100, 90, 0 }));
            var room = new DominionRoomState { HasRound = true, Round = 1, Stage = DominionStage.Round, EndMs = RoundEnd, ResetFor = RoundEnd, Points = Pts(p), Wins = new int[3], Winner = -1 };
            DominionWrite end = DominionRoomWrites.Next(true, true, RoundEnd, room, cfg, Three, new[] { 3, 3, 3 });
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, (int[])end.Props[DominionKeys.Wins]);
        }

        // ---- two masters must not both add to the same points (dPseq)

        [Test] public void EveryPointsWriteCountsUpTheSequenceAndExpectsTheOneItBuiltOn()
        {
            DominionTickInput input = Input(Owners(z3: 1));
            input.BasePointsSeq = 4;
            DominionTickPlan p = DominionPointsRules.Plan(input);
            Assert.AreEqual(5, p.Write.Props[DominionKeys.PointsSeq]);
            Assert.AreEqual(4, p.Write.Expected[DominionKeys.PointsSeq]);
            Assert.AreEqual(4, p.Write.Expected.Count, "the stage, round, end time and the sequence");
        }

        [Test] public void TheFirstPointsWriteOfAMatchExpectsNoSequenceYet()
        {
            DominionTickPlan p = DominionPointsRules.Plan(Input(Owners(z3: 1)));
            Assert.AreEqual(1, p.Write.Props[DominionKeys.PointsSeq]);
            Assert.IsTrue(p.Write.Expected.ContainsKey(DominionKeys.PointsSeq));
            Assert.IsNull(p.Write.Expected[DominionKeys.PointsSeq], "null in a check-and-set means the key must still be absent");
        }

        [Test] public void ACentreOnlyWriteCountsTheSequenceToo()
        {
            DominionTickInput input = Input(Owners(), now: 30000, last: 29500, centre: true, centreOwner: -1, baseCtr: 30000);
            input.BasePointsSeq = 9;
            Assert.AreEqual(10, DominionPointsRules.Plan(input).Write.Props[DominionKeys.PointsSeq]);
        }

        [Test] public void TheRoomReadsDpseq()
        {
            Assert.AreEqual(12, DominionRoomState.Read(new Hashtable { { DominionKeys.PointsSeq, 12 } }).PointsSeq);
            Assert.AreEqual(0, DominionRoomState.Read(new Hashtable()).PointsSeq);
        }

        [Test] public void TheBreakWriteClearsTheCentresStalePayoutTime()
        {
            var cfg = new DominionFlowNumbers { RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, HasCentre = true, CentreFirstMs = 30000, CentreIntervalMs = 30000 };
            var room = new DominionRoomState { HasRound = true, Round = 1, Stage = DominionStage.Round, EndMs = 9000, Points = new[] { 5, 1, 0 }, Wins = new int[3], Winner = -1, CentreMs = 9000 + 30000 };
            DominionWrite w = DominionRoomWrites.Next(true, true, 9000, room, cfg, Three, new[] { 3, 3, 3 });
            Assert.AreEqual(DominionStage.Break, (DominionStage)(int)w.Props[DominionKeys.Stage]);
            Assert.IsTrue(w.Props.ContainsKey(DominionKeys.CentrePayout), "the key is in the write...");
            Assert.IsNull(w.Props[DominionKeys.CentrePayout], "...as null, which removes it from the room");
        }

        // ---- bounty

        [Test] public void PendingBountyJoinsTheNextWriteEvenWithNoTickDue()
        {
            DominionTickInput input = Input(Owners(), now: 10300, last: 10000, basePts: new[] { 5, 5, 5 });
            input.PendingBounty = new[] { 0, 150, 0 };
            DominionTickPlan p = DominionPointsRules.Plan(input);
            CollectionAssert.AreEqual(new[] { 5, 155, 5 }, Pts(p));
            Assert.AreEqual(10000, p.NewLastTickMs);
        }

        [Test] public void PendingBountyIsDroppedOutsideARound()
        {
            DominionTickInput input = Input(Owners(), stage: DominionStage.Break);
            input.PendingBounty = new[] { 0, 150, 0 };
            Assert.IsNull(DominionPointsRules.Plan(input).Write);
        }

        [Test] public void ABountyIsDueOnlyAfterAnUnbrokenHoldFromARealOwner()
        {
            Assert.AreEqual(150, DominionPointsRules.BountyPoints(61000, 60000, 0, 1, 150));
            Assert.AreEqual(150, DominionPointsRules.BountyPoints(60000, 60000, 0, 1, 150));
            Assert.AreEqual(0, DominionPointsRules.BountyPoints(59999, 60000, 0, 1, 150));
            Assert.AreEqual(0, DominionPointsRules.BountyPoints(61000, 60000, 1, 1, 150), "retaking your own zone pays nothing");
            Assert.AreEqual(0, DominionPointsRules.BountyPoints(61000, 60000, -1, 1, 150), "nobody held it");
            Assert.AreEqual(0, DominionPointsRules.BountyPoints(61000, 60000, 0, -1, 150));
            Assert.AreEqual(0, DominionPointsRules.BountyPoints(61000, 60000, 0, 1, 0));
        }

        [Test] public void TheBountyReadsTheHoldTheRoomKeepsWhenAZoneGoesNeutral()
        {
            TerritorySnapshot snap = new TerritorySnapshot(6).WithCapture(3, 0, 1000, 0).WithNeutral(3, 71000);
            Assert.AreEqual(0, snap.LastOwnerOf(3));
            Assert.AreEqual(70000, snap.LastHeldMs(3));
            Assert.AreEqual(150, DominionPointsRules.BountyPoints(snap.LastHeldMs(3), 60000, snap.LastOwnerOf(3), 1, 150));

            TerritorySnapshot after = snap.WithCapture(3, 1, 72000, 0);
            Assert.AreEqual(-1, after.LastOwnerOf(3), "the capture itself clears the hold: it must be read BEFORE the capture");
            Assert.AreEqual(0, DominionPointsRules.BountyPoints(after.LastHeldMs(3), 60000, after.LastOwnerOf(3), 1, 150));

            TerritorySnapshot wiped = new TerritorySnapshot(6).WithCapture(3, 0, 1000, 0).WithNeutralReset(3, 71000);
            Assert.AreEqual(0, DominionPointsRules.BountyPoints(wiped.LastHeldMs(3), 60000, wiped.LastOwnerOf(3), 1, 150), "a reset takes from nobody");

            TerritorySnapshot brief = new TerritorySnapshot(6).WithCapture(3, 0, 1000, 0).WithNeutral(3, 20000);
            Assert.AreEqual(0, DominionPointsRules.BountyPoints(brief.LastHeldMs(3), 60000, brief.LastOwnerOf(3), 1, 150));
        }

        // ---- the ledger (build on the last write while its echo is pending)

        [Test] public void WithNothingPendingTheBasisIsTheRoom()
        {
            var ledger = new DominionPointsLedger();
            ledger.Basis(1f, 2f, new[] { 1, 2, 3 }, 777, 3, out int[] pts, out int ctr, out int seq);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, pts);
            Assert.AreEqual(777, ctr);
            Assert.AreEqual(3, seq, "no write pending: the room's own sequence");
        }

        [Test] public void WhileAnEchoIsPendingTheBasisIsTheLastWrite()
        {
            var ledger = new DominionPointsLedger();
            ledger.Sent(new[] { 9, 9, 9 }, 5000, 7, 1f);
            ledger.Basis(1.5f, 2f, new[] { 1, 2, 3 }, 777, 3, out int[] pts, out int ctr, out int seq);
            CollectionAssert.AreEqual(new[] { 9, 9, 9 }, pts);
            Assert.AreEqual(5000, ctr);
            Assert.AreEqual(7, seq, "the sequence of the last write, not the room's older one");
        }

        [Test] public void TwoWritesNeedTwoEchoesBeforeTheRoomIsTrusted()
        {
            var ledger = new DominionPointsLedger();
            ledger.Sent(new[] { 1, 0, 0 }, 0, 7, 1f);
            ledger.Sent(new[] { 2, 0, 0 }, 0, 7, 1.1f);
            ledger.Echoed();
            ledger.Basis(1.2f, 2f, new[] { 1, 0, 0 }, 0, 3, out int[] pts, out _, out _);
            CollectionAssert.AreEqual(new[] { 2, 0, 0 }, pts, "the second write is still on its way");
            ledger.Echoed();
            Assert.IsFalse(ledger.Pending(1.3f, 2f));
            ledger.Basis(1.3f, 2f, new[] { 2, 0, 0 }, 0, 3, out pts, out _, out _);
            CollectionAssert.AreEqual(new[] { 2, 0, 0 }, pts);
        }

        [Test] public void AWriteThatNeverEchoesStopsBeingTrustedAfterTheTimeout()
        {
            var ledger = new DominionPointsLedger();
            ledger.Sent(new[] { 9, 9, 9 }, 5000, 7, 1f);
            ledger.Basis(3.5f, 2f, new[] { 1, 2, 3 }, 777, 3, out int[] pts, out int ctr, out int seq);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, pts);
            Assert.AreEqual(777, ctr);
            Assert.AreEqual(3, seq);
        }

        [Test] public void ResetForgetsEverything()
        {
            var ledger = new DominionPointsLedger();
            ledger.Sent(new[] { 9, 9, 9 }, 5000, 7, 1f);
            ledger.Reset();
            Assert.IsFalse(ledger.Pending(1f, 2f));
            ledger.Basis(1f, 2f, new[] { 1, 2, 3 }, 777, 3, out int[] pts, out _, out _);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, pts);
        }

        [Test] public void ALedgerKeepsItsOwnCopyOfTheWrittenPoints()
        {
            var ledger = new DominionPointsLedger();
            int[] sent = { 4, 4, 4 };
            ledger.Sent(sent, 0, 7, 1f);
            sent[0] = 99;
            ledger.Basis(1f, 2f, new int[3], 0, 3, out int[] pts, out _, out _);
            Assert.AreEqual(4, pts[0]);
        }

        // ---- a refused points write (Task 5 review fix 3)

        [Test] public void ARoomSequencePastOurWriteMeansItWasRefusedAndTheRoomIsTrustedAtOnce()
        {
            var ledger = new DominionPointsLedger();
            ledger.Sent(new[] { 9, 9, 9 }, 0, 7, 1f);
            ledger.Basis(1.1f, 2f, new[] { 4, 0, 0 }, 0, 8, out int[] pts, out _, out int seq);
            CollectionAssert.AreEqual(new[] { 4, 0, 0 }, pts, "not the refused copy, and not after the 2 s timeout either");
            Assert.AreEqual(8, seq);
            Assert.IsFalse(ledger.Pending(1.1f, 2f));
        }

        [Test] public void TheSameSequenceWithOtherPointsMeansAnotherMasterWroteItFirst()
        {
            var ledger = new DominionPointsLedger();
            ledger.Sent(new[] { 9, 9, 9 }, 0, 7, 1f);
            Assert.IsTrue(ledger.RefusedByRoom(new[] { 5, 0, 0 }, 0, 7));
            Assert.IsTrue(ledger.RefusedByRoom(new[] { 9, 9, 9 }, 123, 7), "a different centre time is a different write too");
            ledger.Basis(1.1f, 2f, new[] { 5, 0, 0 }, 0, 7, out int[] pts, out _, out _);
            CollectionAssert.AreEqual(new[] { 5, 0, 0 }, pts);
        }

        [Test] public void OurOwnEchoOrAnOlderRoomIsNotARefusal()
        {
            var ledger = new DominionPointsLedger();
            ledger.Sent(new[] { 9, 9, 9 }, 5000, 7, 1f);
            Assert.IsFalse(ledger.RefusedByRoom(new[] { 9, 9, 9 }, 5000, 7), "the room holds exactly what we sent");
            Assert.IsFalse(ledger.RefusedByRoom(new[] { 1, 1, 1 }, 0, 6), "the room has not reached our write yet");
            ledger.Basis(1.1f, 2f, new[] { 1, 1, 1 }, 0, 6, out int[] pts, out _, out int seq);
            CollectionAssert.AreEqual(new[] { 9, 9, 9 }, pts);
            Assert.AreEqual(7, seq);
        }

        [Test] public void NothingPendingMeansNothingToRefuseAndTheSequenceCompareSurvivesTheWrap()
        {
            var ledger = new DominionPointsLedger();
            Assert.IsFalse(ledger.RefusedByRoom(new[] { 1, 1, 1 }, 0, 99));
            ledger.Sent(new[] { 2, 2, 2 }, 0, int.MaxValue, 1f);
            Assert.IsTrue(ledger.RefusedByRoom(new[] { 3, 3, 3 }, 0, int.MinValue), "one past int.MaxValue wraps to int.MinValue");
            Assert.IsFalse(ledger.RefusedByRoom(new[] { 3, 3, 3 }, 0, int.MaxValue - 1));
        }

        // ---- the markers

        [Test] public void TheMarkerNamesTheZoneTeamAndPoints()
        {
            Assert.AreEqual("dominion bounty zone 3 team 1 +150", DominionMarkerNotes.Bounty(3, 1, 150));
            Assert.AreEqual("dominion centre payout team 2 +200", DominionMarkerNotes.CentrePayout(2, 200));
            Assert.AreEqual("dominion centre payout nobody", DominionMarkerNotes.CentrePayout(-1, 200));
        }

        // ---- the round start carries the centre's first payout

        [Test] public void ARoundStartWriteCarriesTheCentresFirstPayoutWhenThereIsACentre()
        {
            var cfg = new DominionFlowNumbers { RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, HasCentre = true, CentreFirstMs = 30000, CentreIntervalMs = 30000 };
            DominionWrite first = DominionRoomWrites.Next(true, true, 5000, default, cfg, Three, new[] { 3, 3, 3 });
            Assert.IsFalse(first.Props.ContainsKey(DominionKeys.CentrePayout), "the break before round 1 pays nothing; the round start writes the first payout");
            DominionWrite round1 = DominionRoomWrites.Next(true, true, 15000, DominionRoomState.Read(first.Props), cfg, Three, new[] { 3, 3, 3 });
            Assert.AreEqual(45000, round1.Props[DominionKeys.CentrePayout]);

            var breakRoom = new DominionRoomState { HasRound = true, Round = 2, Stage = DominionStage.Break, EndMs = 9000, Points = new[] { 1, 2, 3 }, Wins = new[] { 1, 0, 0 }, Winner = -1 };
            DominionWrite second = DominionRoomWrites.Next(true, true, 9000, breakRoom, cfg, Three, new[] { 3, 3, 3 });
            Assert.AreEqual(39000, second.Props[DominionKeys.CentrePayout]);
        }

        [Test] public void ARoundStartWriteHasNoDctrWithoutACentre()
        {
            var cfg = new DominionFlowNumbers { RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f };
            DominionWrite first = DominionRoomWrites.Next(true, true, 5000, default, cfg, Three, new[] { 3, 3, 3 });
            Assert.IsFalse(first.Props.ContainsKey(DominionKeys.CentrePayout));
        }

        [Test] public void TheRoomReadsDctr()
        {
            var props = new Hashtable { { DominionKeys.CentrePayout, 4242 } };
            Assert.AreEqual(4242, DominionRoomState.Read(props).CentreMs);
            Assert.AreEqual(0, DominionRoomState.Read(new Hashtable()).CentreMs);
        }
    }
}
