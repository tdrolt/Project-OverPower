using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>Where the Dash's streak and floor chevrons sit and how long they show. The streak runs straight along the dash line from the
    /// launch to just behind the dasher's progress on it, so a remote copy that wobbles sideways or stops early at a wall still draws a
    /// straight path that never runs past the dash or behind its start, and the chevrons appear only where the dasher got.</summary>
    public static class DashLook
    {
        /// <summary>How far chevron <paramref name="index"/> sits from the launch along the dash: the first at the launch, one spacing
        /// apart after it, squeezed together on a dash too short to hold them all.</summary>
        public static float ChevronDistance(int index, int count, float spacing, float dashDistance)
        {
            float step = Mathf.Max(0f, spacing);
            if (count > 0 && dashDistance > 0f)
                step = Mathf.Min(step, dashDistance / count);
            return step * Mathf.Max(0, index);
        }

        /// <summary>Point <paramref name="k"/> of a flat chevron with its tip at <paramref name="tip"/>: 0 and 2 are the wings, trailing the
        /// tip by <paramref name="depth"/>, <paramref name="width"/> apart; 1 is the tip, pointing along <paramref name="forward"/>.</summary>
        public static Vector3 ChevronPoint(Vector3 tip, Vector3 forward, float width, float depth, int k)
        {
            if (k == 1)
                return tip;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float side = k == 0 ? -0.5f : 0.5f;
            return tip - forward * depth + right * (width * side);
        }

        /// <summary>The dasher's progress along the dash line from the launch, flat, between 0 and the dash's length.</summary>
        public static float StreakReach(Vector3 start, Vector3 forward, Vector3 body, float dashDistance)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0f)
                return 0f;
            Vector3 travelled = body - start;
            travelled.y = 0f;
            return Mathf.Clamp(Vector3.Dot(travelled, forward.normalized), 0f, Mathf.Max(0f, dashDistance));
        }

        /// <summary>Where the streak's wide end sits along the dash: <paramref name="gap"/> short of the dasher, so it never paints over them.</summary>
        public static float HeadReach(float bodyReach, float gap) => Mathf.Max(0f, bodyReach - Mathf.Max(0f, gap));

        /// <summary>A chevron shows once the dasher has reached it, so a dash stopped short by a wall draws none past where it got.</summary>
        public static bool ChevronShown(float chevronDistance, float bodyReach) => bodyReach >= chevronDistance;

        /// <summary>Before the dasher has been seen on this screen: nothing of the dash is drawn.</summary>
        public const float NotSeenYet = -1f;

        /// <summary>How far along the dash the dasher was when this screen first saw them (their fog gate opened): the streak and chevrons start
        /// there, so a dash out of the fog never draws back to where it was launched. Fixed once found.</summary>
        public static float SeenFrom(float seenFrom, bool shownNow, float bodyReach) =>
            seenFrom >= 0f || !shownNow ? seenFrom : Mathf.Max(0f, bodyReach);

        /// <summary>Solid while the dash travels, then fading to nothing over <paramref name="fadeSeconds"/>.</summary>
        public static float Alpha(float ageSeconds, float travelSeconds, float fadeSeconds) =>
            ageSeconds <= travelSeconds ? 1f : AbilityVisualGeometry.Fade01(ageSeconds - travelSeconds, fadeSeconds);
    }
}
