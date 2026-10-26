using NUnit.Framework;
using Overpower.Match;
using UnityEngine;

namespace Overpower.Tests
{
    public class CaptureRingGeometryTests
    {
        private static void AssertClose(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-3f), $"expected {expected:F4} but was {actual:F4}");

        [Test]
        public void PointOnRingStartsAtTheCamerasUpAndGoesClockwiseSeenFromAbove()
        {
            var centre = new Vector3(10f, 0.5f, 20f);
            // Camera yaw 0 looks toward +Z, so screen-up on the ground is +Z; clockwise from above turns +Z toward +X.
            AssertClose(new Vector3(10f, 0.5f, 25f), CaptureRingGeometry.PointOnRing(centre, 5f, 0f, 0f));
            AssertClose(new Vector3(15f, 0.5f, 20f), CaptureRingGeometry.PointOnRing(centre, 5f, 0f, 90f));
            // A camera turned 90° has +X at the top of the screen.
            AssertClose(new Vector3(15f, 0.5f, 20f), CaptureRingGeometry.PointOnRing(centre, 5f, 90f, 0f));
            AssertClose(new Vector3(10f, 0.5f, 15f), CaptureRingGeometry.PointOnRing(centre, 5f, 90f, 90f));
        }

        [Test]
        public void ArcPointCountFollowsTheFillAndNeverDrawsASinglePoint()
        {
            Assert.AreEqual(0, CaptureRingGeometry.ArcPointCount(0f, 96));
            Assert.AreEqual(2, CaptureRingGeometry.ArcPointCount(0.001f, 96));
            Assert.AreEqual(49, CaptureRingGeometry.ArcPointCount(0.5f, 96));
            Assert.AreEqual(97, CaptureRingGeometry.ArcPointCount(1f, 96), "a full band closes the circle");
            Assert.AreEqual(97, CaptureRingGeometry.ArcPointCount(1.5f, 96));
        }

        [Test]
        public void ArcStepSplitsTheCircleEvenly()
        {
            Assert.AreEqual(3.75f, CaptureRingGeometry.ArcStepDegrees(96), 1e-5f);
        }

        [Test]
        public void PulseStartsAtZeroAndPeaksHalfwayThroughAPulse()
        {
            Assert.AreEqual(0f, CaptureRingGeometry.Pulse01(0f, 2f), 1e-5f);
            Assert.AreEqual(1f, CaptureRingGeometry.Pulse01(0.25f, 2f), 1e-5f);
            Assert.AreEqual(0f, CaptureRingGeometry.Pulse01(0.5f, 2f), 1e-5f);
        }

        [Test]
        public void BlinkStartsFullyOnAndIsTheOppositeOfThePulse()
        {
            Assert.AreEqual(1f, CaptureRingGeometry.Blink01(0f, 0.7f), 1e-5f);
            Assert.AreEqual(1f - CaptureRingGeometry.Pulse01(0.3f, 0.7f), CaptureRingGeometry.Blink01(0.3f, 0.7f), 1e-5f);
        }
    }
}
