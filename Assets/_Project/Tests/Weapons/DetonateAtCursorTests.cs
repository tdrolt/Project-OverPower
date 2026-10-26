using NUnit.Framework;
using Overpower.Weapons;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// DetonateAtCursor.ClampedDistanceToTarget is the one home for "how far does a cursor rocket
    /// travel before it detonates" - the rocket's own DistanceToCursorPoint and AimConeView's aim
    /// lines for this weapon (Task 7) both call it, so the two can never disagree about how far a
    /// cursor rocket actually reaches. Pinned here (Playtest polish review, fix 11) so a future
    /// change to the formula cannot quietly drift from what the aim lines promise.
    /// </summary>
    public class DetonateAtCursorTests
    {
        [Test]
        public void HeightDifferenceIsIgnored()
        {
            // The cursor point is resolved on the ground plane while the rocket leaves the muzzle
            // well above it - a straight-line distance would read long and the rocket would drift
            // past the mark, so the target's Y is flattened onto the origin's before measuring
            // (see the method's own comment).
            Vector3 origin = new Vector3(0f, 1.5f, 0f);
            Vector3 targetPoint = new Vector3(3f, 0f, 0f); // 3m away on the ground, well below the muzzle.

            float distance = DetonateAtCursor.ClampedDistanceToTarget(origin, targetPoint, maxRange: 50f);

            Assert.AreEqual(3f, distance, 1e-4f);
        }

        [Test]
        public void DistanceIsClampedToMaxRange()
        {
            Vector3 origin = Vector3.zero;
            Vector3 targetPoint = new Vector3(100f, 0f, 0f); // Far past the weapon's own range.

            float distance = DetonateAtCursor.ClampedDistanceToTarget(origin, targetPoint, maxRange: 30f);

            Assert.AreEqual(30f, distance, 1e-4f);
        }

        [Test]
        public void WithinRangeReturnsTheRealDistanceUnclamped()
        {
            Vector3 origin = Vector3.zero;
            Vector3 targetPoint = new Vector3(3f, 0f, 4f); // 3-4-5 triangle - real distance 5, well under range.

            float distance = DetonateAtCursor.ClampedDistanceToTarget(origin, targetPoint, maxRange: 30f);

            Assert.AreEqual(5f, distance, 1e-4f);
        }

        [Test]
        public void ZeroDistanceWhenTheCursorIsAtTheOrigin()
        {
            Vector3 origin = new Vector3(5f, 2f, -3f);

            float distance = DetonateAtCursor.ClampedDistanceToTarget(origin, origin, maxRange: 30f);

            Assert.AreEqual(0f, distance, 1e-4f);
        }
    }
}
