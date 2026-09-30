using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Task 9g (Tudor D28): Spectate after a knockout, and what a rejoin respawn costs.</summary>
    public class SpectateRulesTests
    {
        private static SpectateCandidate C(int actor, int team) => new SpectateCandidate(actor, team);

        // ---- who knocked you out

        [Test]
        public void TheTeamThatLandedTheKillingBlowIsTheOneThatKnockedYouOut() =>
            Assert.AreEqual(2, SpectateRules.KnockerTeam(lastKillerTeam: 2, ownCapitalOwner: 1, myTeam: 0));

        [Test]
        public void WithNoKnownKillerTheTeamHoldingYourBaseIsUsed() =>
            Assert.AreEqual(2, SpectateRules.KnockerTeam(lastKillerTeam: -1, ownCapitalOwner: 2, myTeam: 0));

        [Test]
        public void AKillerOnYourOwnTeamDoesNotCountSoTheBaseOwnerIsUsed() =>
            Assert.AreEqual(1, SpectateRules.KnockerTeam(lastKillerTeam: 0, ownCapitalOwner: 1, myTeam: 0));

        [TestCase(-1)] // nobody holds it (neutral, e.g. after the phase-two cut)
        [TestCase(0)]  // your own team still holds it: nobody took it
        public void WhenNeitherIsKnownThereIsNoPreferredTeam(int owner) =>
            Assert.AreEqual(SpectateRules.None, SpectateRules.KnockerTeam(-1, owner, myTeam: 0));

        // ---- the first press and the next presses

        [Test]
        public void TheFirstPressLandsOnTheLowestActorOfTheKnockerTeam()
        {
            var living = new[] { C(5, 1), C(3, 2), C(4, 2), C(9, 1) };
            Assert.AreEqual(3, SpectateRules.NextTarget(living, preferredTeam: 2, currentActor: SpectateRules.None));
        }

        [Test]
        public void EachPressMovesToTheNextLivingPlayerOfThatTeamAndWraps()
        {
            var living = new[] { C(3, 2), C(4, 2), C(7, 2), C(5, 1) };
            Assert.AreEqual(4, SpectateRules.NextTarget(living, 2, 3));
            Assert.AreEqual(7, SpectateRules.NextTarget(living, 2, 4));
            Assert.AreEqual(3, SpectateRules.NextTarget(living, 2, 7), "wraps inside the team");
        }

        [Test]
        public void ANextPressWithOnePlayerLeftStaysOnThem()
        {
            var living = new[] { C(3, 2), C(5, 1) };
            Assert.AreEqual(3, SpectateRules.NextTarget(living, 2, 3));
        }

        [Test]
        public void TheOrderDoesNotDependOnHowTheListIsSorted()
        {
            var living = new[] { C(7, 2), C(3, 2), C(4, 2) };
            Assert.AreEqual(3, SpectateRules.NextTarget(living, 2, SpectateRules.None));
            Assert.AreEqual(4, SpectateRules.NextTarget(living, 2, 3));
        }

        // ---- the knocker team has nobody alive

        [Test]
        public void WithNoLivingKnockerAnyLivingPlayerOfATeamStillInIsPickedInTeamOrder()
        {
            var living = new[] { C(8, 1), C(6, 1), C(2, 0) };
            Assert.AreEqual(2, SpectateRules.NextTarget(living, preferredTeam: 2, currentActor: SpectateRules.None));
            Assert.AreEqual(6, SpectateRules.NextTarget(living, 2, 2));
            Assert.AreEqual(8, SpectateRules.NextTarget(living, 2, 6));
            Assert.AreEqual(2, SpectateRules.NextTarget(living, 2, 8));
        }

        [Test]
        public void WithNoPreferredTeamAtAllAnyLivingPlayerIsPicked()
        {
            var living = new[] { C(4, 1) };
            Assert.AreEqual(4, SpectateRules.NextTarget(living, SpectateRules.None, SpectateRules.None));
        }

        [Test]
        public void ACurrentTargetWhoIsNoLongerAliveStartsTheOrderAgain()
        {
            var living = new[] { C(3, 2), C(4, 2) };
            Assert.AreEqual(3, SpectateRules.NextTarget(living, 2, 99));
        }

        [Test]
        public void NobodyAliveMeansNoTarget()
        {
            Assert.AreEqual(SpectateRules.None, SpectateRules.NextTarget(new SpectateCandidate[0], 2, SpectateRules.None));
            Assert.AreEqual(SpectateRules.None, SpectateRules.NextTarget(null, 2, SpectateRules.None));
        }

        // ---- nobody watchable right now

        [Test]
        public void WithNobodyToPickTheCurrentTargetIsKept() =>
            Assert.AreEqual(4, SpectateRules.PickOrKeep(SpectateRules.None, currentActor: 4));

        [Test]
        public void WithNobodyToPickAndNoTargetThereStaysNoTarget() =>
            Assert.AreEqual(SpectateRules.None, SpectateRules.PickOrKeep(SpectateRules.None, SpectateRules.None));

        [Test]
        public void APickedPlayerReplacesTheCurrentTarget() =>
            Assert.AreEqual(7, SpectateRules.PickOrKeep(7, currentActor: 4));

        // ---- when the button shows

        [TestCase(MatchPhase.Warmup)]
        [TestCase(MatchPhase.ThreeTeams)]
        [TestCase(MatchPhase.TwoTeams)]
        public void TheButtonShowsOnTheLosePanelWhileTheMatchRuns(MatchPhase phase) =>
            Assert.IsTrue(SpectateRules.ButtonVisible(losePanelShown: true, phase));

        [Test]
        public void TheButtonIsGoneOnceTheMatchIsOver() =>
            Assert.IsFalse(SpectateRules.ButtonVisible(true, MatchPhase.Over));

        [Test]
        public void NoLosePanelNoButton() =>
            Assert.IsFalse(SpectateRules.ButtonVisible(false, MatchPhase.TwoTeams));

        // ---- the rejoiner onto a knocked-out team

        [TestCase(MatchPhase.ThreeTeams)]
        [TestCase(MatchPhase.TwoTeams)]
        public void ARejoinerOnAKnockedOutTeamLandsOnTheLosePanel(MatchPhase phase) =>
            Assert.IsTrue(RejoinRules.LandsOnLosePanel(teamEliminated: true, phase));

        [Test]
        public void ARejoinerOnATeamStillInDoesNot() =>
            Assert.IsFalse(RejoinRules.LandsOnLosePanel(false, MatchPhase.ThreeTeams));

        [Test]
        public void WhenTheMatchIsOverTheResultScreenDecidesInstead() =>
            Assert.IsFalse(RejoinRules.LandsOnLosePanel(true, MatchPhase.Over));

        // ---- the rejoin respawn

        [Test]
        public void TheDeathCountComesBackFromTheScoreboardDeaths() =>
            Assert.AreEqual(4, RespawnDelayRules.DeathCountOnRejoin(new[] { 7, 4, 2, 950, 1 }));

        [Test]
        public void NoScoreboardMeansNoDeaths() =>
            Assert.AreEqual(0, RespawnDelayRules.DeathCountOnRejoin(null));

        [Test]
        public void AShortOrNegativeScoreboardMeansNoDeaths()
        {
            Assert.AreEqual(0, RespawnDelayRules.DeathCountOnRejoin(new[] { 3 }));
            Assert.AreEqual(0, RespawnDelayRules.DeathCountOnRejoin(new[] { 3, -2, 0, 0, 0 }));
        }

        [TestCase(5f, 5f)]
        [TestCase(0f, 0f)]
        [TestCase(-3f, 0f)]
        public void TheRejoinWaitIsTheFlatNumberNeverNegative(float flat, float expected) =>
            Assert.AreEqual(expected, RespawnDelayRules.RejoinDelay(flat));

        [Test]
        public void TheRejoinRespawnDoesNotChargeADeathWhateverTheCountdownFlag()
        {
            Assert.AreEqual(4, RespawnDelayRules.DeathCountForRetake(4, countdownAlreadyCounted: false, rejoinRespawn: true));
            Assert.AreEqual(4, RespawnDelayRules.DeathCountForRetake(4, countdownAlreadyCounted: true, rejoinRespawn: true));
        }

        [Test]
        public void AnOrdinaryRespawnStillChargesOnceAsBefore()
        {
            Assert.AreEqual(5, RespawnDelayRules.DeathCountForRetake(4, false, false));
            Assert.AreEqual(4, RespawnDelayRules.DeathCountForRetake(4, true, false));
        }

        [Test]
        public void TheNextDeathAfterARejoinCountsOnFromWhereThePlayerWas()
        {
            // 4 deaths before the drop; base 5, +2 each, cap 35 (the config's numbers).
            int count = RespawnDelayRules.DeathCountOnRejoin(new[] { 0, 4, 0, 0, 0 });
            count = RespawnDelayRules.DeathCountForRetake(count, false, rejoinRespawn: true);
            Assert.AreEqual(4, count, "the rejoin respawn itself adds nothing");
            count = RespawnDelayRules.DeathCountForRetake(count, false, rejoinRespawn: false); // the next real death
            Assert.AreEqual(5, count);
            Assert.AreEqual(13f, RespawnDelayRules.Delay(count, 5f, 2f, 35f), "fifth death: 5 + 2 * 4");
        }
    }
}
