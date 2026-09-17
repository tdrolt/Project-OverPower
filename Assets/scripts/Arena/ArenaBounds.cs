using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// The arena's playable outline seen from above, and the one "is this spot inside the arena" rule (movement
    /// step 3). Built from the Source third's outline (ArenaSymmetry.sourceOutline) turned into all three thirds with
    /// RadialSymmetry, so the outline obeys the same symmetry as the walls it traces.
    ///
    /// Why it exists: the ground outside the boundary walls is real terrain on the same layer as the arena floor, so
    /// "is there ground here" cannot tell inside from outside. Blink's landing, a portal's placement and arrival, and
    /// (through PlayerMotor) the out-of-arena safety net all ask this instead.
    ///
    /// Plain C#: tested in edit mode without a scene (ArenaBoundsTests). Vector2 here is a flat world point - x is
    /// world X and y is world Z.
    /// </summary>
    public sealed class ArenaBounds
    {
        private readonly Vector2[] polygon;

        private ArenaBounds(Vector2[] polygon) => this.polygon = polygon;

        /// <summary>The whole outline: the Source points, then their 120 degree turns, then their 240 degree turns.</summary>
        public IReadOnlyList<Vector2> Polygon => polygon;

        /// <summary>Null for fewer than two points: no outline means no arena bounds are known.</summary>
        public static ArenaBounds FromSourceOutline(IReadOnlyList<Vector2> sourceOutline, Vector3 centre)
        {
            if (sourceOutline == null || sourceOutline.Count < 2)
                return null;

            int count = sourceOutline.Count;
            var points = new Vector2[count * 3];
            for (int third = 0; third < 3; third++)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 turned = RadialSymmetry.RotatePoint(
                        new Vector3(sourceOutline[i].x, 0f, sourceOutline[i].y), centre, third);
                    points[third * count + i] = new Vector2(turned.x, turned.z);
                }
            }

            return new ArenaBounds(points);
        }

        /// <summary>Metres to the nearest edge: positive inside the outline, negative outside it.</summary>
        public float SignedDistance(Vector2 pointXZ)
        {
            float nearest = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[j], b = polygon[i];
                nearest = Mathf.Min(nearest, DistanceToSegment(pointXZ, a, b));
                // Even-odd crossing count along a ray toward +x: it needs no winding order, so the outline may be
                // listed either way round.
                if ((a.y > pointXZ.y) != (b.y > pointXZ.y) &&
                    pointXZ.x < a.x + (pointXZ.y - a.y) * (b.x - a.x) / (b.y - a.y))
                    inside = !inside;
            }

            return inside ? nearest : -nearest;
        }

        /// <summary>The same, for a world point: its height is ignored. Kept as its own overload so a Vector3 can
        /// never be converted to a Vector2 by (x, y) and silently read the height as a Z.</summary>
        public float SignedDistance(Vector3 world) => SignedDistance(new Vector2(world.x, world.z));

        /// <summary>True when the point is inside with at least <paramref name="margin"/> metres to the nearest edge -
        /// pass a player's radius for a spot a player has to fit in.</summary>
        public bool Contains(Vector3 world, float margin) => SignedDistance(world) >= margin;

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            float t = lengthSqr > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
