using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Arena;

namespace Overpower.Tests
{
    /// <summary>Arena amendment 1, step 4 (amended): pins ArenaWallPlan.ForSource - one wall run per outline edge,
    /// with an outward corner filled by thickness * tan(turn / 2), a reflex (inward) corner left unextended, and a
    /// straight run given a hairline 1 cm overlap - against a small hand-built hexagon with one corner of each kind
    /// (90 degrees, a straight pass-through, 30 degrees, and a reflex corner), verified with SignedArea's shoelace
    /// convention: (0,0) (4,0) (4,5) (4,8) (2,11.4641) (5,11.4641) has POSITIVE signed area, so "outward" here means
    /// exactly what CornerExtension itself calls a positive (CCW) turn - the same convention the real, three-fold
    /// arena outline resolves to on its own, whichever way a designer happens to list its points.</summary>
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

        [Test]
        public void EveryEdgeGetsOneWallWhoseInnerFaceRunsCornerToCorner()
        {
            // A plain rectangle: four 90 degree outward corners, nothing reflex or straight to complicate it.
            var rectangle = new List<Vector2> { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 6f), new Vector2(0f, 6f) };

            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(rectangle, rectangle.Count, Thickness);

            Assert.AreEqual(4, runs.Count, "one run per edge");
            for (int i = 0; i < runs.Count; i++)
            {
                ArenaWallPlan.Run run = runs[i];
                ArenaWallPlan.Run next = runs[(i + 1) % runs.Count];
                // Each wall's OUTER face (thickness further out than its inner face) reaches exactly where the next
                // wall's outer face starts: the 90 degree extension (thickness * tan45 = thickness) is exactly
                // enough for a mitred corner to close with no gap and no overlap beyond the corner itself.
                Vector2 thisOuterEnd = run.InnerEnd - run.Inward * Thickness;
                Vector2 nextOuterStart = next.InnerStart - next.Inward * Thickness;
                Assert.That(Vector2.Distance(thisOuterEnd, nextOuterStart), Is.LessThan(1e-4f),
                    $"run {i}'s outer end {thisOuterEnd} should meet run {(i + 1) % runs.Count}'s outer start {nextOuterStart}");
            }
        }

        [Test]
        public void OnlyOutwardCornersRunOnAndByJustEnoughToFillTheNotch()
        {
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(Hexagon(), 6, Thickness);
            Assert.AreEqual(6, runs.Count);

            float ninety = Thickness * Mathf.Tan(45f * Mathf.Deg2Rad);   // turn/2 = 45 deg
            float thirty = Thickness * Mathf.Tan(15f * Mathf.Deg2Rad);   // turn/2 = 15 deg

            // Edge0 (P0->P1) ends at P1, a 90 degree outward corner: extended past P1 by "ninety".
            Assert.That(Vector2.Distance(runs[0].InnerEnd, P1 + new Vector2(1f, 0f) * ninety), Is.LessThan(1e-3f),
                "a 90 degree corner should extend the arriving wall by thickness * tan(45 deg)");
            // Edge1 (P1->P2) starts at the SAME corner: extended back before P1 by the same amount.
            Assert.That(Vector2.Distance(runs[1].InnerStart, P1 - new Vector2(0f, 1f) * ninety), Is.LessThan(1e-3f));

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
        private static readonly Vector3 RealCentre = new Vector3(65.05f, 0f, 53.34f);
        private static readonly List<Vector2> RealSourceOutline = new List<Vector2>
        {
            new Vector2(96.184f, 60.034f),
            new Vector2(98.782f, 61.534f),
            new Vector2(89.012f, 78.456f),
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
