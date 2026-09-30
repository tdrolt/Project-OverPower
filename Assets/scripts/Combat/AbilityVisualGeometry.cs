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

        /// <summary>Vertices in a flat cone ("fan") of arcSegments slices and <paramref name="rings"/> concentric arcs
        /// beyond the tip (default 1, the plain triangle fan every other view's fan-shaped piece still uses): the tip
        /// plus rings * (arcSegments + 1) arc points.</summary>
        public static int FanVertexCount(int arcSegments, int rings = 1) =>
            1 + Mathf.Max(1, rings) * (Mathf.Max(1, arcSegments) + 1);

        /// <summary>
        /// Fan vertex <paramref name="index"/>, opening along +Z: 0 is the tip (the caster); every other index falls on
        /// one of <paramref name="rings"/> concentric arcs (ring 1 nearest the tip, ring <paramref name="rings"/> at the
        /// full <paramref name="range"/>), each running arcSegments + 1 points from the left edge (-half angle) to the
        /// right edge (+half angle). rings = 1 (the default) is the plain single-arc fan every prior view uses: every
        /// non-tip vertex sits at the full range, exactly as before this parameter existed. The same shape
        /// ConeFilter.IsWithinCone tests: range and half-angle measured flat from the tip.
        /// </summary>
        public static Vector3 FanVertex(int index, float range, float fullAngleDegrees, int arcSegments, int rings = 1)
        {
            if (index <= 0)
                return Vector3.zero;

            int slices = Mathf.Max(1, arcSegments);
            int ringCount = Mathf.Max(1, rings);
            int i = index - 1;
            int ring = i / (slices + 1) + 1; // 1..ringCount
            int slice = i % (slices + 1); // 0..slices
            float t = slice / (float)slices;
            float radius = range * ring / (float)ringCount;
            return CirclePoint(radius, Mathf.Lerp(-fullAngleDegrees * 0.5f, fullAngleDegrees * 0.5f, t));
        }

        /// <summary>Triangles for a fan of arcSegments slices and <paramref name="rings"/> concentric arcs: a tip fan
        /// onto ring 1, then a quad strip (two triangles per slice) between every pair of neighbouring rings, all
        /// clockwise seen from above so the fan faces up. <paramref name="triangles"/> must hold
        /// 3 * arcSegments * (2 * rings - 1) entries (3 * arcSegments when rings = 1, the pre-existing single-arc case).</summary>
        public static void FillFanTriangles(int arcSegments, int[] triangles, int rings = 1)
        {
            int slices = Mathf.Max(1, arcSegments);
            int ringCount = Mathf.Max(1, rings);
            int t = 0;
            for (int s = 0; s < slices; s++)
            {
                triangles[t++] = 0;
                triangles[t++] = 1 + s;
                triangles[t++] = 1 + s + 1;
            }
            for (int r = 1; r < ringCount; r++)
            {
                int innerStart = 1 + (r - 1) * (slices + 1);
                int outerStart = 1 + r * (slices + 1);
                for (int s = 0; s < slices; s++)
                {
                    int i0 = innerStart + s, i1 = innerStart + s + 1;
                    int o0 = outerStart + s, o1 = outerStart + s + 1;
                    triangles[t++] = i0; triangles[t++] = o0; triangles[t++] = o1;
                    triangles[t++] = i0; triangles[t++] = o1; triangles[t++] = i1;
                }
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
        /// A3 (Tudor 2026-09-17 evening): 0 at the fan's tip, 1 on the outermost arc (ring <paramref name="rings"/>) -
        /// the RADIAL position a soft cone's per-vertex colour/alpha fades along, warm at the tip to clear at the far
        /// edge. Same fan topology as FanVertex, so this is just another reading of the same vertex index, not a
        /// second mesh shape.
        ///
        /// CORRECTION (review, 2026-09-18): with the pre-existing default rings = 1 (a single tip and one outer
        /// arc, no vertex in between), this was a STEP function - exactly 0 at the tip and exactly 1 at every other
        /// vertex, whatever ring count a caller claimed to use - so a caller building its radial fade from this
        /// alone got a straight line from the tip to zero, never a held middle. Callers that want that middle need
        /// <paramref name="rings"/> greater than 1, which is what makes this genuinely continuous (ring / rings for
        /// ring 1..rings).
        /// </summary>
        public static float FanVertexRangeFraction(int index, int arcSegments, int rings = 1)
        {
            if (index <= 0)
                return 0f;

            int slices = Mathf.Max(1, arcSegments);
            int ringCount = Mathf.Max(1, rings);
            int ring = (index - 1) / (slices + 1) + 1;
            return ring / (float)ringCount;
        }

        /// <summary>
        /// A3: 0 at the centre bearing, 1 at either side edge - the ANGULAR position a soft cone's alpha fades
        /// toward, so the flame reads narrower than its own hit cone rather than filling the screen with a flat wash
        /// of colour. The tip has no side (every direction meets there), so it reads 0, matching the tip's own
        /// RangeFraction of 0. Ring-agnostic: works the same whichever of FanVertex's rings <paramref name="index"/>
        /// falls on, since every ring runs the same arcSegments + 1 points across the same angular sweep.
        /// </summary>
        public static float FanVertexSideFraction(int index, int arcSegments)
        {
            if (index <= 0)
                return 0f;

            int slices = Mathf.Max(1, arcSegments);
            int slice = (index - 1) % (slices + 1);
            float t = slice / (float)slices;
            return Mathf.Abs(t * 2f - 1f);
        }
    }
}
