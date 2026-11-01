using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>The match edges that every body the host controls takes, human or bot: whose team just went out, who is told that three teams
    /// became two and who is sent home for it, and who has to wait out the respawn settle.</summary>
    public class BotMatchRulesTests
    {
        // ---- a team just went out ------------------------------------------------------------------

        [Test]
        public void ATeamNotOnTheListBeforeAndOnItNowJustWentOut() =>
            Assert.IsTrue(MatchPhaseRules.TeamJustEliminated(teamKnown: true, wasEliminated: false, isEliminated: true));

        [Test]
        public void ATeamAlreadyOnTheListBeforeDidNotJustGoOut() =>
            Assert.IsFalse(MatchPhaseRules.TeamJustEliminated(teamKnown: true, wasEliminated: true, isEliminated: true));

        [Test]
        public void ATeamStillInTheMatchDidNotJustGoOut() =>
            Assert.IsFalse(MatchPhaseRules.TeamJustEliminated(teamKnown: true, wasEliminated: false, isEliminated: false));

        [Test]
        public void ABodyWithNoTeamYetNeverJustWentOut() =>
            Assert.IsFalse(MatchPhaseRules.TeamJustEliminated(teamKnown: false, wasEliminated: false, isEliminated: true));

        // ---- three teams became two ----------------------------------------------------------------

        [Test]
        public void ASurvivingTeamIsToldWhenThreeTeamsBecomeTwo() =>
            Assert.IsTrue(MatchPhaseRules.TwoTeamsLeftReaches(MatchPhase.ThreeTeams, MatchPhase.TwoTeams, teamJustEliminated: false));

        [Test]
        public void TheTeamThatWentOutIsNotToldBecauseItIsGettingTheLostPanel() =>
            Assert.IsFalse(MatchPhaseRules.TwoTeamsLeftReaches(MatchPhase.ThreeTeams, MatchPhase.TwoTeams, teamJustEliminated: true));

        [TestCase(MatchPhase.Warmup, MatchPhase.TwoTeams)]
        [TestCase(MatchPhase.Warmup, MatchPhase.ThreeTeams)]
        [TestCase(MatchPhase.ThreeTeams, MatchPhase.ThreeTeams)]
        [TestCase(MatchPhase.TwoTeams, MatchPhase.TwoTeams)]
        [TestCase(MatchPhase.TwoTeams, MatchPhase.Over)]
        public void NoOtherChangeOfPhaseIsTheTwoTeamsLeftEdge(MatchPhase before, MatchPhase now) =>
            Assert.IsFalse(MatchPhaseRules.TwoTeamsLeftReaches(before, now, teamJustEliminated: false));

        [Test]
        public void ALivingBodyOfASurvivingTeamIsSentHome() =>
            Assert.IsTrue(MatchPhaseRules.SendsHomeOnPhaseChange(MatchPhase.ThreeTeams, MatchPhase.TwoTeams, teamJustEliminated: false, alive: true));

        [Test]
        public void ADeadBodyIsNotSentHomeItRespawnsInsteadOfThat() =>
            Assert.IsFalse(MatchPhaseRules.SendsHomeOnPhaseChange(MatchPhase.ThreeTeams, MatchPhase.TwoTeams, teamJustEliminated: false, alive: false));

        [Test]
        public void ABodyOfTheTeamThatWentOutIsNotSentHome() =>
            Assert.IsFalse(MatchPhaseRules.SendsHomeOnPhaseChange(MatchPhase.ThreeTeams, MatchPhase.TwoTeams, teamJustEliminated: true, alive: true));

        [Test]
        public void NobodyIsSentHomeWhenThePhaseDidNotGoFromThreeToTwo() =>
            Assert.IsFalse(MatchPhaseRules.SendsHomeOnPhaseChange(MatchPhase.TwoTeams, MatchPhase.Over, teamJustEliminated: false, alive: true));

        // ---- the master measuring a player back from the dead --------------------------------------

        [Test]
        public void ARemoteHumanJustBackFromTheDeadWaitsForItsPositionToArrive() =>
            Assert.IsTrue(ZoneThreat.WaitsForRespawnSettle(isLocalPlayer: false, isBot: false));

        [Test]
        public void TheMastersOwnPlayerDoesNotWait() =>
            Assert.IsFalse(ZoneThreat.WaitsForRespawnSettle(isLocalPlayer: true, isBot: false));

        [Test]
        public void ABotTheMasterPlaysDoesNotWait() =>
            Assert.IsFalse(ZoneThreat.WaitsForRespawnSettle(isLocalPlayer: false, isBot: true));
    }
}
