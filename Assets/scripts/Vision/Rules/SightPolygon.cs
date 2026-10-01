using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// The exact outline of what one eye sees, as a triangle fan, from rays and a wall test. Pure (Vector2, Mathf and a
    /// raycast function only) so the shape rules are provable in edit mode; the game passes a Physics.Raycast on the
    /// Building layer.
    /// </summary>
    public static class SightPolygon
    {
        /// <summary>
        /// Clears <paramref name="into"/> and fills it with the fan: the eye position first, then the outline points
        /// in order around the eye. The cone (GDD: a cone toward the cursor) is rayCount rays spread evenly across the
        /// cone angle; the circle (GDD: a circle around the player) is the rest of the full turn. Each ray stops where a
        /// wall stops it, because sight stops where bullets stop (Tudor), plus wallRevealDepth so the wall face and top
        /// you look at stay lit (A3), but never past the shape's own reach. Circle rays are as dense per degree as the
        /// cone's, so a 90 degree cone and its 270 degree circle part both look smooth. A zero facing has no direction to
        /// look, so only the circle. If the circle is wider than the cone is long (never in practice) the cone takes the
        /// larger of the two. raycast(origin, unitDirection, maxDistance) returns the hit distance, or null for no hit.
        /// </summary>
        public static void Build(Eye eye, int rayCount, float wallRevealDepth,
            Func<Vector2, Vector2, float, float?> raycast, List<Vector2> into)
        {
            into.Clear();
            into.Add(eye.Position);

            SightShape shape = eye.Shape;
            bool hasCone = eye.Facing.sqrMagnitude > 1e-8f && shape.ConeAngleDegrees > 0f && shape.ConeLength > 0f;
            rayCount = Mathf.Max(2, rayCount);
            float coneAngle = Mathf.Clamp(shape.ConeAngleDegrees, 0f, 360f);
            float coneReach = Mathf.Max(shape.ConeLength, shape.CircleRadius);

            // Rays per degree: the cone's own, reused for the circle. Without a cone, as if for a 90 degree one.
            float perDegree = rayCount / (hasCone ? coneAngle : 90f);

            if (!hasCone)
            {
                AddArc(eye.Position, 0f, 360f, Mathf.CeilToInt(perDegree * 360f), shape.CircleRadius, wallRevealDepth, raycast, into);
                return;
            }

            float facing = Mathf.Atan2(eye.Facing.y, eye.Facing.x) * Mathf.Rad2Deg;
            float half = coneAngle * 0.5f;
            AddArc(eye.Position, facing - half, coneAngle, rayCount, coneReach, wallRevealDepth, raycast, into);

            float rest = 360f - coneAngle;
            if (rest > 1e-3f)
                AddArc(eye.Position, facing + half, rest, Mathf.CeilToInt(perDegree * rest), shape.CircleRadius, wallRevealDepth, raycast, into);
        }

        // count points from startDegrees across spanDegrees, both ends included; a full turn does not repeat its start.
        private static void AddArc(Vector2 origin, float startDegrees, float spanDegrees, int count, float reach,
            float reveal, Func<Vector2, Vector2, float, float?> raycast, List<Vector2> into)
        {
            count = Mathf.Max(2, count);
            bool fullTurn = spanDegrees >= 359.999f;
            float step = spanDegrees / (fullTurn ? count : count - 1);
            for (int i = 0; i < count; i++)
            {
                float radians = (startDegrees + step * i) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                float? hit = raycast(origin, dir, reach);
                float length = hit.HasValue ? Mathf.Min(hit.Value + reveal, reach) : reach;
                into.Add(origin + dir * length);
            }
        }
    }
}
