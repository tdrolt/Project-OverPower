using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class TerritoryMapTests
    {
        // The scene's real adjacency, including the GDD p.27 links (Tudor, 2026-09-16): the centre (9) links to every
        // Tier 2 zone as well as every Tier 3. TerritoryAdjacencySceneTests checks the saved scene matches this copy.
        private static TerritoryMap RealMap() => new TerritoryMap(
            new List<(int, IEnumerable<int>)>
            {
                (0, new[] { 6, 3, 5 }), (1, new[] { 7, 3, 4 }), (2, new[] { 8, 4, 5 }),
                (3, new[] { 0, 1 }), (4, new[] { 1, 2 }), (5, new[] { 0, 2 }),
                (6, new[] { 0 }), (7, new[] { 1 }), (8, new[] { 2 }),
                (9, new[] { 3, 4, 5, 0, 1, 2 }),
            },
            new List<(int, int)> { (6, 0), (7, 1), (8, 2) });

        private static Dictionary<int, int> StartOwners() =>
            new Dictionary<int, int> { { 6, 0 }, { 7, 1 }, { 8, 2 } };

        [Test]
        public void AZoneNextToOneYouOwnIsCapturable()
        {
            Assert.IsTrue(RealMap().MayCapture(0, 0, StartOwners()));
        }

        [Test]
        public void AZoneWithNothingOfYoursNextToItIsNot()
        {
            Assert.IsFalse(RealMap().MayCapture(0, 3, StartOwners()));
        }

        [Test]
        public void AZoneYouAlreadyOwnIsNotCapturable()
        {
            Assert.IsFalse(RealMap().MayCapture(0, 6, StartOwners()));
        }

        [Test]
        public void YourOwnCapitalIsAlwaysCapturableEvenWithNothingAdjacent()
        {
            var owners = new Dictionary<int, int> { { 6, 1 }, { 7, 1 }, { 8, 2 } };
            Assert.IsTrue(RealMap().MayCapture(0, 6, owners));
        }

        [Test]
        public void AnEnemyCapitalNeedsAnAdjacentZone()
        {
            Assert.IsFalse(RealMap().MayCapture(1, 6, StartOwners()));
            var owners = StartOwners();
            owners[0] = 1;
            Assert.IsTrue(RealMap().MayCapture(1, 6, owners));
        }

        [Test]
        public void TheCentreIsCapturableFromATier2Alone()
        {
            var owners = StartOwners();
            owners[0] = 0;
            Assert.IsTrue(RealMap().MayCapture(0, 9, owners));
        }

        [Test]
        public void TheCentreIsStillCapturableFromAFlankingZone()
        {
            var owners = StartOwners();
            owners[3] = 0;
            Assert.IsTrue(RealMap().MayCapture(0, 9, owners));
        }

        [Test]
        public void TheCentreStillNeedsAZoneOfYoursNextToIt()
        {
            // Team 0 owns only its capital 6, which isn't next to the centre.
            Assert.IsFalse(RealMap().MayCapture(0, 9, StartOwners()));
        }

        [Test]
        public void ACapitalStillLinksOnlyToItsOwnTier2()
        {
            TerritoryMap map = RealMap();
            CollectionAssert.AreEqual(new[] { 0 }, map.AdjacentTo(6));
            CollectionAssert.AreEqual(new[] { 1 }, map.AdjacentTo(7));
            CollectionAssert.AreEqual(new[] { 2 }, map.AdjacentTo(8));
        }

        [Test]
        public void ATier2UnderAttackIsNotAWayIntoTheCentre()
        {
            // The capital-under-attack link block (2026-09-16) applies to the new T2 -> T4 link too.
            var owners = StartOwners();
            owners[0] = 0;
            Assert.IsFalse(RealMap().MayCapture(0, 9, owners, zone => zone == 0));
            owners[3] = 0;
            Assert.IsTrue(RealMap().MayCapture(0, 9, owners, zone => zone == 0), "a second, safe link still works");
        }

        [Test]
        public void AdjacencyWorksInBothDirectionsEvenIfListedOnce()
        {
            var map = new TerritoryMap(
                new List<(int, IEnumerable<int>)> { (3, new int[0]), (9, new[] { 3 }) },
                new List<(int, int)>());
            CollectionAssert.Contains(map.AdjacentTo(3), 9);
        }

        [Test]
        public void AnUnknownZoneOrNoTeamIsNeverCapturable()
        {
            Assert.IsFalse(RealMap().MayCapture(0, 42, StartOwners()));
            Assert.IsFalse(RealMap().MayCapture(-1, 0, StartOwners()));
        }

        [Test]
        public void CapitalOfFindsTheTeamsCapitalZone()
        {
            Assert.AreEqual(7, RealMap().CapitalOf(1));
            Assert.AreEqual(TerritoryMap.Neutral, RealMap().CapitalOf(5));
        }

        [Test]
        public void AnOwnedNeighbourUnderAttackIsNotALink()
        {
            // Team 0 owns only its capital 6; 6 is under attack, so T2 zone 0 can't be captured through it.
            Assert.IsFalse(RealMap().MayCapture(0, 0, StartOwners(), zone => zone == 6));
        }

        [Test]
        public void ASecondSafeOwnedNeighbourStillAllowsTheCapture()
        {
            var owners = new Dictionary<int, int> { { 6, 0 }, { 3, 0 }, { 7, 1 }, { 8, 2 } };
            Assert.IsTrue(RealMap().MayCapture(0, 0, owners, zone => zone == 6));
        }

        [Test]
        public void YourOwnCapitalStaysCapturableWhateverIsUnderAttack()
        {
            var owners = new Dictionary<int, int> { { 6, 1 }, { 7, 1 }, { 8, 2 } };
            Assert.IsTrue(RealMap().MayCapture(0, 6, owners, zone => true));
        }

        [Test]
        public void NoUnderAttackCheckBehavesLikeTheOriginalRule()
        {
            Assert.IsTrue(RealMap().MayCapture(0, 0, StartOwners(), null));
            Assert.IsFalse(RealMap().MayCapture(0, 3, StartOwners(), null));
        }
    }
}
