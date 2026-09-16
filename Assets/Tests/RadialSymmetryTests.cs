using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    public class RadialSymmetryTests
    {
        private static readonly Vector3 Centre = new Vector3(65.05f, 0f, 53.34f);

        private static void AssertClose(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-3f), $"expected {expected:F4} but was {actual:F4}");
        }

        [Test]
        public void OneThirdTurnMovesAPointCounterClockwiseSeenFromAbove()
        {
            // A point due +X of the centre (map angle 0°) ends at map angle 120°. Height is kept.
            Vector3 moved = RadialSymmetry.RotatePoint(Centre + new Vector3(10f, 2f, 0f), Centre, 1);
            AssertClose(Centre + new Vector3(-5f, 2f, 8.660254f), moved);
        }

        [Test]
        public void TheTopCapitalsAxisTurnsOntoTheBottomLeftThenTheBottomRight()
        {
            // Map angle 90° (the top capital) -> 210° (bottom-left, team 0) -> 330° (bottom-right, team 1).
            Vector3 top = Centre + new Vector3(0f, 0f, 57.66f);
            AssertClose(Centre + new Vector3(-49.935f, 0f, -28.83f), RadialSymmetry.RotatePoint(top, Centre, 1));
            AssertClose(Centre + new Vector3(49.935f, 0f, -28.83f), RadialSymmetry.RotatePoint(top, Centre, 2));
        }

        [Test]
        public void ThreeThirdTurnsReturnToTheStart()
        {
            Vector3 start = new Vector3(80f, 1f, 90f);
            AssertClose(start, RadialSymmetry.RotatePoint(start, Centre, 3));
        }

        [Test]
        public void AnObjectFacingAwayFromTheCentreStillFacesAwayAfterTheTurn()
        {
            // Facing +Z (map angle 90°) turns to face map angle 210°.
            Quaternion turned = RadialSymmetry.RotateRotation(Quaternion.identity, 1);
            AssertClose(new Vector3(-0.8660254f, 0f, -0.5f), turned * Vector3.forward);
        }

        [Test]
        public void MapAngleIsCounterClockwiseFromPlusXInDegrees()
        {
            Assert.AreEqual(0f, RadialSymmetry.MapAngleDegrees(Centre + Vector3.right, Centre), 1e-3f);
            Assert.AreEqual(90f, RadialSymmetry.MapAngleDegrees(Centre + Vector3.forward, Centre), 1e-3f);
            Assert.AreEqual(180f, RadialSymmetry.MapAngleDegrees(Centre + Vector3.left, Centre), 1e-3f);
            Assert.AreEqual(270f, RadialSymmetry.MapAngleDegrees(Centre + Vector3.back, Centre), 1e-3f);
        }

        [Test]
        public void ThirdIndexIncludesTheStartBorderAndExcludesTheEnd()
        {
            Vector3 At(float degrees) => Centre + new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0f, Mathf.Sin(degrees * Mathf.Deg2Rad)) * 20f;
            Assert.AreEqual(0, RadialSymmetry.ThirdIndex(At(25f), Centre, 25f));
            Assert.AreEqual(0, RadialSymmetry.ThirdIndex(At(144.9f), Centre, 25f));
            Assert.AreEqual(1, RadialSymmetry.ThirdIndex(At(145f), Centre, 25f));
            Assert.AreEqual(2, RadialSymmetry.ThirdIndex(At(265f), Centre, 25f));
            Assert.AreEqual(2, RadialSymmetry.ThirdIndex(At(24.9f), Centre, 25f));
        }
    }
}
