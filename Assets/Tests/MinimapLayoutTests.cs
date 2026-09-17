using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class MinimapLayoutTests
    {
        private static readonly Vector2 Centre = new Vector2(65.05f, 53.34f); // ArenaSymmetry.centre (x, z)
        private const float WorldSize = 154f;
        private const float MapSize = 300f;

        private static void AssertClose(Vector2 expected, Vector2 actual, float tolerance = 1e-3f) =>
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(tolerance), $"expected {expected:F4} but was {actual:F4}");

        private static Vector3 World(float dx, float dz) => new Vector3(Centre.x + dx, 3f, Centre.y + dz);

        [Test]
        public void TheBakedCentreIsTheMiddleOfTheMap()
        {
            AssertClose(Vector2.zero, MinimapLayout.WorldToMap(World(0f, 0f), Centre, WorldSize, MapSize));
        }

        [Test]
        public void EastIsRightAndNorthIsUpBeforeTheMapTurns()
        {
            AssertClose(new Vector2(150f, 0f), MinimapLayout.WorldToMap(World(77f, 0f), Centre, WorldSize, MapSize));
            AssertClose(new Vector2(0f, 150f), MinimapLayout.WorldToMap(World(0f, 77f), Centre, WorldSize, MapSize));
        }

        [Test]
        public void OnlyPointsInsideTheBakedSquareCountAsInside()
        {
            Assert.IsTrue(MinimapLayout.IsInsideBakedArea(World(77f, -77f), Centre, WorldSize));
            Assert.IsFalse(MinimapLayout.IsInsideBakedArea(World(77.1f, 0f), Centre, WorldSize));
            Assert.IsFalse(MinimapLayout.IsInsideBakedArea(World(0f, -77.1f), Centre, WorldSize));
        }

        [Test]
        public void TurningPutsTheCamerasUpDirectionAtTheTop()
        {
            // A camera turned 90° looks toward +X: +X is at the top of the screen and +Z on the left.
            AssertClose(new Vector2(0f, 150f), MinimapLayout.TurnWithCamera(new Vector2(150f, 0f), 90f));
            AssertClose(new Vector2(-150f, 0f), MinimapLayout.TurnWithCamera(new Vector2(0f, 150f), 90f));
        }

        [Test]
        public void TurningMatchesWhatTheCameraShows()
        {
            const float yaw = 37f;
            Vector3 offset = new Vector3(12f, 0f, -31f);
            Vector3 screenRight = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.right;
            Vector3 screenUp = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
            float scale = MapSize / WorldSize;
            var expected = new Vector2(Vector3.Dot(offset, screenRight), Vector3.Dot(offset, screenUp)) * scale;

            Vector2 map = MinimapLayout.WorldToMap(World(offset.x, offset.z), Centre, WorldSize, MapSize);
            AssertClose(expected, MinimapLayout.TurnWithCamera(map, yaw));
        }

        [Test]
        public void EachTeamSeesItsOwnCapitalInTheSamePlace()
        {
            // Capitals sit 57.66 m from the centre at map angles 90/210/330 (arena symmetry). CameraTracking.ResolveTeamYaw
            // turns each team's camera to atan2(toSpawn) + Team Yaw Offset (120 in Game Scene).
            const float radius = 57.66f;
            var expected = new Vector2(-0.8660254f, -0.5f) * (radius * MapSize / WorldSize); // 120° counter-clockwise from up
            foreach (float mapAngle in new[] { 90f, 210f, 330f })
            {
                float rad = mapAngle * Mathf.Deg2Rad;
                Vector3 capital = World(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
                float yaw = Mathf.Atan2(capital.x - Centre.x, capital.z - Centre.y) * Mathf.Rad2Deg + 120f;
                Vector2 onScreen = MinimapLayout.TurnWithCamera(MinimapLayout.WorldToMap(capital, Centre, WorldSize, MapSize), yaw);
                AssertClose(expected, onScreen, 0.01f);
            }
        }

        [Test]
        public void LabelsAndRingsStayUprightWhateverTheYaw()
        {
            foreach (float yaw in new[] { 0f, 37f, 120f, 240f, -75f })
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, MinimapLayout.MapRotationDegrees(yaw) + MinimapLayout.UprightRotationDegrees(yaw)), 1e-3f);
        }

        [Test]
        public void YourMarkerPointsWhereYouFaceOnScreen()
        {
            const float cameraYaw = 37f;
            // Facing the camera's own direction points straight up.
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, MinimapLayout.MapRotationDegrees(cameraYaw) + MinimapLayout.FacingRotationDegrees(cameraYaw)), 1e-3f);
            // Facing 90° clockwise from it points right: a UI rotation of -90.
            Assert.AreEqual(-90f, Mathf.DeltaAngle(0f, MinimapLayout.MapRotationDegrees(cameraYaw) + MinimapLayout.FacingRotationDegrees(cameraYaw + 90f)), 1e-3f);
        }

        [Test]
        public void ASegmentHasItsMiddleLengthAndAngle()
        {
            var (centre, length, angle) = MinimapLayout.Segment(new Vector2(0f, 0f), new Vector2(0f, 10f));
            AssertClose(new Vector2(0f, 5f), centre);
            Assert.AreEqual(10f, length, 1e-4f);
            Assert.AreEqual(90f, angle, 1e-3f);
            Assert.AreEqual(180f, MinimapLayout.Segment(new Vector2(10f, 0f), Vector2.zero).AngleDegrees, 1e-3f);
        }

        [Test]
        public void AnArrowheadStopsShortOfTheZoneItPointsAt()
        {
            AssertClose(new Vector2(7f, 0f), MinimapLayout.PointBeforeEnd(Vector2.zero, new Vector2(10f, 0f), 3f));
            AssertClose(new Vector2(5f, 5f), MinimapLayout.PointBeforeEnd(new Vector2(5f, 5f), new Vector2(5f, 5f), 3f));
        }

        // Apex-up equilateral triangle: vertex directions at 90/210/330 degrees, matching the real capitals' map
        // angles (EachTeamSeesItsOwnCapitalInTheSamePlace above).
        private static readonly Vector2[] EquilateralDirections =
        {
            new Vector2(0f, 1f),
            new Vector2(-0.8660254f, -0.5f),
            new Vector2(0.8660254f, -0.5f),
        };

        [Test]
        public void AVertexDirectionPointNeedsRadiusEqualToItsDistance()
        {
            // A point exactly at vertex 0's own direction, distance D out, is the vertex itself: R must reach
            // exactly D with no margin (its reach against the two OTHER normals, -d1/-d2, is 0.5D each, so
            // worstReach = 0.5D and R = 2*0.5D = D). With a margin, the margin is added to the inradius (worstReach)
            // BEFORE doubling (review fix, 2026-09-17: R = 2 x (worstReach + margin), not 2 x worstReach + margin,
            // so every side of the triangle gets the full margin, not half of it) - R = 2*(0.5D + margin).
            const float d = 10f;
            var points = new List<Vector2> { new Vector2(0f, d) };
            Assert.AreEqual(d, MinimapLayout.TriangleCircumradius(points, EquilateralDirections, 0f), 1e-3f);
            Assert.AreEqual(d + 8f, MinimapLayout.TriangleCircumradius(points, EquilateralDirections, 4f), 1e-3f);
        }

        [Test]
        public void AnOppositePointNeedsRadiusTwiceItsDistance()
        {
            // A point straight OPPOSITE vertex 0 (along -d0), distance D out, touches the edge facing vertex 0 at
            // exactly its inradius: R/2 = D, so R = 2D.
            const float d = 10f;
            var points = new List<Vector2> { new Vector2(0f, -d) };
            Assert.AreEqual(2f * d, MinimapLayout.TriangleCircumradius(points, EquilateralDirections, 0f), 1e-3f);
        }

        [Test]
        public void TheWorstPointAcrossAllCornersAndNormalsForcesRPlusMargin()
        {
            // Two points: one reaches 5 (opposite vertex 0, along -d0); the other sits AT vertex 1's own direction,
            // distance 9 out, which only reaches 4.5 against -d1 (and -d2) - so the first point, not the second,
            // must win, even though the second is farther from the centre in a plain radius sense (9 vs 5), because
            // TriangleCircumradius measures per-normal reach, not overall distance. The margin is added to the
            // winning reach BEFORE doubling (see the rule's own comment).
            var points = new List<Vector2> { new Vector2(0f, -5f), new Vector2(0f, 9f) };
            Assert.AreEqual(16f, MinimapLayout.TriangleCircumradius(points, EquilateralDirections, 3f), 1e-3f);
        }

        [Test]
        public void TheGddLinksMakeTwelveLinesWithNoDuplicates()
        {
            // Tudor's 2026-09-17 redesign: the T3<->T4 link is gone, so the centre (9) links to every Tier 2 zone
            // only (this drops 3 of the previous 15 links: (3,9), (4,9), (5,9)).
            var map = new TerritoryMap(
                new List<(int, IEnumerable<int>)>
                {
                    (0, new[] { 6, 3, 5 }), (1, new[] { 7, 3, 4 }), (2, new[] { 8, 4, 5 }),
                    (3, new[] { 0, 1 }), (4, new[] { 1, 2 }), (5, new[] { 0, 2 }),
                    (6, new[] { 0 }), (7, new[] { 1 }), (8, new[] { 2 }),
                    (9, new[] { 0, 1, 2 }),
                },
                new List<(int, int)> { (6, 0), (7, 1), (8, 2) });

            List<(int A, int B)> pairs = MinimapLayout.LinkPairs(map, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });

            Assert.AreEqual(12, pairs.Count);
            CollectionAssert.Contains(pairs, (0, 9));
            CollectionAssert.Contains(pairs, (2, 9));
            CollectionAssert.DoesNotContain(pairs, (6, 9));
            CollectionAssert.DoesNotContain(pairs, (3, 9));
            foreach ((int a, int b) in pairs)
                Assert.Less(a, b);
        }

        [Test]
        public void TierLabelsAreRomanNumerals()
        {
            Assert.AreEqual("I", MinimapLayout.TierLabel(1));
            Assert.AreEqual("II", MinimapLayout.TierLabel(2));
            Assert.AreEqual("III", MinimapLayout.TierLabel(3));
            Assert.AreEqual("IV", MinimapLayout.TierLabel(4));
            Assert.AreEqual("5", MinimapLayout.TierLabel(5));
        }
    }
}
