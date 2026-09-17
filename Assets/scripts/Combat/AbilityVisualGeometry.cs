using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The small maths behind the ability visuals (Tudor, 2026-09-17: "make the abilities a bit clearer"), kept pure so
    /// it is tested, and so every visual is drawn from the SAME number the gameplay reads - a mine's Trigger Radius, the
    /// flamethrower's Cone Angle - never a second copy typed into a visual.
    ///
    /// Angles are Unity yaw: degrees clockwise seen from above, 0 = +Z. Points are in the visual's own local space, flat
    /// on the floor (y = 0).
    /// </summary>
    public static class AbilityVisualGeometry
    {
        /// <summary>A point on a flat circle of this radius, yawDegrees clockwise from +Z.</summary>
        public static Vector3 CirclePoint(float radius, float yawDegrees)
        {
            float radians = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius);
        }

        /// <summary>Vertices in a flat cone ("fan") of arcSegments slices: the tip plus arcSegments + 1 arc points.</summary>
        public static int FanVertexCount(int arcSegments) => Mathf.Max(1, arcSegments) + 2;

        /// <summary>
        /// Fan vertex <paramref name="index"/>, opening along +Z: 0 is the tip (the caster), 1..arcSegments + 1 run along
        /// the arc from the left edge (-half angle) to the right edge (+half angle), all at <paramref name="range"/>. The
        /// same shape ConeFilter.IsWithinCone tests: range and half-angle measured flat from the tip.
        /// </summary>
        public static Vector3 FanVertex(int index, float range, float fullAngleDegrees, int arcSegments)
        {
            if (index <= 0)
                return Vector3.zero;

            int slices = Mathf.Max(1, arcSegments);
            float t = Mathf.Clamp01((index - 1) / (float)slices);
            return CirclePoint(range, Mathf.Lerp(-fullAngleDegrees * 0.5f, fullAngleDegrees * 0.5f, t));
        }

        /// <summary>Triangles (tip, i, i + 1) for every slice, clockwise seen from above so the fan faces up.
        /// <paramref name="triangles"/> must hold 3 * arcSegments entries.</summary>
        public static void FillFanTriangles(int arcSegments, int[] triangles)
        {
            int slices = Mathf.Max(1, arcSegments);
            for (int i = 0; i < slices; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        /// <summary>How many posts stand round a cage of this radius so no two neighbours are more than maxSpacing apart
        /// (at least 3).</summary>
        public static int CagePostCount(float radius, float maxSpacing)
        {
            if (radius <= 0f)
                return 3;
            return Mathf.Max(3, Mathf.CeilToInt(2f * Mathf.PI * radius / Mathf.Max(0.1f, maxSpacing)));
        }

        /// <summary>Radius of the circle where a sphere cuts a flat plane, 0 when the plane misses it. A rocket's splash is
        /// a sphere round the blast and its falloff is measured to a target's ROOT, so the circle at a standing player's
        /// root height is exactly where a standing player still takes splash.</summary>
        public static float RadiusOnPlane(float sphereRadius, float sphereCentreY, float planeY)
        {
            float dy = sphereCentreY - planeY;
            float squared = sphereRadius * sphereRadius - dy * dy;
            return squared > 0f ? Mathf.Sqrt(squared) : 0f;
        }

        /// <summary>How far a capsule's root sits above its feet - the same derivation TestRangeSpawner.Grounded uses
        /// (0.5 m for the player, measured 2026-09-13).</summary>
        public static float RootAboveFeet(float capsuleCentreY, float capsuleHeight, float scaleY) =>
            -(capsuleCentreY - capsuleHeight * 0.5f) * scaleY;

        /// <summary>Seconds a pull of this distance takes at this speed; 0 when either isn't positive.</summary>
        public static float PullSeconds(float distance, float speed) =>
            distance > 0f && speed > 0f ? distance / speed : 0f;

        /// <summary>1 at age 0, fading linearly to 0 at <paramref name="seconds"/>; 0 when seconds isn't positive.</summary>
        public static float Fade01(float ageSeconds, float seconds) =>
            seconds > 0f ? Mathf.Clamp01(1f - ageSeconds / seconds) : 0f;

        /// <summary>
        /// A3 (Tudor 2026-09-17 evening): 0 at the fan's tip, 1 on the arc - the RADIAL position a soft cone's
        /// per-vertex colour/alpha fades along, warm at the tip to clear at the far edge. Same fan topology as
        /// FanVertex (one tip, one ring of arc points at full range), so this is just another reading of the same
        /// vertex index, not a second mesh shape.
        /// </summary>
        public static float FanVertexRangeFraction(int index) => index <= 0 ? 0f : 1f;

        /// <summary>
        /// A3: 0 at the centre bearing, 1 at either side edge - the ANGULAR position a soft cone's alpha fades
        /// toward, so the flame reads narrower than its own hit cone rather than filling the screen with a flat wash
        /// of colour. The tip has no side (every direction meets there), so it reads 0, matching the tip's full
        /// RangeFraction-driven brightness.
        /// </summary>
        public static float FanVertexSideFraction(int index, int arcSegments)
        {
            if (index <= 0)
                return 0f;

            int slices = Mathf.Max(1, arcSegments);
            float t = Mathf.Clamp01((index - 1) / (float)slices);
            return Mathf.Abs(t * 2f - 1f);
        }
    }
}
