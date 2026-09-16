using System;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class DrainRuleTests
    {
        private const int Owner = 0;
        private static readonly Func<int, bool> AnyTeamMay = team => true;
        private static readonly Func<int, bool> NoTeamMay = team => false;

        private static DrainRule.Decision Decide(int[] teams, bool defender, bool draining, int drainingTeam,
                                                 Func<int, bool> mayCapture) =>
            DrainRule.Decide(Owner, teams, defender, draining, drainingTeam, mayCapture);

        [Test]
        public void AnEnemyAloneStartsADrain()
        {
            DrainRule.Decision d = Decide(new[] { 1 }, false, false, -1, AnyTeamMay);
            Assert.AreEqual(DrainRule.Step.Start, d.Step);
            Assert.AreEqual(1, d.Team);
        }

        [Test]
        public void ARunningDrainContinues()
        {
            DrainRule.Decision d = Decide(new[] { 1 }, false, true, 1, AnyTeamMay);
            Assert.AreEqual(DrainRule.Step.Continue, d.Step);
            Assert.AreEqual(1, d.Team);
        }

        [Test]
        public void NobodyInsideAndNothingRunningIsNothing()
        {
            Assert.AreEqual(DrainRule.Step.None, Decide(new int[0], false, false, -1, AnyTeamMay).Step);
        }

        [Test]
        public void ADefenderStopsADrain()
        {
            Assert.AreEqual(DrainRule.Step.Stop, Decide(new[] { 1 }, true, true, 1, AnyTeamMay).Step);
        }

        [Test]
        public void ADefenderStopsADrainStartingAtAll()
        {
            Assert.AreEqual(DrainRule.Step.None, Decide(new[] { 1 }, true, false, -1, AnyTeamMay).Step);
        }

        [Test]
        public void EveryAttackerLeavingStopsADrain()
        {
            Assert.AreEqual(DrainRule.Step.Stop, Decide(new int[0], false, true, 1, AnyTeamMay).Step);
        }

        [Test]
        public void TheOwnersOwnTeamListedInsideNeverDrains()
        {
            Assert.AreEqual(DrainRule.Step.None, Decide(new[] { Owner }, false, false, -1, AnyTeamMay).Step);
        }

        [Test]
        public void ADrainWhoseWayInComesUnderAttackPauses()
        {
            DrainRule.Decision d = Decide(new[] { 1 }, false, true, 1, NoTeamMay);
            Assert.AreEqual(DrainRule.Step.Pause, d.Step);
            Assert.AreEqual(1, d.Team);
        }

        [Test]
        public void APausedDrainCarriesOnRatherThanStartingAgainOnceItsWayInIsSafe()
        {
            // Pause keeps "draining" true on the tower, so the next safe frame is Continue (progress kept), not Start
            // (progress back to full).
            Assert.AreEqual(DrainRule.Step.Pause, Decide(new[] { 1 }, false, true, 1, NoTeamMay).Step);
            Assert.AreEqual(DrainRule.Step.Continue, Decide(new[] { 1 }, false, true, 1, AnyTeamMay).Step);
        }

        [Test]
        public void AnAttackerWithNoSafeWayInDoesNotStartADrain()
        {
            Assert.AreEqual(DrainRule.Step.None, Decide(new[] { 1 }, false, false, -1, NoTeamMay).Step);
        }

        [Test]
        public void ADefenderStopsAPausedDrain()
        {
            Assert.AreEqual(DrainRule.Step.Stop, Decide(new[] { 1 }, true, true, 1, NoTeamMay).Step);
        }

        [Test]
        public void AttackersLeavingAPausedDrainStopIt()
        {
            Assert.AreEqual(DrainRule.Step.Stop, Decide(new int[0], false, true, 1, NoTeamMay).Step);
        }

        [Test]
        public void WhenTheDrainingTeamLeavesTheTeamStillDrainingIsNamed()
        {
            DrainRule.Decision d = Decide(new[] { 2 }, false, true, 1, AnyTeamMay);
            Assert.AreEqual(DrainRule.Step.Continue, d.Step);
            Assert.AreEqual(2, d.Team);
        }

        [Test]
        public void WhenTheDrainingTeamLosesItsWayInAnotherTeamThatMayTakesOver()
        {
            DrainRule.Decision d = Decide(new[] { 1, 2 }, false, true, 1, team => team == 2);
            Assert.AreEqual(DrainRule.Step.Continue, d.Step);
            Assert.AreEqual(2, d.Team);
        }

        [Test]
        public void TheTeamAlreadyDrainingKeepsItWhenASecondAttackerWalksIn()
        {
            DrainRule.Decision d = Decide(new[] { 2, 1 }, false, true, 1, AnyTeamMay);
            Assert.AreEqual(DrainRule.Step.Continue, d.Step);
            Assert.AreEqual(1, d.Team);
        }

        [Test]
        public void LeavingAnOwnedZoneNeverEndsItsDrain()
        {
            Assert.IsFalse(DrainRule.LeavingEndsCapture(zoneOwned: true, capturingTeamStillInside: false));
        }

        [Test]
        public void TheLastCapturerLeavingANeutralZoneEndsTheCapture()
        {
            Assert.IsTrue(DrainRule.LeavingEndsCapture(zoneOwned: false, capturingTeamStillInside: false));
            Assert.IsFalse(DrainRule.LeavingEndsCapture(zoneOwned: false, capturingTeamStillInside: true));
        }

        [Test]
        public void ADrainerLeavingAndComingBackInOneNetworkUpdateKeepsTheDrainGoing()
        {
            // The leave doesn't touch the drain (owned zone), and on the next tick the drainer is listed again, so the
            // drain continues from its progress rather than starting over or ending.
            Assert.IsFalse(DrainRule.LeavingEndsCapture(zoneOwned: true, capturingTeamStillInside: false));
            Assert.AreEqual(DrainRule.Step.Continue, Decide(new[] { 1 }, false, true, 1, AnyTeamMay).Step);
        }
    }
}
