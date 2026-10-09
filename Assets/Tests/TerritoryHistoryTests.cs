using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class TerritoryHistoryTests
    {
        private static TerritorySnapshot Snap() => new TerritorySnapshot(4);

        [Test]
        public void WithNothingSeenThereIsNoBefore()
        {
            Assert.IsNull(new TerritoryHistory().BeforeEventIn(Snap()));
        }

        [Test]
        public void TheFirstEventOfASnapshotSeesTheLastSnapshotSeen()
        {
            var history = new TerritoryHistory();
            TerritorySnapshot before = Snap(), after = Snap();
            history.Seen(before);
            Assert.AreSame(before, history.BeforeEventIn(after));
        }

        [Test]
        public void EveryZoneOfOneSnapshotSeesTheSameBefore()
        {
            var history = new TerritoryHistory();
            TerritorySnapshot before = Snap(), after = Snap();
            history.Seen(before);
            Assert.AreSame(before, history.BeforeEventIn(after));
            Assert.AreSame(before, history.BeforeEventIn(after));
            Assert.AreSame(before, history.BeforeEventIn(after));
        }

        [Test]
        public void TwoSnapshotsInOneFrameEachSeeTheOneBeforeThem()
        {
            var history = new TerritoryHistory();
            TerritorySnapshot a = Snap(), b = Snap(), c = Snap();
            history.Seen(a);
            Assert.AreSame(a, history.BeforeEventIn(b));
            Assert.AreSame(b, history.BeforeEventIn(c));
        }

        [Test]
        public void ASnapshotSeenBetweenEventsBecomesTheNextBefore()
        {
            var history = new TerritoryHistory();
            TerritorySnapshot a = Snap(), b = Snap(), c = Snap();
            history.Seen(a);
            history.BeforeEventIn(b);
            history.Seen(b);
            Assert.AreSame(b, history.BeforeEventIn(c));
        }
    }
}
