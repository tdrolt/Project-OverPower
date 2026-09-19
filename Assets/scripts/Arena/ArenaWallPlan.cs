using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// Turns the arena's outline into one straight wall per edge (Amendment 1, Decision D3 - Tudor: "make sure that
    /// in this arena the walls dont have an empty space at the corners"). Today's walls are captured box for box and
    /// each stops short of a corner; this instead builds each run exactly from the outline, corner to corner:
    ///
    /// - An OUTWARD (convex) corner - today's 90 degree capital and side corners - gets each of its two walls
    ///   extended past the corner by thickness * tan(turn / 2), which is exactly enough for their outer edges to meet
    ///   with no gap behind the corner (a 90 degree turn needs exactly one thickness of extension).
    /// - An INWARD (reflex) corner - the two mouth corners of each Tier III recess, and the two capital-enclosure
    ///   mouths - needs no extension at all: the two walls already overlap behind a reflex corner, so extending them
    ///   there would poke a wedge into the play space instead of closing a gap.
    /// - A straight run (no turn - one of today's 14 in-line seams within a single logical wall) still gets a
    ///   hairline 1 cm overlap, so floating-point rounding never leaves a zero-width crack between two collinear
    ///   pieces that are meant to butt together as one wall.
    ///
    /// The outline may be listed either way round (like ArenaBounds): the winding is worked out from the whole
    /// outline's signed area, so "outward" always means "away from the enclosed area", never "clockwise" or
    /// "counter-clockwise" as such.
    /// </summary>
    public static class ArenaWallPlan
    {
        public const float StraightOverlapMetres = 0.01f;

        /// <summary>Below this many radians either way, a corner counts as a straight run rather than a genuine
        /// turn - float noise from three-fold rotation must never be read as a real (if tiny) corner.</summary>
        private const float StraightToleranceRadians = 1e-4f;

        public readonly struct Run
        {
            /// <summary>The wall's inner face start point, extension already applied.</summary>
            public readonly Vector2 InnerStart;

            /// <summary>The wall's inner face end point, extension already applied.</summary>
            public readonly Vector2 InnerEnd;

            /// <summary>Unit vector pointing into the arena, perpendicular to the run (local +Z once built).</summary>
            public readonly Vector2 Inward;

            public readonly float Length;

            /// <summary>A Unity Y-Euler yaw whose local +Z is Inward and local +X runs InnerStart -> InnerEnd.</summary>
            public readonly float UnityYawDegrees;

            public Run(Vector2 innerStart, Vector2 innerEnd, Vector2 inward)
            {
                InnerStart = innerStart;
                InnerEnd = innerEnd;
                Inward = inward;
                Length = Vector2.Distance(innerStart, innerEnd);
                UnityYawDegrees = Quaternion.LookRotation(new Vector3(inward.x, 0f, inward.y), Vector3.up).eulerAngles.y;
            }

            /// <summary>The box's world XZ centre: thickness/2 outside the inner face, along the midpoint.</summary>
            public Vector2 Centre(float thickness) => (InnerStart + InnerEnd) * 0.5f - Inward * (thickness * 0.5f);

            /// <summary>The footprint the built box leaves on the floor, for the coverage/intrusion checks.</summary>
            public BoxFootprint Footprint(float thickness)
            {
                Vector2 along = Length > 0.0001f ? (InnerEnd - InnerStart) / Length : new Vector2(-Inward.y, Inward.x);
                return new BoxFootprint(Centre(thickness), along, Length * 0.5f, thickness * 0.5f);
            }
        }

        /// <summary>
        /// One Run per edge of <paramref name="wholeOutline"/> (a closed polygon of any winding), keeping only the
        /// first <paramref name="sourceEdges"/> of them - the Source third's own share. Edge sourceEdges-1 to
        /// sourceEdges is the seam onto the next third, so every third closes on the next with the same rule as any
        /// other corner: outward corners and seams get filled, reflex ones don't.
        /// </summary>
        public static List<Run> ForSource(IReadOnlyList<Vector2> wholeOutline, int sourceEdges, float thickness)
        {
            var runs = new List<Run>();
            if (wholeOutline == null)
                return runs;
            int count = wholeOutline.Count;
            if (count < 3 || sourceEdges <= 0)
                return runs;

            // Positive signed area = interior lies to the left of travel (the standard planar convention, applied
            // here to (x, z) exactly as if z were the usual y). Negative = interior lies to the right. Either way,
            // multiplying a raw left-turn (CCW) angle by this sign always yields a positive value at an outward
            // corner and a negative one at a reflex corner, regardless of which way the outline is listed.
            float leftSign = SignedArea(wholeOutline) >= 0f ? 1f : -1f;

            for (int i = 0; i < sourceEdges && i < count; i++)
            {
                Vector2 a = wholeOutline[i];
                Vector2 b = wholeOutline[(i + 1) % count];
                Vector2 prev = wholeOutline[(i - 1 + count) % count];
                Vector2 next = wholeOutline[(i + 2) % count];

                Vector2 dir = (b - a).normalized;
                Vector2 inward = leftSign * new Vector2(-dir.y, dir.x);

                float startExtension = CornerExtension(prev, a, b, leftSign, thickness);
                float endExtension = CornerExtension(a, b, next, leftSign, thickness);

                Vector2 innerStart = a - dir * startExtension;
                Vector2 innerEnd = b + dir * endExtension;
                runs.Add(new Run(innerStart, innerEnd, inward));
            }

            return runs;
        }

        /// <summary>The extension both walls meeting at the corner (prev -> corner -> next) get, applied identically
        /// whichever of the two edges is asking (the end of one, the start of the other).</summary>
        private static float CornerExtension(Vector2 prev, Vector2 corner, Vector2 next, float leftSign, float thickness)
        {
            Vector2 inDir = (corner - prev).normalized;
            Vector2 outDir = (next - corner).normalized;

            // The signed turn from inDir to outDir: atan2(cross, dot) is the raw CCW angle in ordinary planar
            // (x, y) terms; leftSign turns that into "positive = outward" regardless of the outline's own winding.
            float cross = inDir.x * outDir.y - inDir.y * outDir.x;
            float dot = inDir.x * outDir.x + inDir.y * outDir.y;
            float turnRadians = leftSign * Mathf.Atan2(cross, dot);

            if (turnRadians > StraightToleranceRadians)
                return thickness * Mathf.Tan(turnRadians * 0.5f);
            if (turnRadians < -StraightToleranceRadians)
                return 0f;
            return StraightOverlapMetres;
        }

        private static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float sum = 0f;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % n];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum * 0.5f;
        }
    }
}
