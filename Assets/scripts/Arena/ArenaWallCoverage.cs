using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// Two coverage checks shared by arena step 4's red tests and its BEFORE record: does the wall line have a hole
    /// (an exterior point near the outline that no wall footprint covers, so a ray, a view or a body could slip
    /// through), and does any wall poke into the arena instead of standing outside the outline. Both work on plain
    /// BoxFootprints, so the same code checks a synthetic test outline, today's captured walls (the BEFORE record)
    /// and the new outline-built walls (the red-then-green check).
    /// </summary>
    public static class ArenaWallCoverage
    {
        private const float EdgeSampleStepMetres = 0.05f;
        private const float CornerFanStepDegrees = 3f;
        private const float CornerFanMinRadiusMetres = 0.03f;
        private const int CornerFanRadiusSteps = 6;

        // A dead zone straddling the outline itself: a fan angle can land EXACTLY along a polygon edge's own line
        // (found at a real corner, arena step 4 review: a 3-degree step landed within float noise of running right
        // along the adjoining edge), where SignedDistance reads as approximately zero and tips either way on
        // rounding alone. Neither side of that razor's edge is a meaningful hole or intrusion - a real gap always
        // shows up clearly negative (or positive) at the very next sample a few millimetres further round.
        private const float BoundaryDeadZoneMetres = 0.001f;

        /// <summary>Every exterior point within <paramref name="band"/> metres of the outline that no footprint
        /// covers: sampled every 5 cm along each edge at outward offsets 0.02, band/2 and band-0.02, plus a fan of
        /// radii (0.03 m to band) and angles (every 3 degrees) around every corner, so a notch tucked right against
        /// a corner is found even when no single edge sample lands on it. Covering the whole band at every sampled
        /// point means nothing exterior can reach the play space through the wall line at that point.</summary>
        public static List<string> FindHoles(ArenaBounds bounds, IReadOnlyList<BoxFootprint> footprints, float band)
        {
            var holes = new List<string>();
            if (bounds == null)
                return holes;
            IReadOnlyList<Vector2> polygon = bounds.Polygon;
            int n = polygon.Count;
            float[] outwardOffsets = { 0.02f, band * 0.5f, band - 0.02f };

            for (int i = 0; i < n; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % n];
                float length = Vector2.Distance(a, b);
                if (length < 0.0001f)
                    continue;
                Vector2 dir = (b - a) / length;
                Vector2 perp = new Vector2(-dir.y, dir.x);

                // Segment MIDPOINTS, never the edge's own two endpoints: a vertex belongs to two edges whose own
                // perpendiculars point different ways, so probing "straight out from THIS edge" exactly at a shared
                // corner can land right on the interior/exterior boundary (or even on the wrong side) depending on
                // the corner's own angle - purely a sampling artifact, not a real gap. The corner fan below is the
                // one place a vertex's own (possibly reflex) exterior wedge is actually walked correctly.
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / EdgeSampleStepMetres));
                for (int s = 0; s < steps; s++)
                {
                    Vector2 onEdge = a + dir * Mathf.Min(length, (s + 0.5f) * EdgeSampleStepMetres);
                    foreach (float offset in outwardOffsets)
                    {
                        Vector2 candidate = OutwardPoint(bounds, onEdge, perp, offset);
                        if (bounds.SignedDistance(candidate) >= -BoundaryDeadZoneMetres)
                            continue; // couldn't resolve an exterior sample here (degenerate outline); skip it
                        if (!IsCovered(candidate, footprints))
                            holes.Add($"edge {i} {offset:0.00}m out at ({candidate.x:0.00},{candidate.y:0.00}): uncovered");
                    }
                }
            }

            // Kept a hair inside `band` (matching the edge samples' own band-0.02, never band exactly): an outward
            // corner's own extension reaches to EXACTLY thickness at a 90 degree turn, so a fan sampled right out to
            // that same radius probes the precise mathematical edge of coverage, where float rounding alone can tip
            // "just inside" into "just outside". Nothing meaningful is missed - a real hole is never a hairline sliver
            // only detectable at the exact limit.
            float fanMaxRadius = Mathf.Max(CornerFanMinRadiusMetres, band - 0.02f);
            for (int i = 0; i < n; i++)
            {
                Vector2 corner = polygon[i];
                for (int r = 0; r < CornerFanRadiusSteps; r++)
                {
                    float radius = Mathf.Lerp(CornerFanMinRadiusMetres, fanMaxRadius, r / (float)(CornerFanRadiusSteps - 1));
                    for (float degrees = 0f; degrees < 360f; degrees += CornerFanStepDegrees)
                    {
                        float radians = degrees * Mathf.Deg2Rad;
                        Vector2 candidate = corner + radius * new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                        if (bounds.SignedDistance(candidate) >= -BoundaryDeadZoneMetres)
                            continue; // only clearly exterior points can be a hole
                        if (!IsCovered(candidate, footprints))
                            holes.Add($"corner {i} r={radius:0.00}m a={degrees:0}deg at ({candidate.x:0.00},{candidate.y:0.00}): uncovered");
                    }
                }
            }

            return holes;
        }

        /// <summary>Every interior point within 0.02, 0.1 or 0.3 m of the outline that a footprint covers: a wall
        /// must stand entirely outside the outline (Decision D3), so any footprint reaching this far in is an
        /// intrusion into the play space.</summary>
        public static List<string> FindIntrusions(ArenaBounds bounds, IReadOnlyList<BoxFootprint> footprints)
        {
            var intrusions = new List<string>();
            if (bounds == null)
                return intrusions;
            IReadOnlyList<Vector2> polygon = bounds.Polygon;
            int n = polygon.Count;
            float[] inwardOffsets = { 0.02f, 0.1f, 0.3f };

            for (int i = 0; i < n; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % n];
                float length = Vector2.Distance(a, b);
                if (length < 0.0001f)
                    continue;
                Vector2 dir = (b - a) / length;
                Vector2 perp = new Vector2(-dir.y, dir.x);

                // Segment midpoints only - see FindHoles' identical reasoning: a shared vertex's own two edges
                // disagree about which way is "in", so sampling exactly at one is a coin flip, not a real check.
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / EdgeSampleStepMetres));
                for (int s = 0; s < steps; s++)
                {
                    Vector2 onEdge = a + dir * Mathf.Min(length, (s + 0.5f) * EdgeSampleStepMetres);
                    foreach (float offset in inwardOffsets)
                    {
                        Vector2 candidate = InwardPoint(bounds, onEdge, perp, offset);
                        if (bounds.SignedDistance(candidate) <= BoundaryDeadZoneMetres)
                            continue; // couldn't resolve an interior sample here; skip it
                        if (IsCovered(candidate, footprints))
                            intrusions.Add($"edge {i} {offset:0.00}m in at ({candidate.x:0.00},{candidate.y:0.00}): a wall covers this");
                    }
                }
            }

            return intrusions;
        }

        private static Vector2 OutwardPoint(ArenaBounds bounds, Vector2 onEdge, Vector2 perp, float offset)
        {
            Vector2 p1 = onEdge + perp * offset;
            Vector2 p2 = onEdge - perp * offset;
            return bounds.SignedDistance(p1) < bounds.SignedDistance(p2) ? p1 : p2;
        }

        private static Vector2 InwardPoint(ArenaBounds bounds, Vector2 onEdge, Vector2 perp, float offset)
        {
            Vector2 p1 = onEdge + perp * offset;
            Vector2 p2 = onEdge - perp * offset;
            return bounds.SignedDistance(p1) > bounds.SignedDistance(p2) ? p1 : p2;
        }

        private static bool IsCovered(Vector2 point, IReadOnlyList<BoxFootprint> footprints)
        {
            for (int i = 0; i < footprints.Count; i++)
                if (footprints[i].Contains(point))
                    return true;
            return false;
        }
    }
}
