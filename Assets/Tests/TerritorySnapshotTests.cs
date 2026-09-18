using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class TerritorySnapshotTests
    {
        [Test]
        public void ANewSnapshotIsAllNeutral()
        {
            var s = new TerritorySnapshot(10);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(TerritoryMap.Neutral, s.OwnerOf(i));
                Assert.AreEqual(0, s.BountyPaidOnLastCapture(i));
            }
        }

        [Test]
        public void CaptureSetsOwnerAndHoldStart()
        {
            var s = new TerritorySnapshot(10).WithCapture(3, team: 1, nowMs: 5000, bountyPaid: 0);
            Assert.AreEqual(1, s.OwnerOf(3));
            Assert.AreEqual(5000, s.HeldSinceMs(3));
        }

        [Test]
        public void TransitionsDoNotMutateTheOriginal()
        {
            var a = new TerritorySnapshot(10);
            var b = a.WithCapture(3, 1, 5000, 0);
            Assert.AreEqual(TerritoryMap.Neutral, a.OwnerOf(3));
            Assert.AreEqual(1, b.OwnerOf(3));
        }

        [Test]
        public void NeutralisingRemembersWhoHeldItAndForHowLong()
        {
            var s = new TerritorySnapshot(10).WithCapture(3, 1, 5000, 0).WithNeutral(3, nowMs: 305000);
            Assert.AreEqual(TerritoryMap.Neutral, s.OwnerOf(3));
            Assert.AreEqual(1, s.LastOwnerOf(3));
            Assert.AreEqual(300000, s.LastHeldMs(3));
        }

        [Test]
        public void ResetNeutralisingWipesTheBountyHistoryInstead()
        {
            // Task 2.7 review: the Tier-3 reset takes a zone from nobody, so unlike an ordinary
            // WithNeutral, it must not leave anything for BountyRule.PayoutOnCapture to pay out on.
            var s = new TerritorySnapshot(10).WithCapture(3, 1, 5000, 0).WithNeutralReset(3, nowMs: 305000);
            Assert.AreEqual(TerritoryMap.Neutral, s.OwnerOf(3));
            Assert.AreEqual(TerritoryMap.Neutral, s.LastOwnerOf(3));
            Assert.AreEqual(0, s.LastHeldMs(3));
        }

        [Test]
        public void ANextCaptureAfterAResetNeutralisePaysNoBounty()
        {
            var s = new TerritorySnapshot(10).WithCapture(3, 1, 5000, 0).WithNeutralReset(3, nowMs: 305000);
            int pay = BountyRule.PayoutOnCapture(newOwner: 2, s.LastOwnerOf(3), s.LastHeldMs(3), bounty: 900, holdMs: 300000);
            Assert.AreEqual(0, pay);
        }

        [Test]
        public void HoldTimeSurvivesServerTimestampWrapAround()
        {
            // PhotonNetwork.ServerTimestamp is an int that wraps; unchecked subtraction still gives the gap.
            var s = new TerritorySnapshot(10).WithCapture(3, 1, int.MaxValue - 1000, 0)
                                             .WithNeutral(3, unchecked(int.MaxValue + 2000));
            Assert.AreEqual(3000, s.LastHeldMs(3)); // (MaxValue + 2000) - (MaxValue - 1000), wrapped
        }

        [Test]
        public void PropertiesRoundTrip()
        {
            var s = new TerritorySnapshot(10).WithCapture(6, 0, 100, 0).WithCapture(3, 2, 200, 900);
            var props = new Dictionary<object, object>();
            s.WriteTo(props);
            Assert.IsTrue(TerritorySnapshot.TryRead(props, 10, out TerritorySnapshot back));
            Assert.AreEqual(0, back.OwnerOf(6));
            Assert.AreEqual(2, back.OwnerOf(3));
            Assert.AreEqual(200, back.HeldSinceMs(3));
            Assert.AreEqual(900, back.BountyPaidOnLastCapture(3));
        }

        [Test]
        public void ReadingPropertiesWithoutTerritoryFails()
        {
            Assert.IsFalse(TerritorySnapshot.TryRead(new Dictionary<object, object>(), 10, out _));
        }

        [Test]
        public void ReadingAShorterArrayPadsWithNeutral()
        {
            // A room created before tower 9 existed must still load: missing zones are neutral.
            var s = new TerritorySnapshot(9).WithCapture(8, 2, 1, 0);
            var props = new Dictionary<object, object>();
            s.WriteTo(props);
            Assert.IsTrue(TerritorySnapshot.TryRead(props, 10, out TerritorySnapshot back));
            Assert.AreEqual(2, back.OwnerOf(8));
            Assert.AreEqual(TerritoryMap.Neutral, back.OwnerOf(9));
        }

        [Test]
        public void ChangedZonesListsOnlyOwnershipChanges()
        {
            var a = new TerritorySnapshot(10).WithCapture(6, 0, 1, 0);
            var b = a.WithCapture(0, 0, 2, 0).WithNeutral(6, 3);
            CollectionAssert.AreEquivalent(new[] { 0, 6 }, b.ZonesWhoseOwnerChangedSince(a));
        }

        [Test]
        public void OwnersAsDictionaryFeedsTheMap()
        {
            var s = new TerritorySnapshot(10).WithCapture(6, 0, 1, 0);
            IReadOnlyDictionary<int, int> owners = s.OwnersByZone();
            Assert.AreEqual(0, owners[6]);
            Assert.AreEqual(TerritoryMap.Neutral, owners[0]);
        }
    }
}
