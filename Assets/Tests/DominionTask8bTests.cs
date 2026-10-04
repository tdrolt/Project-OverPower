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
            Hashtable props = DominionRoomWrites.LateJoinerPlayerProps(DominionStage.SuddenDeath, takingTeamSeat: true, nowMs: 123456);
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
            Assert.IsNull(DominionRoomWrites.LateJoinerPlayerProps(DominionStage.SuddenDeath, takingTeamSeat: false, nowMs: 5), "a spectator seat");
            Assert.IsNull(DominionRoomWrites.LateJoinerPlayerProps(DominionStage.Round, true, 5));
            Assert.IsNull(DominionRoomWrites.LateJoinerPlayerProps(DominionStage.Break, true, 5));
        }

        [Test] public void AJoinerWrittenDeadNeverCountsAsAliveForATiedTeam()
        {
            Hashtable props = DominionRoomWrites.LateJoinerPlayerProps(DominionStage.SuddenDeath, true, 150000);
            var joiner = new SuddenDeathRules.Player
            {
                Team = 1, Counts = true, HasAliveFlag = true, AliveFlag = (bool)props[PlayerLifecycle.AliveKey],
                HasDeathStamp = true, DeathStampMs = (int)props[PlayerLifecycle.LastStandAtKey],
            };
            // team 0's last player fell 3 s earlier; team 1 has only the joiner, dead from the seat write: the joiner's team fell last and wins
            SuddenDeathResult r = SuddenDeathRules.Judge(TallyOf(Fell(0, 147000), joiner), TiedTwo, Tolerance);
            Assert.AreEqual(SuddenDeathState.Won, r.State);
            Assert.AreEqual(1, r.Team);
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

        [Test] public void TheCircleStillCountsAsCombatLikeAnyLandedHit() =>
            Assert.IsTrue(HitVerdictRule.CountsAsCombat(HitVerdictRule.Classify(false, false, true, null, 5f, ignoresInvulnerability: true)));

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
