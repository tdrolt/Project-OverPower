using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Arena;

namespace Overpower.Tests
{
    /// <summary>Pins ArenaWallPlan.ForSource: one wall per outline edge; a right-angle corner closed by the longer wall
    /// alone (the shorter reaches only the 1 cm hairline into it, as the arena shader shows any overlap); any other outward
    /// corner filled by thickness * tan(turn / 2); any other reflex corner left unextended; a straight run given the
    /// hairline. The hand-built hexagon (0,0) (4,0) (4,5) (4,8) (2,11.4641) (5,11.4641) has POSITIVE signed area, so
    /// "outward" is what CornerExtension calls a positive (CCW) turn, whichever way an outline is listed.</summary>
    public class ArenaWallPlanTests
    {
        private const float Thickness = 0.724f;

        // P0..P5: a right turn (90 deg, at P1), a straight pass-through (0 deg, at P2), a mild turn (30 deg, at P3)
        // and a reflex turn (-120 deg, at P4). P5's own corner (closing back to P0) is left unasserted.
        private static readonly Vector2 P0 = new Vector2(0f, 0f);
        private static readonly Vector2 P1 = new Vector2(4f, 0f);
        private static readonly Vector2 P2 = new Vector2(4f, 5f);
        private static readonly Vector2 P3 = new Vector2(4f, 8f);
        private static readonly Vector2 P4 = new Vector2(2f, 11.4641016f);
        private static readonly Vector2 P5 = new Vector2(5f, 11.4641016f);

        private static List<Vector2> Hexagon() => new List<Vector2> { P0, P1, P2, P3, P4, P5 };

        private static Vector2[] FootprintOf(ArenaWallPlan.Run run) =>
            ArenaPieceShapes.FootprintCorners(run.Centre(Thickness), run.UnityYawDegrees, new Vector2(run.Length, Thickness));

        private static List<BoxFootprint> FootprintsOf(List<ArenaWallPlan.Run> runs)
        {
            var footprints = new List<BoxFootprint>();
            foreach (ArenaWallPlan.Run run in runs)
                footprints.Add(run.Footprint(Thickness));
            return footprints;
        }

        private const float Hairline = ArenaWallPlan.StraightOverlapMetres;

        [Test]
        public void EachRightAngleCornerIsClosedByTheLongerWallAloneAndTheWallsMeetFaceToFace()
        {
            // A plain rectangle: four 90 degree outward corners, the 10 m walls longer than the 6 m ones.
            var rectangle = new List<Vector2> { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 6f), new Vector2(0f, 6f) };

            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(rectangle, rectangle.Count, Thickness);

            Assert.AreEqual(4, runs.Count, "one run per edge");
            for (int i = 0; i < runs.Count; i++)
            {
                Vector2 a = rectangle[i], b = rectangle[(i + 1) % 4];
                float expectedRunOn = Vector2.Distance(a, b) > 8f ? Thickness : Hairline;
                Vector2 dir = (b - a).normalized;
                Assert.That(Vector2.Distance(runs[i].InnerStart, a - dir * expectedRunOn), Is.LessThan(1e-4f), $"run {i}'s start");
                Assert.That(Vector2.Distance(runs[i].InnerEnd, b + dir * expectedRunOn), Is.LessThan(1e-4f), $"run {i}'s end");

                ArenaWallPlan.Run next = runs[(i + 1) % runs.Count];
                Assert.That(ArenaPieceShapes.OverlapDepth(FootprintOf(runs[i]), FootprintOf(next)), Is.LessThan(Hairline + 1e-3f),
                    $"run {i} and run {(i + 1) % runs.Count} should meet face to face, not share space");
            }

            List<string> holes = ArenaWallCoverage.FindHoles(ArenaBounds.FromPolygon(rectangle), FootprintsOf(runs), Thickness);
            Assert.IsEmpty(holes, "the corners must still be closed:\n" + string.Join("\n", holes));
        }

        [Test]
        public void AReflexRightAngleCornerKeepsTheLongerWallAndCutsTheShorterBackBehindIt()
        {
            // An L: the reflex corner at (4, 3) joins a 6 m wall (arriving) and a 7 m wall (leaving).
            var outline = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 3f), new Vector2(4f, 3f), new Vector2(4f, 10f), new Vector2(0f, 10f),
            };
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(outline, outline.Count, Thickness);

            Vector2 corner = new Vector2(4f, 3f);
            Assert.That(Vector2.Distance(runs[2].InnerEnd, corner + new Vector2(1f, 0f) * (Thickness - Hairline)), Is.LessThan(1e-4f),
                "the shorter wall stops one thickness before the corner, where the longer wall's end face takes over");
            Assert.That(Vector2.Distance(runs[3].InnerStart, corner), Is.LessThan(1e-4f), "the longer wall runs to the corner");

            for (int i = 0; i < runs.Count; i++)
                for (int j = i + 1; j < runs.Count; j++)
                    Assert.That(ArenaPieceShapes.OverlapDepth(FootprintOf(runs[i]), FootprintOf(runs[j])), Is.LessThan(Hairline + 1e-3f),
                        $"run {i} and run {j} share space");

            List<string> holes = ArenaWallCoverage.FindHoles(ArenaBounds.FromPolygon(outline), FootprintsOf(runs), Thickness);
            Assert.IsEmpty(holes, "the corners must still be closed:\n" + string.Join("\n", holes));
        }

        [Test]
        public void AWallTooShortToLoseAWholeThicknessAtBothEndsKeepsAStubAndTheCornersStayClosed()
        {
            // A 1 m wide, 2 m deep step into the play space: its 1 m face meets the two longer sides at reflex right angles.
            var outline = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 2f), new Vector2(5f, 2f), new Vector2(5f, 0f),
                new Vector2(10f, 0f), new Vector2(10f, 10f), new Vector2(0f, 10f),
            };
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(outline, outline.Count, Thickness);

            ArenaWallPlan.Run face = runs[2];
            Assert.That(face.Length, Is.GreaterThan(0f), "the face's wall is cut back from both ends but never turned inside out");
            Assert.That(Vector2.Dot(face.InnerEnd - face.InnerStart, new Vector2(1f, 0f)), Is.GreaterThan(0f), "it still runs the face's way");
            Assert.That(Vector2.Distance((face.InnerStart + face.InnerEnd) * 0.5f, new Vector2(4.5f, 2f)), Is.LessThan(1e-4f),
                "it is cut back equally from both ends, each by half the face");

            List<string> holes = ArenaWallCoverage.FindHoles(ArenaBounds.FromPolygon(outline), FootprintsOf(runs), Thickness);
            Assert.IsEmpty(holes, "the two sides cover the face where its own wall was cut back:\n" + string.Join("\n", holes));
        }

        [Test]
        public void OnlyOutwardCornersRunOnAndByJustEnoughToFillTheNotch()
        {
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(Hexagon(), 6, Thickness);
            Assert.AreEqual(6, runs.Count);

            float thirty = Thickness * Mathf.Tan(15f * Mathf.Deg2Rad);   // turn/2 = 15 deg

            // P1 is a 90 degree outward corner between Edge0 (4 m) and Edge1 (5 m): only the longer Edge1 runs on, by one
            // thickness, and Edge0 stops at the corner against its inner face.
            Assert.That(Vector2.Distance(runs[0].InnerEnd, P1 + new Vector2(1f, 0f) * Hairline), Is.LessThan(1e-4f),
                "the shorter wall stops at a right-angle corner, but for the hairline");
            Assert.That(Vector2.Distance(runs[1].InnerStart, P1 - new Vector2(0f, 1f) * Thickness), Is.LessThan(1e-3f),
                "the longer wall runs on past a right-angle corner by one thickness");

            // Edge1 ends at P2, a straight pass-through (0 degrees): a hairline 1 cm overlap, not zero.
            Assert.That(Vector2.Distance(runs[1].InnerEnd, P2 + new Vector2(0f, 1f) * ArenaWallPlan.StraightOverlapMetres), Is.LessThan(1e-4f));
            Assert.That(Vector2.Distance(runs[2].InnerStart, P2 - new Vector2(0f, 1f) * ArenaWallPlan.StraightOverlapMetres), Is.LessThan(1e-4f));

            // Edge2 ends at P3, a mild 30 degree outward corner.
            Vector2 dir2 = new Vector2(0f, 1f);
            Assert.That(Vector2.Distance(runs[2].InnerEnd, P3 + dir2 * thirty), Is.LessThan(1e-3f),
                "a 30 degree corner should extend the arriving wall by thickness * tan(15 deg)");
            Vector2 dir3 = new Vector2(Mathf.Cos(120f * Mathf.Deg2Rad), Mathf.Sin(120f * Mathf.Deg2Rad));
            Assert.That(Vector2.Distance(runs[3].InnerStart, P3 - dir3 * thirty), Is.LessThan(1e-3f));

            // Edge3 ends at P4, a REFLEX corner: no extension at all - the two walls already overlap there.
            Assert.That(Vector2.Distance(runs[3].InnerEnd, P4), Is.LessThan(1e-4f), "a reflex corner adds no extension");
            Assert.That(Vector2.Distance(runs[4].InnerStart, P4), Is.LessThan(1e-4f));
        }

        // The real arena's own Source Outline and centre (ArenaSymmetry, read live at HEAD 015cfde - a literal
        // constant per the amendment's own rule: a scene edit can't silently re-baseline this test). Every corner is
        // one of Amendment 1 section A's two real kinds: a 90 degree outward corner, or one of the six 270 degree
        // reflex corners (three of them seams onto the next third) - the exact shape the BEFORE coverage record
        // (coverage-before.txt) finds 12 notches in today's captured walls.
        //
        // P1 and P2 (the Tier III recess's back edge) updated 2026-09-20: Tudor - "increase the pocket size by a
        // small bit (0.2-0.3)" so a portal (needs 2.5 m of floor) fits behind the recess's jersey barrier (it had
        // ~2.4 m). Both points moved 0.25 m further from RealCentre, along the P0->P1 direction (already exactly
        // perpendicular to the P0-P3 mouth line), which is also the P3->P2 direction - so the back edge stays
        // parallel to the mouth line, the same length, just 0.25 m further out. P0 and P3 (the mouth corners, one of
        // them a third-to-third seam) are untouched, so every other corner's angle and extension is unchanged.
        private static readonly Vector3 RealCentre = new Vector3(65.05f, 0f, 53.34f);
        private static readonly List<Vector2> RealSourceOutline = new List<Vector2>
        {
            new Vector2(96.184f, 60.034f),
            new Vector2(98.9985046f, 61.6590042f),
            new Vector2(89.22851f, 78.581f),
            new Vector2(86.414f, 76.956f),
            new Vector2(75.451f, 95.944f),
            new Vector2(75.451f, 121.401f),
            new Vector2(54.649f, 121.401f),
            new Vector2(54.649f, 95.944f),
        };

        [Test]
        public void TheRealOutlineIsClosedAtEveryCornerAndSeam()
        {
            ArenaBounds bounds = ArenaBounds.FromSourceOutline(RealSourceOutline, RealCentre);
            IReadOnlyList<Vector2> wholeOutline = bounds.Polygon;
            Assert.AreEqual(RealSourceOutline.Count * 3, wholeOutline.Count);

            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(wholeOutline, wholeOutline.Count, Thickness);
            Assert.AreEqual(wholeOutline.Count, runs.Count, "one run per edge of the WHOLE (three-fold) outline");

            var footprints = new List<BoxFootprint>();
            foreach (ArenaWallPlan.Run run in runs)
                footprints.Add(run.Footprint(Thickness));

            List<string> holes = ArenaWallCoverage.FindHoles(bounds, footprints, Thickness);
            Assert.IsEmpty(holes, "outline-built walls, corners and seams included, should leave no hole:\n" + string.Join("\n", holes));
        }

        [Test]
        public void TheRealOutlinesDrawnWallsNeverShareSpace()
        {
            // The boxes (the colliders) still share space behind the inner faces at the corners that are not right angles (two in
            // each third, 30 degrees); the drawn walls, mitred there, only touch.
            ArenaBounds bounds = ArenaBounds.FromSourceOutline(RealSourceOutline, RealCentre);
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(bounds.Polygon, bounds.Polygon.Count, Thickness);

            int mitred = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].IsMitred) mitred++;
                for (int j = i + 1; j < runs.Count; j++)
                {
                    float drawn = ArenaPieceShapes.ConvexOverlapDepth(runs[i].DrawnCorners(Thickness), runs[j].DrawnCorners(Thickness));
                    Assert.That(drawn, Is.LessThan(Hairline + 1e-3f), $"drawn runs {i} and {j} share space");
                    if (runs[i].IsMitred && runs[j].IsMitred && ArenaPieceShapes.OverlapDepth(FootprintOf(runs[i]), FootprintOf(runs[j])) > Hairline + 1e-3f)
                        Assert.That(drawn, Is.GreaterThan(-1e-3f), $"mitred runs {i} and {j} share their slanted end, no gap");
                }
            }
            Assert.Greater(mitred, 0, "the real outline has corners that are not right angles");
        }

        [Test]
        public void ACornerThatIsNotARightAngleIsMitredSoTheDrawnWallsMeetAlongOneLine()
        {
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(Hexagon(), 6, Thickness);

            Assert.AreEqual(0f, runs[0].EndMitre, "a right angle keeps square ends");
            Assert.AreEqual(0f, runs[1].StartMitre, "a right angle keeps square ends");
            Assert.AreEqual(0f, runs[1].EndMitre, "a straight run keeps square ends");

            // P3: 30 degrees outward. The inner faces end at the corner, the outer faces where they meet.
            AssertMeetAlongOneLine(runs[2], runs[3], P3, "the 30 degree outward corner");
            Assert.That(runs[2].EndMitre, Is.GreaterThan(0f), "outward: the inner face is cut back");
            // P4: 120 degrees reflex. The inner faces meet at the corner; the outer faces stop where they meet.
            AssertMeetAlongOneLine(runs[3], runs[4], P4, "the reflex corner");
            Assert.That(runs[3].EndMitre, Is.LessThan(0f), "reflex: the outer face is cut back");

            void AssertMeetAlongOneLine(ArenaWallPlan.Run arriving, ArenaWallPlan.Run leaving, Vector2 corner, string label)
            {
                Vector2[] a = arriving.DrawnCorners(Thickness), b = leaving.DrawnCorners(Thickness);
                Assert.That(Vector2.Distance(a[1], corner), Is.LessThan(1e-3f), label + ": the arriving wall's inner face ends at the corner");
                Assert.That(Vector2.Distance(b[0], corner), Is.LessThan(1e-3f), label + ": the leaving wall's inner face starts there");
                Assert.That(Vector2.Distance(a[2], b[3]), Is.LessThan(1e-3f), label + ": the outer faces meet at one point");
                Assert.That(Mathf.Abs(ArenaPieceShapes.ConvexOverlapDepth(a, b)), Is.LessThan(1e-3f), label + ": the two only touch");
            }
        }

        [Test]
        public void AnOutlineListedTheOtherWayRoundGivesTheSameWalls()
        {
            List<ArenaWallPlan.Run> forward = ArenaWallPlan.ForSource(Hexagon(), 6, Thickness);

            var reversed = new List<Vector2>(Hexagon());
            reversed.Reverse();
            List<ArenaWallPlan.Run> backward = ArenaWallPlan.ForSource(reversed, reversed.Count, Thickness);

            Assert.AreEqual(forward.Count, backward.Count);

            // Same physical walls: for every forward run there is a backward run with the same footprint (its inner
            // face's two endpoints match, in either order, since the reversed outline walks each edge backwards).
            foreach (ArenaWallPlan.Run f in forward)
            {
                bool found = false;
                foreach (ArenaWallPlan.Run b in backward)
                {
                    bool sameWay = Vector2.Distance(f.InnerStart, b.InnerStart) < 1e-3f && Vector2.Distance(f.InnerEnd, b.InnerEnd) < 1e-3f;
                    bool otherWay = Vector2.Distance(f.InnerStart, b.InnerEnd) < 1e-3f && Vector2.Distance(f.InnerEnd, b.InnerStart) < 1e-3f;
                    if (sameWay || otherWay) { found = true; break; }
                }
                Assert.IsTrue(found, $"no matching wall found for a forward run from {f.InnerStart} to {f.InnerEnd}");
            }
        }
    }
}
