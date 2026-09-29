using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>2.7b (Tudor, 2026-09-18): who is out and which phase the match is in once it is live. Rewritten from
    /// 2.7: a host-started match is TwoTeams from the start, an emptied team stays in, and there is no draw.</summary>
    public class MatchPhaseRulesTests
    {
        private static TeamStatus Holds(int id, int members = 1, int outForLastStand = 0) =>
            new TeamStatus { TeamId = id, InMatch = true, Members = members, MembersOutForLastStand = outForLastStand,
                             HoldsOwnCapital = true, HoldsAnyCapitalInPlay = true };

        /// <summary>Holds no capital at all. lastOutAt: the server ms its latest member went out (null = nobody did).</summary>
        private static TeamStatus Lost(int id, int members = 1, int outForLastStand = 0, int? lastOutAt = null) =>
            new TeamStatus { TeamId = id, InMatch = true, Members = members, MembersOutForLastStand = outForLastStand,
                             HoldsOwnCapital = false, HoldsAnyCapitalInPlay = false, LastOutAtMs = lastOutAt };

        /// <summary>Lost its own capital but holds another team's.</summary>
        private static TeamStatus Adopted(int id, int members = 1, int outForLastStand = 0) =>
            new TeamStatus { TeamId = id, InMatch = true, Members = members, MembersOutForLastStand = outForLastStand,
                             HoldsOwnCapital = false, HoldsAnyCapitalInPlay = true };

        /// <summary>The third team of a host-started two-team match.</summary>
        private static TeamStatus NotInMatch(int id) => new TeamStatus { TeamId = id, InMatch = false };

        private static MatchPhaseResult Run(params TeamStatus[] teams) => MatchPhaseRules.Recompute(new List<int>(), teams);
        private static MatchPhaseResult Run(int[] alreadyOut, params TeamStatus[] teams) => MatchPhaseRules.Recompute(alreadyOut, teams);

        // ---- the phase

        [Test]
        public void ThreeTeamsInTheMatchIsPhaseOne()
        {
            var r = Run(Holds(0, 3), Holds(1, 3), Holds(2, 3));
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(-1, r.Winner);
        }

        [Test]
        public void AHostStartedMatchIsTheTwoTeamPhaseFromTheStart()
        {
            var r = Run(Holds(0), Holds(1), NotInMatch(2));
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void PlayersLeavingNeverMoveThePhase()
        {
            // Telemetry spec Part 3: the phase changes only on a knockout. Two emptied teams still holding capitals are in.
            var r = Run(Holds(0, 3), Holds(1, members: 0), Holds(2, members: 0));
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
        }

        // ---- three teams: the last stand

        [Test]
        public void WithThreeTeamsALostCapitalWithSurvivorsIsALastStand()
        {
            var r = Run(Lost(0, 3, 2), Holds(1, 3), Holds(2, 3));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        [Test]
        public void WithThreeTeamsNoCapitalAndEveryoneOutKnocksTheTeamOut()
        {
            var r = Run(Lost(0, 3, 3), Holds(1, 3), Holds(2, 3));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void EveryoneOutButStillHoldingTheCapitalIsNotOut()
        {
            Assert.IsEmpty(Run(Holds(0, 3, 3), Holds(1, 3), Holds(2, 3)).Eliminated);
        }

        [Test]
        public void WithThreeTeamsAnEmptiedTeamStaysInUntilItsCapitalFalls()
        {
            // Tudor rule 2: nobody is left to make a last stand, so the capital falling IS the knockout.
            Assert.IsEmpty(Run(Holds(0, members: 0), Holds(1, 3), Holds(2, 3)).Eliminated);
            CollectionAssert.AreEqual(new[] { 0 }, Run(Lost(0, members: 0), Holds(1, 3), Holds(2, 3)).Eliminated);
        }

        [Test]
        public void WithThreeTeamsAnotherTeamsCapitalKeepsYouIn()
        {
            // Tudor, 2026-09-18 afternoon: yes - adoption counts in the three-team phase too (GDD p.20).
            var r = Run(Adopted(0, 3, 3), Holds(1, 3), Lost(2, 3, 1));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        // ---- two teams

        [Test]
        public void WithTwoTeamsLosingYourCapitalWhileTheOtherHoldsOneIsNoLongerInstant()
        {
            // Tudor D17 (2026-09-29): the two-team instant knockout is gone - both phases play the same last stand.
            var r = Run(Lost(0, 3, 0), Holds(1, 3), NotInMatch(2));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void WithTwoTeamsNoCapitalAndEveryMemberOutIsOutEvenWhenTheOtherHoldsOne()
        {
            var r = Run(Lost(0, 3, 3), Holds(1, 3), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void WithTwoTeamsHoldingAnotherTeamsCapitalIsNotALastStand()
        {
            Assert.IsEmpty(Run(Adopted(0, 3, 3), Holds(1, 3), NotInMatch(2)).Eliminated);
        }

        [Test]
        public void WithTwoTeamsAnEmptiedTeamStaysInWhileItHoldsItsCapital()
        {
            var r = Run(Holds(0), Holds(1, members: 0), NotInMatch(2));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void WithTwoTeamsAnEmptiedTeamIsOutTheMomentItsCapitalFalls()
        {
            var r = Run(Holds(0), Lost(1, members: 0), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 1 }, r.Eliminated);
            Assert.AreEqual(0, r.Winner);
        }

        [Test]
        public void BothWithoutACapitalIsLastManStandingNotAKnockout()
        {
            var r = Run(Lost(0, 2, 1), Lost(1, 2, 1), NotInMatch(2));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void LastManStandingTheWipedTeamLosesEvenIfTheWinnerHoldsNothing()
        {
            var r = Run(Lost(0, 2, 2), Lost(1, 2, 1), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void LastManStandingAnEmptiedTeamLoses()
        {
            var r = Run(Lost(0, members: 0), Lost(1, 1, 0), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void TakingACapitalInTheLastStandDoesNotKnockOutTheOtherTeamWhileItHasSurvivors()
        {
            // Was "knocks out the other team at once" - Tudor D17: it is out only when every member is out.
            var r = Run(Adopted(0, 2, 1), Lost(1, 2, 1), NotInMatch(2));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        // ---- no draw: the last team to die wins (Tudor, 2026-09-18 afternoon)

        [Test]
        public void NoDrawTheTeamWhoseLastPlayerDiedLastWins()
        {
            var r = Run(Lost(0, 1, 1, lastOutAt: 1000), Lost(1, 1, 1, lastOutAt: 1200), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void NoDrawATrueTieOnTheSameMillisecondGoesToTheLowerTeamNumber()
        {
            var r = Run(Lost(0, 1, 1, lastOutAt: 1000), Lost(1, 1, 1, lastOutAt: 1000), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 1 }, r.Eliminated);
            Assert.AreEqual(0, r.Winner);
        }

        [Test]
        public void NoDrawTheLastDeathIsReadAcrossTheServerClockWrap()
        {
            // PhotonNetwork.ServerTimestamp wraps: int.MinValue + 10 is 21 ms AFTER int.MaxValue - 10.
            var r = Run(Lost(0, 1, 1, lastOutAt: int.MaxValue - 10), Lost(1, 1, 1, lastOutAt: int.MinValue + 10), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void NoDrawATeamThatEmptiedLosesToATeamThatDied()
        {
            // Nobody on team 0 died - its players left - so it has no stamp and counts as out first.
            var r = Run(Lost(0, members: 0), Lost(1, 1, 1, lastOutAt: 1000), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void EveryTeamWipedAtOnceStillEndsWithAWinner()
        {
            var r = Run(Lost(0, 1, 1, lastOutAt: 300), Lost(1, 1, 1, lastOutAt: 100), Lost(2, 1, 1, lastOutAt: 200));
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(0, r.Winner);
        }

        // ---- across the three-to-two transition

        [Test]
        public void TheKnockoutCascadeEndsTheMatchInOneRecompute()
        {
            // A is wiped with no capital and goes out; B has lost its capital and is wiped too (all three out). Two
            // teams remain after A, and B, wiped, goes out in the same call - one call must see both. (Was: B out at
            // once because C holds a capital; that instant rule is gone, D17.)
            var r = Run(Lost(0, 3, 3), Lost(1, 3, 3), Holds(2, 3));
            CollectionAssert.AreEqual(new[] { 0, 1 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(2, r.Winner);
        }

        [Test]
        public void ACapitallessTeamWithSurvivorsSurvivesTheKnockoutOfAnotherTeam()
        {
            var r = Run(Lost(0, 3, 3), Lost(1, 3, 0), Holds(2, 3));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void AKnockoutThatLeavesTwoCapitallessTeamsStartsLastManStanding()
        {
            // Replaces 2.7's draw ("a match can end with nobody winning"): both survivors hold nothing, so neither goes out.
            var r = Run(Lost(0, 3, 3), Lost(1, 3, 1), Lost(2, 3, 2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
            Assert.AreEqual(-1, r.Winner);
        }

        [Test]
        public void TwoTeamsWipedInTheSameThreeTeamRecomputeBothGoAndTheThirdWins()
        {
            var r = Run(Lost(0, 3, 3), Lost(1, 3, 3), Lost(2, 3, 1));
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, r.Eliminated);
            Assert.AreEqual(2, r.Winner);
        }

        // ---- permanence

        [Test]
        public void AKnockoutIsPermanent()
        {
            var r = Run(new[] { 0 }, Holds(0, 3), Holds(1, 3), Holds(2, 3));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void TheLastTeamLeftWins()
        {
            var r = Run(new[] { 0, 2 }, Lost(0, 3, 3), Holds(1, 3), Lost(2, 3, 3));
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        // ---- the small rules the adapters ask

        [Test]
        public void HavingACapitalMeansAnyCapitalInPlayInEveryPhase()
        {
            // Tudor's answer 1 pins here (any capital in play, both phases). Were it ever reversed for the three-team
            // phase: `phase == ThreeTeams ? holdsOwnCapital : holdsAnyCapitalInPlay`, and flip the first assert and
            // WithThreeTeamsAnotherTeamsCapitalKeepsYouIn.
            Assert.IsTrue(MatchPhaseRules.CountsAsHavingACapital(MatchPhase.ThreeTeams, holdsOwnCapital: false, holdsAnyCapitalInPlay: true));
            Assert.IsTrue(MatchPhaseRules.CountsAsHavingACapital(MatchPhase.TwoTeams, false, true));
            Assert.IsTrue(MatchPhaseRules.CountsAsHavingACapital(MatchPhase.TwoTeams, true, true));
            Assert.IsFalse(MatchPhaseRules.CountsAsHavingACapital(MatchPhase.ThreeTeams, false, false));
        }

        [Test]
        public void OnlyALiveDeathWithNoCapitalIsALastStandDeath()
        {
            Assert.IsFalse(MatchPhaseRules.IsLastStandDeath(live: false, teamHasACapital: false), "warm-up: always an ordinary respawn");
            Assert.IsFalse(MatchPhaseRules.IsLastStandDeath(live: true, teamHasACapital: true));
            Assert.IsTrue(MatchPhaseRules.IsLastStandDeath(live: true, teamHasACapital: false));
        }

        private static MatchPhaseRules.CapitalHold Hold(int zone, int owner, int since) =>
            new MatchPhaseRules.CapitalHold { Zone = zone, Owner = owner, HeldSinceMs = since };

        [Test]
        public void YouRespawnAtYourOwnCapitalWhileYouHoldIt()
        {
            // "A team holding a second capital still respawns at its own" - even one it took earlier.
            Assert.AreEqual(6, MatchPhaseRules.RespawnCapital(0, 6, new[] { Hold(7, 0, 100), Hold(6, 0, 5000), Hold(8, 1, 0) }));
        }

        [Test]
        public void WithoutYourOwnYouRespawnAtTheCapitalHeldLongest()
        {
            Assert.AreEqual(8, MatchPhaseRules.RespawnCapital(0, 6, new[] { Hold(6, 1, 0), Hold(7, 0, 2000), Hold(8, 0, 1000) }));
        }

        [Test]
        public void HeldLongestSurvivesTheServerClockWrapping()
        {
            // PhotonNetwork.ServerTimestamp is an int that wraps: the stamp just before the wrap is the OLDER hold.
            Assert.AreEqual(7, MatchPhaseRules.RespawnCapital(0, 6, new[] { Hold(8, 0, int.MinValue + 100), Hold(7, 0, int.MaxValue - 100) }));
        }

        [Test]
        public void NoCapitalMeansNowhereToRespawn()
        {
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.RespawnCapital(0, 6, new[] { Hold(6, 1, 0), Hold(7, 1, 0) }));
        }

        [Test]
        public void WhereAnEndedCountdownPutsYou()
        {
            const int own = 6, adopted = 7, none = TerritoryMap.Neutral;
            Assert.AreEqual(own, MatchPhaseRules.SpawnCapitalFor(MatchPhase.Warmup, false, own, none), "warm-up: always home");
            Assert.AreEqual(adopted, MatchPhaseRules.SpawnCapitalFor(MatchPhase.TwoTeams, false, own, adopted));
            Assert.AreEqual(none, MatchPhaseRules.SpawnCapitalFor(MatchPhase.ThreeTeams, false, own, none), "D17: a countdown that ends with no base becomes the wait, in both phases");
            Assert.AreEqual(adopted, MatchPhaseRules.SpawnCapitalFor(MatchPhase.ThreeTeams, false, own, adopted), "holding another team's base: respawn there");
            Assert.AreEqual(own, MatchPhaseRules.SpawnCapitalFor(MatchPhase.ThreeTeams, false, own, own), "retaken: the waiting respawn at home again");
            Assert.AreEqual(none, MatchPhaseRules.SpawnCapitalFor(MatchPhase.TwoTeams, false, own, none), "no base: the dead wait");
            Assert.AreEqual(none, MatchPhaseRules.SpawnCapitalFor(MatchPhase.ThreeTeams, true, own, own), "a knocked-out team never respawns");
        }

        [Test]
        public void ATerritoryWinCountsOnlyOnceLiveAndOnlyForEveryCapitalInPlay()
        {
            // Replaces 2.7's "two teams with players" guard: going live already needed two teams, and Tudor's rule 2
            // keeps an emptied team in - a survivor who takes its capital has won it.
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(live: false, new[] { 0, 0, 0 }));
            Assert.AreEqual(0, MatchPhaseRules.TerritoryWinner(true, new[] { 0, 0, 0 }));
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(true, new[] { 0, 0, 1 }));
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(true, new[] { 0, 0, TerritoryMap.Neutral }));
            Assert.AreEqual(1, MatchPhaseRules.TerritoryWinner(true, new[] { 1, 1 }), "a host-started match has two capitals in play");
        }

        [Test]
        public void AnAdoptionIsACapitallessTeamTakingSomeoneElsesCapital()
        {
            Assert.IsTrue(MatchPhaseRules.IsAdoption(newOwner: 0, capitalTeamOfZone: 1, otherCapitalsInPlayHeldByNewOwner: 0));
            Assert.IsFalse(MatchPhaseRules.IsAdoption(0, 0, 0), "retaking your own capital is a recapture");
            Assert.IsFalse(MatchPhaseRules.IsAdoption(0, 1, 1), "a team that already had a capital just took a second");
            Assert.IsFalse(MatchPhaseRules.IsAdoption(TerritoryMap.Neutral, 1, 0));
        }

        // ---- Task 9b-2: follow-ups to the last stand

        /// <summary>Holds no capital; deadNow members are dead (last-stand wait or a respawn countdown still running).</summary>
        private static TeamStatus LostWithDead(int id, int members, int deadNow, int? lastOutAt = null) =>
            new TeamStatus { TeamId = id, InMatch = true, Members = members, MembersDead = deadNow,
                             HoldsOwnCapital = false, HoldsAnyCapitalInPlay = false, LastOutAtMs = lastOutAt };

        [Test]
        public void ARespawnCountdownStillRunningCountsAsOutOnceTheTeamHasNoBase()
        {
            // Two members: one waiting (lastStand), one still on a countdown (dead, not yet waiting).
            var team = new TeamStatus { TeamId = 0, InMatch = true, Members = 2, MembersOutForLastStand = 1, MembersDead = 2 };
            var r = Run(team, Holds(1, 2), Holds(2, 2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
        }

        [Test]
        public void ALivingMemberKeepsTheBaselessTeamIn()
        {
            var r = Run(LostWithDead(0, 2, 1), Holds(1, 2), Holds(2, 2));
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void DeadMembersOfATeamThatStillHoldsABaseAreNotOut()
        {
            var team = new TeamStatus { TeamId = 0, InMatch = true, Members = 2, MembersDead = 2, HoldsOwnCapital = true, HoldsAnyCapitalInPlay = true };
            Assert.IsEmpty(Run(team, Holds(1, 2), Holds(2, 2)).Eliminated);
        }

        [Test]
        public void NoDrawUsesTheMomentOfDeathNotTheEndOfTheCountdown()
        {
            // Team 0's last member died at 1000 (its countdown ended later, in the wait); team 1's last died at 2000.
            var r = Run(LostWithDead(0, 1, 1, lastOutAt: 1000), LostWithDead(1, 1, 1, lastOutAt: 2000));
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void TwoTeamsTheEnemyTakingYourBaseDoesNotEndTheMatchWhileYouLive()
        {
            var teams = new[] { LostWithDead(0, 2, 0), Holds(1, 2) };
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(true, new[] { 1, 1 }, teams),
                "team 0 has no base but is alive: its last stand comes first");
            Assert.IsEmpty(Run(teams).Eliminated);
            Assert.AreEqual(-1, Run(teams).Winner);
        }

        [Test]
        public void TwoTeamsTheTerritoryWinCountsOnceTheOtherTeamHasNobodyAlive()
        {
            var teams = new[] { LostWithDead(0, 2, 2), Holds(1, 2) };
            Assert.AreEqual(1, MatchPhaseRules.TerritoryWinner(true, new[] { 1, 1 }, teams));
            Assert.AreEqual(1, Run(teams).Winner, "and the last stand ends the same way");
        }

        [Test]
        public void ThreeTeamsAKnockedOutTeamsCutBaseDoesNotHandTheHolderAWinOverALivingBaselessTeam()
        {
            // Team 0 knocked out earlier (everyone dead), team 2 has no base but a living member, team 1 holds every base in play.
            var teams = new[] { LostWithDead(0, 2, 2), Holds(1, 2), LostWithDead(2, 2, 1) };
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(true, new[] { 1, 1 }, teams));
            var again = Run(new[] { 0 }, teams);
            Assert.AreEqual(-1, again.Winner);
            Assert.IsEmpty(again.Eliminated.FindAll(t => t != 0));
        }

        [Test]
        public void TheTerritoryWinIgnoresATeamNotInTheMatchAndAnEmptiedTeam()
        {
            var teams = new[] { Holds(0, 2), NotInMatch(1), new TeamStatus { TeamId = 2, InMatch = true, Members = 0 } };
            Assert.AreEqual(0, MatchPhaseRules.TerritoryWinner(true, new[] { 0, 0 }, teams));
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(false, new[] { 0, 0 }, teams));
        }

        [Test]
        public void ANewMemberOfATeamInItsLastStandSpawnsIntoTheWait()
        {
            const int own = 6, none = TerritoryMap.Neutral;
            Assert.IsTrue(MatchPhaseRules.JoinsIntoLastStand(MatchPhase.TwoTeams, false, own, none));
            Assert.IsTrue(MatchPhaseRules.JoinsIntoLastStand(MatchPhase.ThreeTeams, false, own, none));
            Assert.IsFalse(MatchPhaseRules.JoinsIntoLastStand(MatchPhase.ThreeTeams, false, own, own), "the team holds a base: spawn normally");
            Assert.IsFalse(MatchPhaseRules.JoinsIntoLastStand(MatchPhase.TwoTeams, false, own, 7), "holding another team's base counts");
            Assert.IsFalse(MatchPhaseRules.JoinsIntoLastStand(MatchPhase.Warmup, false, own, none), "nothing counts before live");
            Assert.IsFalse(MatchPhaseRules.JoinsIntoLastStand(MatchPhase.Over, false, own, none), "an ended match has no last stand");
        }

        [Test]
        public void AJoinerIsJudgedOnTheTeamItWasSpawnedOnNotAStaleOrMissingProperty()
        {
            Assert.AreEqual(1, MatchPhaseRules.TeamForJoinCheck(instantiatedTeam: 1, propertyTeam: 0), "an in-process rejoiner's property is its OLD team");
            Assert.AreEqual(1, MatchPhaseRules.TeamForJoinCheck(1, null), "a fresh process has no property yet at Start");
            Assert.AreEqual(0, MatchPhaseRules.TeamForJoinCheck(null, 0), "no instantiation data: fall back to the property");
            Assert.AreEqual(0, MatchPhaseRules.TeamForJoinCheck(-1, 0));
            Assert.IsNull(MatchPhaseRules.TeamForJoinCheck(null, null), "not known yet: keep asking");
        }

        [Test]
        public void ARetakeRespawnAfterAnEndedCountdownIsNotChargedASecondDeath()
        {
            Assert.AreEqual(3, RespawnDelayRules.DeathCountForRetake(3, countdownAlreadyCounted: true));
            Assert.AreEqual(4, RespawnDelayRules.DeathCountForRetake(3, countdownAlreadyCounted: false), "a last-stand death is charged once, on the retake");
        }

        [Test]
        public void TheRespawnDelayGrowsPerDeathUpToTheCap()
        {
            Assert.AreEqual(5f, RespawnDelayRules.Delay(1, 5f, 1f, 10f));
            Assert.AreEqual(7f, RespawnDelayRules.Delay(3, 5f, 1f, 10f));
            Assert.AreEqual(10f, RespawnDelayRules.Delay(20, 5f, 1f, 10f));
        }
    }
}
