using UnityEngine;

namespace Overpower.Match
{
    /// <summary>
    /// The small maths behind the capture ring on the ground and the progress ring on the minimap, kept pure so it's
    /// tested: where a point on a ring is, how many points a part-filled band needs, and the pulse/blink curves.
    ///
    /// Angles are Unity yaw: degrees clockwise seen from above, 0 = +Z. That is also "clockwise on screen" for this
    /// game's camera, which looks down at the player without rolling, so a band filling clockwise here fills
    /// clockwise on screen.
    /// </summary>
    public static class CaptureRingGeometry
    {
        /// <summary>The point <paramref name="clockwiseDegrees"/> round from <paramref name="startYawDegrees"/> on a
        /// flat ring. Pass the camera's yaw as the start to begin at the top of the screen. Height is the centre's.</summary>
        public static Vector3 PointOnRing(Vector3 centre, float radius, float startYawDegrees, float clockwiseDegrees)
        {
            float radians = (startYawDegrees + clockwiseDegrees) * Mathf.Deg2Rad;
            return new Vector3(centre.x + Mathf.Sin(radians) * radius, centre.y, centre.z + Mathf.Cos(radians) * radius);
        }

        /// <summary>Points in a band filled to <paramref name="fill01"/> of a ring made of <paramref name="segments"/>
        /// pieces: 0 when empty, at least 2 (a line needs two), segments + 1 when full (the last point closes the
        /// circle). Rounded to whole pieces, so the band only needs new points when this number changes.</summary>
        public static int ArcPointCount(float fill01, int segments)
        {
            if (fill01 <= 0f || segments < 1)
                return 0;
            int pieces = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * segments), 1, segments);
            return pieces + 1;
        }

        public static float ArcStepDegrees(int segments) => 360f / Mathf.Max(1, segments);

        /// <summary>0 → 1 → 0 once per 1/perSecond seconds, starting at 0: a pulse that begins at the base colour.</summary>
        public static float Pulse01(float timeSeconds, float perSecond) =>
            0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * perSecond * timeSeconds);

        /// <summary>1 → 0 → 1, starting fully visible: a paused band blinks off and back on.</summary>
        public static float Blink01(float timeSeconds, float perSecond) => 1f - Pulse01(timeSeconds, perSecond);
    }
}
