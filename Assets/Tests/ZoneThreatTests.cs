using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class ZoneThreatTests
    {
        private const int Linger = 3000;
        private static int[] NeverSeen() => new int[ZoneThreat.MaxTeams];

        [Test]
        public void ANeutralZoneIsNeverUnderAttack()
        {
            Assert.IsFalse(ZoneThreat.IsUnderAttack(-1, 0b111, NeverSeen(), 0, 50000, Linger));
        }

        [Test]
        public void AnEnemyStandingInsideIsAnAttackEvenWithDefendersThere()
        {
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0b011, NeverSeen(), 0, 50000, Linger));
        }

        [Test]
        public void OnlyTheOwnersTeamInsideIsNotAnAttack()
        {
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0b001, NeverSeen(), 0, 50000, Linger));
        }

        [Test]
        public void AnEnemyWhoLeftJustUnderTheLingerAgoStillCounts()
        {
            int[] seen = { 0, 10000, 0 };
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 12900, Linger));
        }

        [Test]
        public void AnEnemyWhoLeftJustOverTheLingerAgoNoLongerCounts()
        {
            int[] seen = { 0, 10000, 0 };
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 13100, Linger));
        }

        [Test]
        public void TheOwnersOwnDepartureNeverCounts()
        {
            int[] seen = { 49900, 0, 0 };
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 50000, Linger));
        }

        [Test]
        public void ReadsTheRightZonesSliceOfTheSharedArray()
        {
            // Zone 1's stamps start at index 3: team 2 left zone 1 a second ago.
            int[] seen = { 0, 0, 0, 0, 0, 49000 };
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0, seen, 3, 50000, Linger));
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 50000, Linger));
        }

        [Test]
        public void WithoutAServerClockOnlyPlayersStandingInsideCount()
        {
            int[] seen = { 0, 5, 0 };
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 0, Linger));
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0b010, seen, 0, 0, Linger));
        }

        [Test]
        public void TheLingerWorksAcrossTheServerClockWrap()
        {
            int[] seen = { 0, int.MaxValue - 1000, 0 };
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0, seen, 0, int.MinValue + 1000, Linger));
        }

        [Test]
        public void AStampAheadOfThisClientsClockIsNotAnAttack()
        {
            // A stale stamp (another room, a badly skewed clock) must not hold the zone under attack until now catches up.
            int[] seen = { 0, 50000 + 60000, 0 };
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 50000, Linger));
        }

        [Test]
        public void AnAttackerWhoNowOwnsTheZoneIsNotAttackingIt()
        {
            int[] seen = { 0, 49000, 0 };
            Assert.IsFalse(ZoneThreat.IsUnderAttack(1, 0b010, seen, 0, 50000, Linger));
        }

        [Test]
        public void PresenceMasksCombineTeamsPerZoneAndIgnorePlayersOutsideZones()
        {
            var players = new List<(int team, int zone)> { (0, 2), (1, 2), (1, 5), (2, -1), (7, 3) };
            int[] masks = ZoneThreat.PresenceMasks(10, players);
            Assert.AreEqual(0b011, masks[2]);
            Assert.AreEqual(0b010, masks[5]);
            Assert.AreEqual(0, masks[3], "a team id outside 0..2 is ignored");
            Assert.AreEqual(0, masks[0]);
        }

        [Test]
        public void DeparturesStampOnlyTheTeamsThatLeft()
        {
            int[] before = new int[10]; before[2] = 0b011;
            int[] after = new int[10]; after[2] = 0b001;
            int[] seen = new int[30]; seen[2 * 3 + 0] = 111;

            Assert.IsTrue(ZoneThreat.StampDepartures(before, after, seen, 50000));
            Assert.AreEqual(50000, seen[2 * 3 + 1]);
            Assert.AreEqual(111, seen[2 * 3 + 0], "team 0 is still there, so its stamp is untouched");
            Assert.IsFalse(ZoneThreat.StampDepartures(after, after, seen, 60000));
        }
    }
}
