using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 1: who wins a round and the match, when sudden death starts, what zones pay, when the centre pays and a bounty is due.
    /// Every number here is a literal made up for the test, never one of Tudor's tuning values.</summary>
    public class DominionRulesTests
    {
        // ---- round winner

        [Test] public void TheSingleHighestScoreWinsTheRound() => Assert.AreEqual(1, DominionRules.RoundWinner(new[] { 100, 250, 40 }));
        [Test] public void EqualTopPointsMeanNobodyWinsTheRound() => Assert.AreEqual(-1, DominionRules.RoundWinner(new[] { 300, 300, 40 }));
        [Test] public void ZeroZeroIsATiedRoundWithNoWinner() => Assert.AreEqual(-1, DominionRules.RoundWinner(new[] { 0, 0 }));
        [Test] public void ATieBelowTheTopDoesNotMatter() => Assert.AreEqual(2, DominionRules.RoundWinner(new[] { 5, 5, 9 }));
        [Test] public void NoPointsAtAllMeansNoWinner()
        {
            Assert.AreEqual(-1, DominionRules.RoundWinner(null));
            Assert.AreEqual(-1, DominionRules.RoundWinner(new int[0]));
        }

        // ---- match winner

        [Test] public void TheFirstTeamToTheTargetWinsTheMatch() => Assert.AreEqual(1, DominionRules.MatchWinner(new[] { 1, 2 }, 2));
        [Test] public void NobodyBelowTheTargetWinsTheMatch() => Assert.AreEqual(-1, DominionRules.MatchWinner(new[] { 1, 1, 0 }, 2));
        [Test] public void NullWinsMeansNoMatchWinner() => Assert.AreEqual(-1, DominionRules.MatchWinner(null, 2));

        // ---- sudden death teams

        [Test] public void TwoTeamsLevelAreBothInSuddenDeath() =>
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.SuddenDeathTeams(new[] { 1, 1 }, new[] { 0, 1 }));

        [Test] public void ThreeTeamsLevelAreAllInSuddenDeath() =>
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, DominionRules.SuddenDeathTeams(new[] { 1, 1, 1 }, new[] { 0, 1, 2 }));

        [Test] public void OnlyTheTwoTiedForTheMostAreInSuddenDeath() =>
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionRules.SuddenDeathTeams(new[] { 1, 1, 0 }, new[] { 0, 1, 2 }));

        [Test] public void OnlyTeamsInTheMatchCount() =>
            CollectionAssert.AreEqual(new[] { 0, 2 }, DominionRules.SuddenDeathTeams(new[] { 1, 5, 1 }, new[] { 0, 2 }));

        // ---- after a round

        [Test] public void TwoRoundWinsAfterRoundTwoEndTheMatchWithoutRoundThree()
        {
            RoundOutcome o = DominionRules.AfterRound(2, new[] { 2, 0 }, 2, 3, new[] { 0, 1 });
            Assert.AreEqual(DominionStage.Over, o.Next);
            Assert.AreEqual(0, o.Winner);
        }

        [Test] public void MoreRoundsLeftAndNoWinnerMeansABreak()
        {
            RoundOutcome o = DominionRules.AfterRound(1, new[] { 1, 0 }, 2, 3, new[] { 0, 1 });
            Assert.AreEqual(DominionStage.Break, o.Next);
            Assert.AreEqual(-1, o.Winner);
        }

        [Test] public void LevelAfterTheLastRoundIsSuddenDeathBetweenTheLevelTeams()
        {
            RoundOutcome o = DominionRules.AfterRound(3, new[] { 1, 1 }, 2, 3, new[] { 0, 1 });
            Assert.AreEqual(DominionStage.SuddenDeath, o.Next);
            CollectionAssert.AreEqual(new[] { 0, 1 }, o.SuddenDeathTeams);
        }

        [Test] public void ThreeLevelTeamsAfterTheLastRoundAllGoToSuddenDeath()
        {
            RoundOutcome o = DominionRules.AfterRound(3, new[] { 1, 1, 1 }, 2, 3, new[] { 0, 1, 2 });
            Assert.AreEqual(DominionStage.SuddenDeath, o.Next);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, o.SuddenDeathTeams);
        }

        [Test] public void TwoLevelAtTheTopAfterTheLastRoundLeaveTheThirdOutOfSuddenDeath()
        {
            RoundOutcome o = DominionRules.AfterRound(3, new[] { 1, 1, 0 }, 2, 3, new[] { 0, 1, 2 });
            Assert.AreEqual(DominionStage.SuddenDeath, o.Next);
            CollectionAssert.AreEqual(new[] { 0, 1 }, o.SuddenDeathTeams);
        }

        [Test] public void ASingleLeaderOnRoundWinsAfterTheLastRoundWithNoSuddenDeath()
        {
            // Tudor A7: 1-0-0 after three rounds (two of them tied) is not level, so the leader wins at once.
            RoundOutcome o = DominionRules.AfterRound(3, new[] { 0, 1, 0 }, 2, 3, new[] { 0, 1, 2 });
            Assert.AreEqual(DominionStage.Over, o.Next);
            Assert.AreEqual(1, o.Winner);
        }

        [Test] public void NobodyWinningAnyRoundIsSuddenDeathBetweenEveryone()
        {
            RoundOutcome o = DominionRules.AfterRound(3, new[] { 0, 0 }, 2, 3, new[] { 0, 1 });
            Assert.AreEqual(DominionStage.SuddenDeath, o.Next);
            CollectionAssert.AreEqual(new[] { 0, 1 }, o.SuddenDeathTeams);
        }

        // ---- points per second

        private static readonly int[] Table = { 0, 7, 3, 9 }; // literal test table, tiers 1..4

        [Test] public void TwoOwnedZonesAddBoth()
        {
            int[] owner = { 0, 0, 1 };
            int[] tier = { 2, 3, 2 };
            bool[] spawn = { false, false, false };
            Assert.AreEqual(10, DominionRules.PointsThisTick(owner, tier, spawn, 0, Table));
            Assert.AreEqual(7, DominionRules.PointsThisTick(owner, tier, spawn, 1, Table));
        }

        [Test] public void SpawnZonesAddNothingEvenWithANonZeroTableEntry()
        {
            int[] owner = { 0, 0 };
            int[] tier = { 2, 2 };
            bool[] spawn = { true, false };
            Assert.AreEqual(7, DominionRules.PointsThisTick(owner, tier, spawn, 0, Table));
        }

        [Test] public void Tier1AddsNothingPerTickEvenWithANonZeroTableEntry() =>
            Assert.AreEqual(0, DominionRules.PointsThisTick(new[] { 0 }, new[] { 1 }, new[] { false }, 0, new[] { 5, 7, 3, 9 }));

        [Test] public void TheCentreAddsNothingPerTickEvenWithANonZeroTableEntry() =>
            Assert.AreEqual(0, DominionRules.PointsThisTick(new[] { 0 }, new[] { 4 }, new[] { false }, 0, Table));

        [Test] public void ANeutralZoneAddsNothing() =>
            Assert.AreEqual(0, DominionRules.PointsThisTick(new[] { -1 }, new[] { 2 }, new[] { false }, 0, Table));

        [Test] public void AZoneHeldByAnotherTeamAddsNothing() =>
            Assert.AreEqual(0, DominionRules.PointsThisTick(new[] { 1 }, new[] { 2 }, new[] { false }, 0, Table));

        [Test] public void AllTeamsAreScoredTogether()
        {
            int[] all = DominionRules.PointsThisTickAll(new[] { 0, 1, 1 }, new[] { 2, 3, 2 }, new[] { false, false, false }, 3, Table);
            CollectionAssert.AreEqual(new[] { 7, 10, 0 }, all);
        }

        // ---- the centre

        [Test] public void TheFirstCentrePayoutIsRoundStartPlusTheFirstDelay() =>
            Assert.AreEqual(1000 + 30000, DominionRules.NextCentrePayoutMs(1000, 5000, 30000, 20000));

        [Test] public void TheNextPayoutAfterNowIsStrictlyLater()
        {
            Assert.AreEqual(1000 + 30000 + 20000, DominionRules.NextCentrePayoutMs(1000, 31000, 30000, 20000), "exactly on a payout time: the next one");
            Assert.AreEqual(1000 + 30000 + 40000, DominionRules.NextCentrePayoutMs(1000, 51001, 30000, 20000));
        }

        [Test] public void APayoutIsDueFromItsTimeOn()
        {
            Assert.IsFalse(DominionRules.CentrePayoutDue(31000, 30999));
            Assert.IsTrue(DominionRules.CentrePayoutDue(31000, 31000));
            Assert.IsTrue(DominionRules.CentrePayoutDue(31000, 40000));
        }

        [Test] public void NobodyHoldingTheCentreMeansNobodyIsPaid()
        {
            Assert.AreEqual(-1, DominionRules.CentrePayoutTeam(-1));
            Assert.AreEqual(2, DominionRules.CentrePayoutTeam(2));
        }

        // ---- bounty

        [Test] public void ABountyIsDueAfterAnUnbrokenHoldOfAtLeastTheHoldTime()
        {
            Assert.IsTrue(DominionRules.BountyDue(60000, 60000, lastOwner: 0, newOwner: 1));
            Assert.IsTrue(DominionRules.BountyDue(90000, 60000, 0, 1));
        }

        [Test] public void ATooShortHoldPaysNoBounty() => Assert.IsFalse(DominionRules.BountyDue(59999, 60000, 0, 1));
        [Test] public void RetakingYourOwnZonePaysNoBounty() => Assert.IsFalse(DominionRules.BountyDue(90000, 60000, 0, 0));
        [Test] public void ANeverHeldZonePaysNoBounty() => Assert.IsFalse(DominionRules.BountyDue(90000, 60000, -1, 1));
        [Test] public void AZoneTakenByNobodyPaysNoBounty() => Assert.IsFalse(DominionRules.BountyDue(90000, 60000, 0, -1));

        // The room's data: a zone always goes neutral before it is captured, and WithNeutral stores the finished hold.
        [Test] public void ABountyReadsTheHoldTheNeutralStepSettled()
        {
            var held = new TerritorySnapshot(4).WithCapture(2, 0, 1000, 0);
            var gone = held.WithNeutral(2, 61000);                   // team 0 held it 60 s, then it drained to neutral
            var due = DominionRules.BountyDue(gone.LastHeldMs(2), 60000, gone.LastOwnerOf(2), 1); // team 1 takes it 10 s later
            Assert.IsTrue(due);

            var shortHold = new TerritorySnapshot(4).WithCapture(2, 0, 1000, 0).WithNeutral(2, 6000); // 5 s hold
            Assert.IsFalse(DominionRules.BountyDue(shortHold.LastHeldMs(2), 60000, shortHold.LastOwnerOf(2), 1), "a 5 s hold, even after a 70 s neutral gap");
        }

        // ---- the int clock wraps

        [Test] public void ATimeComparisonStillWorksAcrossTheIntWrap()
        {
            int start = int.MaxValue - 10000;                       // just before the wrap
            int end = DominionRules.StageEndMs(start, 20f);         // wraps to a large negative number
            Assert.Less(end, start, "the end really wrapped");
            Assert.IsFalse(DominionRules.CentrePayoutDue(end, unchecked(end - 1)));
            Assert.IsTrue(DominionRules.CentrePayoutDue(end, end));
            Assert.IsTrue(DominionRules.BountyDue(unchecked((start + 61000) - start), 60000, 0, 1));
            Assert.AreEqual(unchecked(start + 30000), DominionRules.NextCentrePayoutMs(start, unchecked(start + 100), 30000, 20000));
        }

        [Test] public void ThePayoutStepsStillCountRightWhenTheFirstPayoutIsBeforeTheWrapAndNowIsAfterIt()
        {
            int start = int.MaxValue - 40000;
            int first = unchecked(start + 30000);                    // still before the wrap
            int now = unchecked(start + 55000);                      // past the wrap
            Assert.Less(now, 0, "now really wrapped");
            Assert.Greater(first, 0, "the first payout did not");
            Assert.AreEqual(unchecked(first + 40000), DominionRules.NextCentrePayoutMs(start, now, 30000, 20000), "payouts at +0 and +20 s are past; the next is +40 s");
        }

        [Test] public void AStageEndIsTheStartPlusTheSeconds() => Assert.AreEqual(4500, DominionRules.StageEndMs(1000, 3.5f));
    }
}
