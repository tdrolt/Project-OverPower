using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    public class AbilityVisualGeometryTests
    {
        // Flamethrower.prefab's Cone Range and Cone Angle (pinned by AbilityVisualPrefabGuardTests).
        private const float Range = 7f;
        private const float Angle = 45f;
        private const int Slices = 24;

        private static void AssertClose(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), $"expected {expected:F4} but was {actual:F4}");

        [Test]
        public void CirclePointStartsAtPlusZAndTurnsClockwiseSeenFromAbove()
        {
            AssertClose(new Vector3(0f, 0f, 2f), AbilityVisualGeometry.CirclePoint(2f, 0f));
            AssertClose(new Vector3(2f, 0f, 0f), AbilityVisualGeometry.CirclePoint(2f, 90f));
            AssertClose(new Vector3(0f, 0f, -2f), AbilityVisualGeometry.CirclePoint(2f, 180f));
        }

        [Test]
        public void TheFanStartsAtTheCasterAndItsArcRunsEdgeToEdgeAtFullRange()
        {
            Assert.AreEqual(Slices + 2, AbilityVisualGeometry.FanVertexCount(Slices));
            AssertClose(Vector3.zero, AbilityVisualGeometry.FanVertex(0, Range, Angle, Slices));
            Vector3 left = AbilityVisualGeometry.FanVertex(1, Range, Angle, Slices);
            Vector3 right = AbilityVisualGeometry.FanVertex(Slices + 1, Range, Angle, Slices);
            Assert.AreEqual(Range, left.magnitude, 1e-4f);
            Assert.AreEqual(Range, right.magnitude, 1e-4f);
            Assert.AreEqual(Angle * 0.5f, Vector3.Angle(Vector3.forward, left), 1e-3f);
            Assert.AreEqual(Angle * 0.5f, Vector3.Angle(Vector3.forward, right), 1e-3f);
            Assert.Less(left.x, 0f, "the first arc point is the left edge");
            Assert.Greater(right.x, 0f, "the last arc point is the right edge");
        }

        [Test]
        public void EveryFanPointIsInsideTheFlamethrowersRealHitCone()
        {
            for (int i = 0; i < AbilityVisualGeometry.FanVertexCount(Slices); i++)
            {
                Vector3 p = AbilityVisualGeometry.FanVertex(i, Range, Angle, Slices);
                Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, p, Range + 1e-3f, Angle + 1e-2f), $"vertex {i} {p:F4}");
            }
            Assert.IsFalse(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, AbilityVisualGeometry.CirclePoint(Range, Angle * 0.5f + 1f), Range, Angle),
                "control: a point 1 degree past the edge is outside");
        }

        [Test]
        public void TheFansStraightArcPiecesStayWithinACentimetreOfTheRealArc()
        {
            for (int i = 1; i <= Slices; i++)
            {
                Vector3 mid = (AbilityVisualGeometry.FanVertex(i, Range, Angle, Slices) + AbilityVisualGeometry.FanVertex(i + 1, Range, Angle, Slices)) * 0.5f;
                Assert.Greater(mid.magnitude, Range - 0.01f, $"slice {i}");
            }
        }

        [Test]
        public void FanTrianglesAllShareTheApexAndFaceUp()
        {
            var triangles = new int[Slices * 3];
            AbilityVisualGeometry.FillFanTriangles(Slices, triangles);
            for (int t = 0; t < Slices; t++)
            {
                Assert.AreEqual(0, triangles[t * 3]);
                Vector3 a = AbilityVisualGeometry.FanVertex(triangles[t * 3], Range, Angle, Slices);
                Vector3 b = AbilityVisualGeometry.FanVertex(triangles[t * 3 + 1], Range, Angle, Slices);
                Vector3 c = AbilityVisualGeometry.FanVertex(triangles[t * 3 + 2], Range, Angle, Slices);
                Assert.Greater(Vector3.Cross(b - a, c - a).y, 0f, $"triangle {t} faces up");
            }
        }

        [Test]
        public void CagePostsAreNeverFurtherApartThanTheSpacing()
        {
            // Electric Fence.prefab radius 6: 37.7 m around / 2.5 m = 15.08 -> 16 posts.
            Assert.AreEqual(16, AbilityVisualGeometry.CagePostCount(6f, 2.5f));
            Assert.AreEqual(3, AbilityVisualGeometry.CagePostCount(0.2f, 2.5f), "never fewer than 3");
            float gap = Vector3.Distance(AbilityVisualGeometry.CirclePoint(6f, 0f), AbilityVisualGeometry.CirclePoint(6f, 360f / 16));
            Assert.LessOrEqual(gap, 2.5f);
        }

        [Test]
        public void ASphereCutsAFlatPlaneInACircle()
        {
            Assert.AreEqual(3f, AbilityVisualGeometry.RadiusOnPlane(3f, 1f, 1f), 1e-5f);
            Assert.AreEqual(4f, AbilityVisualGeometry.RadiusOnPlane(5f, 3f, 0f), 1e-5f);
            Assert.AreEqual(4f, AbilityVisualGeometry.RadiusOnPlane(5f, 0f, 3f), 1e-5f, "above or below is the same");
            Assert.AreEqual(0f, AbilityVisualGeometry.RadiusOnPlane(3f, 5f, 1f), "a plane that misses the sphere");
        }

        [Test]
        public void ThePlayersRootSitsHalfAMetreAboveItsFeet()
        {
            // Multiplayer Player.prefab CapsuleCollider: centre y 0.8063041, height 2.6126082 (TestRangeSpawner.Grounded, measured 2026-09-13).
            Assert.AreEqual(0.5f, AbilityVisualGeometry.RootAboveFeet(0.8063041f, 2.6126082f, 1f), 1e-3f);
            Assert.AreEqual(1f, AbilityVisualGeometry.RootAboveFeet(0.8063041f, 2.6126082f, 2f), 2e-3f);
        }

        [Test]
        public void APullTakesDistanceOverSpeed()
        {
            Assert.AreEqual(0.6f, AbilityVisualGeometry.PullSeconds(15f, 25f), 1e-5f);
            Assert.AreEqual(0f, AbilityVisualGeometry.PullSeconds(0f, 25f));
            Assert.AreEqual(0f, AbilityVisualGeometry.PullSeconds(5f, 0f));
        }

        [Test]
        public void AFadeRunsFromOneToZero()
        {
            Assert.AreEqual(1f, AbilityVisualGeometry.Fade01(0f, 0.5f), 1e-5f);
            Assert.AreEqual(0.5f, AbilityVisualGeometry.Fade01(0.25f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, AbilityVisualGeometry.Fade01(0.7f, 0.5f));
            Assert.AreEqual(0f, AbilityVisualGeometry.Fade01(0f, 0f));
        }

        // ---- A3 (Tudor 2026-09-17 evening): the flamethrower becomes a soft cone whose per-vertex colour/alpha
        // fades along the range (warm at the tip, clear at the far edge) and toward the side edges. Step 5 builds
        // that mesh; this pure kit exposes the two fractions its vertex-colour pass needs, on the SAME fan topology
        // FanVertex already describes - no second mesh shape, just two more readings of the same vertex.

        [Test]
        public void FanVertexRangeFractionIsZeroAtTheTipAndOneOnTheArcWithTheDefaultSingleRing()
        {
            Assert.AreEqual(0f, AbilityVisualGeometry.FanVertexRangeFraction(0, Slices));
            Assert.AreEqual(1f, AbilityVisualGeometry.FanVertexRangeFraction(1, Slices));
            Assert.AreEqual(1f, AbilityVisualGeometry.FanVertexRangeFraction(Slices + 1, Slices));
        }

        [Test]
        public void FanVertexSideFractionIsZeroAtCentreAndOneAtEitherEdge()
        {
            Assert.AreEqual(0f, AbilityVisualGeometry.FanVertexSideFraction(0, Slices), "the tip is centred");
            Assert.AreEqual(1f, AbilityVisualGeometry.FanVertexSideFraction(1, Slices), 1e-5f, "left edge");
            Assert.AreEqual(1f, AbilityVisualGeometry.FanVertexSideFraction(Slices + 1, Slices), 1e-5f, "right edge");
            int middle = 1 + Slices / 2;
            Assert.AreEqual(0f, AbilityVisualGeometry.FanVertexSideFraction(middle, Slices), 1e-5f, "the centre arc point");
        }

        // ---- Multi-ring fan (review finding, 2026-09-18): with the pre-existing default of 1 ring, a soft cone's
        // radial fade had no vertex to hold a middle value at all - FanVertexRangeFraction was a step function, 0 at
        // the tip and 1 EVERYWHERE else, whatever a caller's own fade maths tried to do with it. rings > 1 makes it
        // a real ramp. rings = 1 (every test above) must keep behaving exactly as before this parameter existed.

        [Test]
        public void MoreRingsAddPointsBetweenTheTipAndTheOuterArcWithoutMovingTheOuterArc()
        {
            const int Rings = 3;
            Assert.AreEqual(1 + Rings * (Slices + 1), AbilityVisualGeometry.FanVertexCount(Slices, Rings));
            Assert.AreEqual(AbilityVisualGeometry.FanVertexCount(Slices), AbilityVisualGeometry.FanVertexCount(Slices, 1),
                "the default (no rings argument) must match rings = 1 exactly - this parameter is additive, not a behaviour change for existing callers");

            // Ring 1 (nearest the tip) sits at range/3; ring 3 (outermost) sits at the full range, same as the
            // single-ring fan's own arc.
            Vector3 ring1Left = AbilityVisualGeometry.FanVertex(1, Range, Angle, Slices, Rings);
            Vector3 ring3Left = AbilityVisualGeometry.FanVertex(1 + 2 * (Slices + 1), Range, Angle, Slices, Rings);
            AssertClose(AbilityVisualGeometry.FanVertex(1, Range, Angle, Slices), ring3Left);
            Assert.AreEqual(Range / 3f, ring1Left.magnitude, 1e-4f);
            Assert.AreEqual(Angle * 0.5f, Vector3.Angle(Vector3.forward, ring1Left), 1e-3f, "same angular sweep as ring 3");
        }

        [Test]
        public void RingFanTrianglesCoverTipFanPlusAQuadStripPerRingGapAndAllFaceUp()
        {
            const int Rings = 3;
            var triangles = new int[Slices * (2 * Rings - 1) * 3];
            AbilityVisualGeometry.FillFanTriangles(Slices, triangles, Rings);
            for (int t = 0; t < triangles.Length / 3; t++)
            {
                Vector3 a = AbilityVisualGeometry.FanVertex(triangles[t * 3], Range, Angle, Slices, Rings);
                Vector3 b = AbilityVisualGeometry.FanVertex(triangles[t * 3 + 1], Range, Angle, Slices, Rings);
                Vector3 c = AbilityVisualGeometry.FanVertex(triangles[t * 3 + 2], Range, Angle, Slices, Rings);
                Assert.Greater(Vector3.Cross(b - a, c - a).y, 0f, $"triangle {t} faces up");
            }
        }

        [Test]
        public void FanVertexRangeFractionIsARealRampAcrossEveryRing()
        {
            const int Rings = 4;
            Assert.AreEqual(0f, AbilityVisualGeometry.FanVertexRangeFraction(0, Slices, Rings), "the tip");
            for (int ring = 1; ring <= Rings; ring++)
            {
                int index = 1 + (ring - 1) * (Slices + 1); // that ring's own left-edge vertex
                Assert.AreEqual(ring / (float)Rings, AbilityVisualGeometry.FanVertexRangeFraction(index, Slices, Rings), 1e-5f, $"ring {ring}");
            }
        }

        [Test]
        public void FanVertexSideFractionIsTheSameShapeOnEveryRing()
        {
            const int Rings = 3;
            for (int ring = 0; ring < Rings; ring++)
            {
                int leftEdge = 1 + ring * (Slices + 1);
                int rightEdge = leftEdge + Slices;
                int centre = leftEdge + Slices / 2;
                Assert.AreEqual(1f, AbilityVisualGeometry.FanVertexSideFraction(leftEdge, Slices), 1e-5f, $"ring {ring + 1} left edge");
                Assert.AreEqual(1f, AbilityVisualGeometry.FanVertexSideFraction(rightEdge, Slices), 1e-5f, $"ring {ring + 1} right edge");
                Assert.AreEqual(0f, AbilityVisualGeometry.FanVertexSideFraction(centre, Slices), 1e-5f, $"ring {ring + 1} centre");
            }
        }
    }
}
