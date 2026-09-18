using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class MatchPhaseRulesTests
    {
        private static TeamStatus Team(int id, int members, int outForLastStand, bool holdsCapital) =>
            new TeamStatus { TeamId = id, Members = members, MembersOutForLastStand = outForLastStand, HoldsItsCapital = holdsCapital };

        [Test]
        public void ThreeTeamsIsPhaseOne()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 0, true), Team(1, 3, 0, true), Team(2, 3, 0, true) });
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void InPhaseOneALostCapitalWithSurvivorsIsALastStand()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 2, false), Team(1, 3, 0, true), Team(2, 3, 0, true) });
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        [Test]
        public void InPhaseOneALostCapitalAndEveryoneOutEliminatesAndMovesToPhaseTwo()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 3, false), Team(1, 3, 0, true), Team(2, 3, 0, true) });
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void EveryoneOutButStillHoldingTheCapitalIsNotEliminated()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 3, true), Team(1, 3, 0, true), Team(2, 3, 0, true) });
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void EliminationIsPermanent()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 0 }, new[] { Team(0, 3, 0, true), Team(1, 3, 0, true), Team(2, 3, 0, true) });
            CollectionAssert.Contains(r.Eliminated, 0);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void TeamsWithNoPlayersAreNotInTheMatch()
        {
            // Tudor's rule (docs/superpowers/specs/2026-09-16-telemetry-design.md): the phase changes
            // only on an elimination, never on who is connected - a two-player test has no third team
            // to eliminate, so it stays Phase 1 (ThreeTeams).
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 1, 0, true), Team(1, 1, 0, true), Team(2, 0, 0, true) });
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        [Test]
        public void TheLastTeamLeftWins()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 0, 2 }, new[] { Team(0, 3, 3, false), Team(1, 3, 0, true), Team(2, 3, 3, false) });
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void ASoloPlayerNeverWinsTheMatchAlone()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 1, 0, true), Team(1, 0, 0, true), Team(2, 0, 0, true) });
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.AreEqual(-1, r.Winner);
        }

        [Test]
        public void TwoTeamsWhereBLeavesWithNoEliminationIsNotOver()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 0, true), Team(1, 0, 0, true), Team(2, 0, 0, true) });
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void TwoTeamsWhereBsCapitalIsLostAndBThenDiesEliminatesBAndEndsTheMatch()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 1, 0, true), Team(1, 1, 1, false), Team(2, 0, 0, true) });
            CollectionAssert.AreEqual(new[] { 1 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(0, r.Winner);
        }

        [Test]
        public void AThirdPlayerJoiningAfterTheFirstEliminationDoesNotBringBackThreeTeams()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 1 }, new[] { Team(0, 3, 0, true), Team(1, 3, 3, false), Team(2, 1, 0, true) });
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void ALeaverBeforeAnyEliminationJustLeaves()
        {
            // Question for Tudor (see assumptions-for-tudor.md): a team's last player leaving before
            // any elimination must never end the match on its own.
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 0, true), Team(1, 3, 0, true), Team(2, 0, 0, true) });
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        [Test]
        public void ALeaverInTheFinalTwoAfterAnEliminationForfeitsTheMatch()
        {
            // Same question, default answer (a): once an elimination has happened, the last player of
            // the other remaining team leaving forfeits the match to whoever is left.
            var r = MatchPhaseRules.Recompute(new List<int> { 2 }, new[] { Team(0, 3, 0, true), Team(1, 0, 0, true), Team(2, 3, 3, false) });
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(0, r.Winner);
        }

        [Test]
        public void OnlyADeathAfterTheCapitalIsLostCountsForTheLastStand()
        {
            Assert.IsFalse(MatchPhaseRules.IsLastStandDeath(teamHoldsCapitalAtDeath: true));
            Assert.IsTrue(MatchPhaseRules.IsLastStandDeath(teamHoldsCapitalAtDeath: false));
        }
    }
}
