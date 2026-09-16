using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class OverPowerStateTests
    {
        private static OverPowerState New() => new OverPowerState(windowSeconds: 3f, maxDistance: 15f, healthThreshold: 35f);

        [Test]
        public void TwoEnemyTeamsWithinTheWindowArmIt()
        {
            var s = New();
            s.RecordEnemyHit(attackerTeam: 1, time: 10f, distanceToOwnedZone: 2f);
            s.RecordEnemyHit(2, 12.5f, 2f);
            Assert.IsTrue(s.Armed);
        }

        [Test]
        public void OneTeamTwiceDoesNot()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 2f);
            s.RecordEnemyHit(1, 11f, 2f);
            Assert.IsFalse(s.Armed);
        }

        [Test]
        public void HitsFurtherApartThanTheWindowDoNot()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 2f);
            s.RecordEnemyHit(2, 13.1f, 2f);
            Assert.IsFalse(s.Armed);
        }

        [Test]
        public void HitsTakenFarFromYourTerritoryDoNotCount()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 16f);
            s.RecordEnemyHit(2, 11f, 2f);
            Assert.IsFalse(s.Armed);
        }

        [Test]
        public void DroppingBelowTheThresholdWhileArmedFiresOnce()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 2f);
            s.RecordEnemyHit(2, 11f, 2f);
            Assert.IsFalse(s.CheckTrigger(40f));
            Assert.IsTrue(s.CheckTrigger(34f));
            Assert.IsTrue(s.Active);
            Assert.IsFalse(s.CheckTrigger(20f));
        }

        [Test]
        public void MovingTooFarEndsItArmedOrActive()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 2f);
            s.RecordEnemyHit(2, 11f, 2f);
            s.CheckTrigger(30f);
            s.UpdateDistance(15.5f);
            Assert.IsFalse(s.Armed);
            Assert.IsFalse(s.Active);
        }

        [Test]
        public void NotArmedNeverTriggers()
        {
            Assert.IsFalse(New().CheckTrigger(1f));
        }
    }
}
