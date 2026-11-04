using UnityEngine;

namespace Overpower.Bots
{
    public static partial class BotPathRules
    {
        /// <summary>A route's length through its corners, flat.</summary>
        public static float RouteMetres(Vector3[] corners)
        {
            float metres = 0f;
            if (corners == null) return metres;
            for (int i = 1; i < corners.Length; i++) metres += Flat(corners[i - 1], corners[i]);
            return metres;
        }

        /// <summary>The wide route when it was found and is at most maxRatio times the tight route's length, or when the tight route does not
        /// reach the goal; else the tight one.</summary>
        public static bool TakeWide(bool wideFound, float wideMetres, bool narrowComplete, float narrowMetres, float maxRatio) =>
            wideFound && (!narrowComplete || wideMetres <= narrowMetres * maxRatio);

        /// <summary>Flat, length 1, across a bend: square to the average of the way in and the way out, pointing to its right.</summary>
        public static Vector3 Across(Vector3 previous, Vector3 corner, Vector3 next)
        {
            Vector3 into = corner - previous;
            Vector3 outOf = next - corner;
            into.y = 0f;
            outOf.y = 0f;
            into = into.sqrMagnitude > 1e-6f ? into.normalized : Vector3.zero;
            outOf = outOf.sqrMagnitude > 1e-6f ? outOf.normalized : Vector3.zero;
            Vector3 along = into + outOf;
            if (along.sqrMagnitude < 1e-6f) along = into.sqrMagnitude > 0f ? into : Vector3.forward;
            return Vector3.Cross(Vector3.up, along.normalized);
        }

        /// <summary>The middle of the gap at a bend: the bend moved across by half the difference between the open ground on its right and on
        /// its left.</summary>
        public static Vector3 GapMiddle(Vector3 corner, Vector3 across, float leftMetres, float rightMetres) =>
            corner + across * ((rightMetres - leftMetres) * 0.5f);

        /// <summary>The share of full speed: slowSpeed on a tight route within slowWithinMetres of a bend (a corner that is not the route's
        /// end); full speed otherwise.</summary>
        public static float CornerSpeed(bool narrowRoute, bool bend, float bendMetres, float slowWithinMetres, float slowSpeed) =>
            narrowRoute && bend && bendMetres <= slowWithinMetres ? slowSpeed : 1f;
    }
}
