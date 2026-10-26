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

        // The Sonic Pulse's live cone as Sonic Pulse.prefab ships it; literal here, the prefab may be retuned freely.
        private const float PulseRange = 5f;
        private const float PulseAngle = 60f;

        private const int ArchPoints = 25;
        private static readonly float[] Bends = { 0f, 0.5f, 1f };

        [Test]
        public void TheArchsFootStaysInsideTheHitCone()
        {
            foreach (float bend in Bends)
                for (int arch = 0; arch < 2; arch++)
                    for (float t = 0f; t <= 0.5f; t += 0.01f)
                    {
                        float radius = AbilityVisualGeometry.ArcBandRadius(arch, t, 0.2f, PulseRange, 0.18f);
                        float height = AbilityVisualGeometry.ArchHeight(radius, PulseAngle, 0.8f, 2.2f);
                        for (int k = 0; k < ArchPoints; k++)
                        {
                            Vector3 p = AbilityVisualGeometry.ArchPoint(radius, PulseAngle, height, bend, k, ArchPoints);
                            Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, p, PulseRange, PulseAngle),
                                $"bend {bend} arch {arch} at {t:F2}s point {k} {p:F4} stands over ground the pulse does not hit");
                            Assert.GreaterOrEqual(p.y, 0f, "never under the floor");
                        }
                    }

            Vector3 wide = AbilityVisualGeometry.ArchPoint(PulseRange, PulseAngle + 4f, 1f, 1f, 0, ArchPoints);
            Assert.IsFalse(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, wide, PulseRange, PulseAngle),
                "control: an arch 2 degrees wider each side leaves the cone");
        }

        [Test]
        public void TheArchSpansTheConesWidthAtItsDistance()
        {
            float half = PulseAngle * 0.5f * Mathf.Deg2Rad;
            foreach (float bend in Bends)
                foreach (float radius in new[] { 1f, 2.5f, PulseRange })
                {
                    Vector3 left = AbilityVisualGeometry.ArchPoint(radius, PulseAngle, 1f, bend, 0, ArchPoints);
                    Vector3 right = AbilityVisualGeometry.ArchPoint(radius, PulseAngle, 1f, bend, ArchPoints - 1, ArchPoints);
                    Assert.AreEqual(0f, left.y, 1e-5f, "the feet stand on the floor");
                    Assert.AreEqual(0f, right.y, 1e-5f);
                    Assert.AreEqual(radius, new Vector2(left.x, left.z).magnitude, 0.002f, $"bend {bend}: the left foot at the arch's distance");
                    Assert.AreEqual(-PulseAngle * 0.5f, Mathf.Atan2(left.x, left.z) * Mathf.Rad2Deg, 0.05f, "on the cone's left edge");
                    Assert.AreEqual(PulseAngle * 0.5f, Mathf.Atan2(right.x, right.z) * Mathf.Rad2Deg, 0.05f, "and the right foot on its right edge");
                    float forward = radius * Mathf.Cos(half);
                    Assert.AreEqual(2f * forward * Mathf.Tan(half), Vector3.Distance(left, right), 0.003f,
                        "the span is the cone's width where the feet stand: 2 d tan(angle / 2)");
                }
        }

        [Test]
        public void TheArchStandsUprightFacingTheAim()
        {
            const float height = 1.7f;
            Vector3 crest = AbilityVisualGeometry.ArchPoint(PulseRange, PulseAngle, height, 1f, ArchPoints / 2, ArchPoints);
            Assert.AreEqual(0f, crest.x, 1e-4f, "the crest is straight ahead");
            Assert.AreEqual(height, crest.y, 1e-4f, "the crest is the arch's height");
            Assert.AreEqual(PulseRange, crest.z, 0.002f, "with the foot following the reach, the crest stands at the full reach");
            for (int k = 0; k < ArchPoints; k++)
            {
                Vector3 p = AbilityVisualGeometry.ArchPoint(PulseRange, PulseAngle, height, 1f, k, ArchPoints);
                Vector3 mirror = AbilityVisualGeometry.ArchPoint(PulseRange, PulseAngle, height, 1f, ArchPoints - 1 - k, ArchPoints);
                Assert.AreEqual(p.y, mirror.y, 1e-4f, "the same on both sides of the aim");
                Assert.AreEqual(-p.x, mirror.x, 1e-4f);
                Assert.LessOrEqual(p.y, height + 1e-4f, "no point above the crest");
                Assert.AreEqual(PulseRange, new Vector2(p.x, p.z).magnitude, 0.002f, "every point stands right above the reach: the arch is upright");
                if (k > 0 && k <= ArchPoints / 2)
                    Assert.Greater(p.y, AbilityVisualGeometry.ArchPoint(PulseRange, PulseAngle, height, 1f, k - 1, ArchPoints).y, "rising from the foot to the crest");
                Vector3 straight = AbilityVisualGeometry.ArchPoint(PulseRange, PulseAngle, height, 0f, k, ArchPoints);
                Assert.AreEqual(PulseRange * Mathf.Cos(PulseAngle * 0.5f * Mathf.Deg2Rad), straight.z, 0.002f,
                    "a straight arch stands in one upright plane across the aim");
            }
        }

        [Test]
        public void TheArchGrowsWithItsDistanceUpToItsMaxHeight()
        {
            float previous = -1f;
            for (float r = 0f; r <= PulseRange; r += 0.25f)
            {
                float h = AbilityVisualGeometry.ArchHeight(r, PulseAngle, 0.8f, 10f);
                Assert.AreEqual(0.8f * r * Mathf.Sin(PulseAngle * 0.5f * Mathf.Deg2Rad), h, 1e-4f, "Arch Height of half the span between the feet");
                Assert.Greater(h, previous, "taller the farther it travels");
                previous = h;
            }
            Assert.AreEqual(2.5f, AbilityVisualGeometry.ArchHeight(PulseRange, PulseAngle, 1f, 10f), 1e-4f, "1 makes a half circle");
            Assert.AreEqual(1.3f, AbilityVisualGeometry.ArchHeight(PulseRange, PulseAngle, 1f, 1.3f), 1e-5f, "never above Max Height");
            Assert.AreEqual(0f, AbilityVisualGeometry.ArchHeight(0f, PulseAngle, 1f, 1.3f), 1e-5f, "nothing at the caster");
        }

        [Test]
        public void TheArchFadesAsItReachesTheEnd()
        {
            Assert.AreEqual(1f, AbilityVisualGeometry.ArchStrength(0f, 0.2f, 0.25f, 0.4f), 1e-5f, "full strength as it leaves the caster");
            Assert.AreEqual(0.7f, AbilityVisualGeometry.ArchStrength(0.1f, 0.2f, 0.25f, 0.4f), 1e-4f, "fading on the way out");
            Assert.AreEqual(0.4f, AbilityVisualGeometry.ArchStrength(0.2f, 0.2f, 0.25f, 0.4f), 1e-4f, "End Strength as it reaches the end");
            Assert.AreEqual(0.2f, AbilityVisualGeometry.ArchStrength(0.325f, 0.2f, 0.25f, 0.4f), 1e-4f, "then gone over the fade");
            Assert.AreEqual(0f, AbilityVisualGeometry.ArchStrength(0.45f, 0.2f, 0.25f, 0.4f), 1e-5f);
            Assert.AreEqual(0f, AbilityVisualGeometry.ArchStrength(3f, 0.2f, 0.25f, 0.4f), 1e-5f);
            float previous = 2f;
            for (float t = 0f; t <= 0.5f; t += 0.01f)
            {
                float s = AbilityVisualGeometry.ArchStrength(t, 0.2f, 0.25f, 0.4f);
                Assert.LessOrEqual(s, previous + 1e-5f, "it only ever fades");
                previous = s;
            }
        }

        [Test]
        public void OuterBandEndsAtRange()
        {
            Assert.AreEqual(0f, AbilityVisualGeometry.ArcBandRadius(0, 0f, 0.2f, PulseRange, 0.18f), 1e-5f, "starts at the caster");
            Assert.AreEqual(PulseRange * 0.5f, AbilityVisualGeometry.ArcBandRadius(0, 0.1f, 0.2f, PulseRange, 0.18f), 1e-4f, "halfway at half the sweep");
            Assert.AreEqual(PulseRange, AbilityVisualGeometry.ArcBandRadius(0, 0.2f, 0.2f, PulseRange, 0.18f), 1e-5f, "the leading band ends exactly at the range");
            Assert.AreEqual(PulseRange, AbilityVisualGeometry.ArcBandRadius(0, 3f, 0.2f, PulseRange, 0.18f), 1e-5f, "and stays there while it fades");
            float previous = -1f;
            for (float t = 0f; t <= 0.4f; t += 0.01f)
            {
                float lead = AbilityVisualGeometry.ArcBandRadius(0, t, 0.2f, PulseRange, 0.18f);
                float second = AbilityVisualGeometry.ArcBandRadius(1, t, 0.2f, PulseRange, 0.18f);
                Assert.GreaterOrEqual(lead, previous, "the wave only moves outward");
                Assert.Less(second, lead + 1e-5f, "a later band trails the leading one");
                previous = lead;
            }
            Assert.AreEqual(0f, AbilityVisualGeometry.ArcBandRadius(1, 0.02f, 0.2f, PulseRange, 0.18f), 1e-5f, "a trailing band is not out yet at the start");
        }
    }
}
