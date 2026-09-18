using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class MatchPhaseRulesTests
    {
        private static TeamStatus Team(int id, int members, int alive, bool holdsCapital) =>
            new TeamStatus { TeamId = id, Members = members, AliveMembers = alive, HoldsItsCapital = holdsCapital };

        [Test]
        public void ThreeTeamsIsPhaseOne()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 3, true), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void InPhaseOneALostCapitalWithSurvivorsIsALastStand()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 1, false), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        [Test]
        public void InPhaseOneALostCapitalAndNobodyAliveEliminatesAndMovesToPhaseTwo()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 0, false), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void EveryoneDeadButStillHoldingTheCapitalIsNotEliminated()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 0, true), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void InPhaseTwoLosingTheCapitalEliminatesAtOnce()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 2 }, new[] { Team(0, 3, 3, false), Team(1, 3, 3, true), Team(2, 3, 0, false) });
            CollectionAssert.AreEquivalent(new[] { 2, 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void EliminationIsPermanent()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 0 }, new[] { Team(0, 3, 3, true), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            CollectionAssert.Contains(r.Eliminated, 0);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void TeamsWithNoPlayersAreNotInTheMatch()
        {
            // A two-player test starts straight in the two-team phase.
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 1, 1, true), Team(1, 1, 1, true), Team(2, 0, 0, true) });
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void TheLastTeamLeftWins()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 0, 2 }, new[] { Team(0, 3, 0, false), Team(1, 3, 2, true), Team(2, 3, 0, false) });
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }
    }
}
