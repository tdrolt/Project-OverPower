using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Arena;

namespace Overpower.Tests
{
    /// <summary>Arena amendment 1, step 4 (amended): pins ArenaWallCoverage's two checks - FindHoles (a gap in the
    /// wall line an exterior ray, view or body could pass through) and FindIntrusions (a wall standing inside the
    /// outline rather than outside it) - against a small L-shaped polygon whose inner corner is exactly the kind of
    /// 270 degree reflex corner the real arena's Tier III recess mouths and capital-enclosure mouths are.</summary>
    public class ArenaWallCoverageTests
    {
        private const float Thickness = 0.724f;

        // An L-shape: five convex (90 degree) corners and one reflex (270 degree) corner at Q2 - the same two kinds
        // of corner the real arena has (Amendment 1, section A).
        private static List<Vector2> LShape() => new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(6f, 0f), new Vector2(6f, 4f),
            new Vector2(10f, 4f), new Vector2(10f, 10f), new Vector2(0f, 10f),
        };

        [Test]
        public void NoWallReachesIntoTheArena()
        {
            List<Vector2> polygon = LShape();
            ArenaBounds bounds = ArenaBounds.FromPolygon(polygon);
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(polygon, polygon.Count, Thickness);

            var footprints = new List<BoxFootprint>();
            foreach (ArenaWallPlan.Run run in runs)
                footprints.Add(run.Footprint(Thickness));

            List<string> intrusions = ArenaWallCoverage.FindIntrusions(bounds, footprints);
            Assert.IsEmpty(intrusions, "every wall stands outside the outline by construction:\n" + string.Join("\n", intrusions));
        }

        [Test]
        public void AWallBuiltWellInsideTheOutlineIsAnIntrusion()
        {
            // The negative case for the same check: a wall footprint deliberately placed centred ON the outline
            // (half of it inside) must be caught.
            List<Vector2> polygon = LShape();
            ArenaBounds bounds = ArenaBounds.FromPolygon(polygon);
            var intrudingWall = new BoxFootprint(new Vector2(3f, 0f), Vector2.right, 3f, Thickness * 0.5f);

            List<string> intrusions = ArenaWallCoverage.FindIntrusions(bounds, new List<BoxFootprint> { intrudingWall });
            Assert.IsNotEmpty(intrusions);
        }

        [Test]
        public void TheHoleFinderFindsTodaysKindOfCornerNotch()
        {
            List<Vector2> polygon = LShape();
            ArenaBounds bounds = ArenaBounds.FromPolygon(polygon);
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(polygon, polygon.Count, Thickness);

            // Correctly built: every run reaches its full, corner-extended length (the reflex corner at Q2 = (6,4)
            // needs no extension at all - the two walls already overlap there).
            var closedFootprints = new List<BoxFootprint>();
            foreach (ArenaWallPlan.Run run in runs)
                closedFootprints.Add(run.Footprint(Thickness));
            List<string> holesWhenClosed = ArenaWallCoverage.FindHoles(bounds, closedFootprints, Thickness);
            Assert.IsEmpty(holesWhenClosed, "walls meeting exactly at the reflex corner should leave no hole:\n" + string.Join("\n", holesWhenClosed));

            // Today's kind of bug: the two walls either side of the SAME reflex corner (Q1->Q2 and Q2->Q3) each stop
            // 0.73 m short of it, exactly like the real arena's 12 notches (Amendment 1, section A).
            var notchedFootprints = new List<BoxFootprint>();
            for (int i = 0; i < runs.Count; i++)
            {
                ArenaWallPlan.Run run = runs[i];
                Vector2 innerStart = run.InnerStart;
                Vector2 innerEnd = run.InnerEnd;
                Vector2 along = (innerEnd - innerStart).normalized;

                bool endsAtQ2 = Vector2.Distance(innerEnd, new Vector2(6f, 4f)) < 1e-3f;
                bool startsAtQ2 = Vector2.Distance(innerStart, new Vector2(6f, 4f)) < 1e-3f;
                if (endsAtQ2) innerEnd -= along * 0.73f;
                if (startsAtQ2) innerStart += along * 0.73f;

                var shortened = new ArenaWallPlan.Run(innerStart, innerEnd, run.Inward);
                notchedFootprints.Add(shortened.Footprint(Thickness));
            }
            List<string> holesWhenNotched = ArenaWallCoverage.FindHoles(bounds, notchedFootprints, Thickness);
            Assert.IsNotEmpty(holesWhenNotched, "two walls each 0.73 m short of the reflex corner should leave a hole there");
        }
    }
}
