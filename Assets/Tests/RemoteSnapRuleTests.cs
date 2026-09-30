using NUnit.Framework;
using Overpower.Net;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// When a remote copy jumps instead of gliding (movement step 5). The numbers are this game's: Remote Snap
    /// Distance 3 m and 20 updates a second, so one send interval is 50 ms.
    /// </summary>
    public class RemoteSnapRuleTests
    {
        private const float Snap = 3f;
        private const float Interval = 50f;
        private static readonly Vector3 Somewhere = new Vector3(65f, 0.5f, 53f);

        private static bool Snaps(Vector3 previous, int previousMs, Vector3 next, int nextMs) =>
            RemoteSnapRule.ShouldSnap(true, previous, previousMs, next, nextMs, Snap, Interval);

        [Test]
        public void TheFirstUpdateAlwaysSnaps() =>
            Assert.IsTrue(RemoteSnapRule.ShouldSnap(false, Vector3.zero, 0, Somewhere, 1000, Snap, Interval));

        [Test]
        public void OrdinaryMovementGlides()
        {
            // The fastest ordinary movement is a zip pull: 25 m/s is 1.25 m per update.
            Assert.IsFalse(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 1.25f, 1050));
            Assert.IsFalse(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 3f, 1050));
        }

        [Test]
        public void ABlinkSnaps() =>
            Assert.IsTrue(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 9f, 1050));

        [Test]
        public void JustOverTheDistanceSnaps() =>
            Assert.IsTrue(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 3.01f, 1050));

        [Test]
        public void AZipPullWithLostUpdatesStillGlides()
        {
            // Two updates lost: 0.15 s of a 25 m/s pull is 3.75 m, which must not read as a teleport.
            Assert.IsFalse(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 3.75f, 1150));
        }

        [Test]
        public void ABlinkAfterOneLostUpdateStillSnaps() =>
            Assert.IsTrue(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 9f, 1100));

        [Test]
        public void AStallCountsAtMostFourIntervals()
        {
            Assert.AreEqual(4, RemoteSnapRule.ElapsedIntervals(0, 1000, Interval));
            Assert.IsTrue(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 13f, 2000));
            Assert.IsFalse(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 11f, 2000));
        }

        [Test]
        public void JitteredIntervalsRoundToWholeUpdates()
        {
            Assert.AreEqual(1, RemoteSnapRule.ElapsedIntervals(0, 70, Interval));
            Assert.AreEqual(2, RemoteSnapRule.ElapsedIntervals(0, 80, Interval));
        }

        [Test]
        public void AStampThatWrappedOrWentBackwardsCountsOneInterval()
        {
            // ServerTimestamp wraps about every 49.7 days, the same reason DeployableAge subtracts unchecked.
            Assert.AreEqual(1, RemoteSnapRule.ElapsedIntervals(int.MaxValue - 20, int.MinValue + 29, Interval));
            Assert.AreEqual(1, RemoteSnapRule.ElapsedIntervals(1000, 900, Interval));
            Assert.AreEqual(1, RemoteSnapRule.ElapsedIntervals(1000, 1000, Interval));
        }
    }
}
