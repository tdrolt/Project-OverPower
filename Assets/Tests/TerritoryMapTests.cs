using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class TerritoryMapTests
    {
        // The scene's real adjacency (phase2-code-survey.md) plus the planned centre 9 -> {3,4,5}.
        private static TerritoryMap RealMap() => new TerritoryMap(
            new List<(int, IEnumerable<int>)>
            {
                (0, new[] { 6, 3, 5 }), (1, new[] { 7, 3, 4 }), (2, new[] { 8, 4, 5 }),
                (3, new[] { 0, 1 }), (4, new[] { 1, 2 }), (5, new[] { 0, 2 }),
                (6, new[] { 0 }), (7, new[] { 1 }), (8, new[] { 2 }),
                (9, new[] { 3, 4, 5 }),
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
        public void TheCentreNeedsAFlankingZone()
        {
            var owners = StartOwners();
            owners[0] = 0;
            Assert.IsFalse(RealMap().MayCapture(0, 9, owners));
            owners[3] = 0;
            Assert.IsTrue(RealMap().MayCapture(0, 9, owners));
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
    }
}
