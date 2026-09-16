using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// The maths behind a three-way symmetric arena: one third is authored, and the other two are that third turned
    /// 120° and 240° about the arena's centre.
    ///
    /// Angles here read like a top-down map: counter-clockwise from +X, seen from above. That is the OPPOSITE sign of a
    /// Unity yaw (a positive Unity Y rotation turns clockwise seen from above). Keeping that conversion in this one
    /// class is its whole point: getting the sign wrong mirrors the copies instead of turning them, which looks almost
    /// right on a near-symmetric map and is easy to miss.
    /// </summary>
    public static class RadialSymmetry
    {
        public const float ThirdDegrees = 120f;

        /// <summary>The rotation that turns something <paramref name="thirds"/> × 120° counter-clockwise seen from
        /// above, about the vertical axis.</summary>
        public static Quaternion ThirdTurn(int thirds) => Quaternion.AngleAxis(-ThirdDegrees * thirds, Vector3.up);

        /// <summary>Turns a world point about <paramref name="centre"/>'s vertical axis. Height is unchanged.</summary>
        public static Vector3 RotatePoint(Vector3 point, Vector3 centre, int thirds) =>
            centre + ThirdTurn(thirds) * (point - centre);

        /// <summary>Turns an object's facing by the same amount RotatePoint turns its position.</summary>
        public static Quaternion RotateRotation(Quaternion rotation, int thirds) => ThirdTurn(thirds) * rotation;

        /// <summary>Map angle of a point around the centre, in [0, 360): 0 = +X, 90 = +Z.</summary>
        public static float MapAngleDegrees(Vector3 point, Vector3 centre)
        {
            float degrees = Mathf.Atan2(point.z - centre.z, point.x - centre.x) * Mathf.Rad2Deg;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        /// <summary>Which third a point is in: 0 for map angles [start, start+120), 1 for the next 120°, 2 for the
        /// last. The start border belongs to third 0, so every point is in exactly one third.</summary>
        public static int ThirdIndex(Vector3 point, Vector3 centre, float firstThirdStartDegrees)
        {
            // BorderEpsilonDegrees absorbs float32 trig round-trip noise (Cos/Sin then Atan2 can land a point that is
            // meant to be exactly on a border a few 1e-6 degrees short of it), so a point placed exactly on the start
            // border reliably reads as third 0 instead of wrapping into third 2.
            const float BorderEpsilonDegrees = 1e-3f;
            float fromStart = MapAngleDegrees(point, centre) - firstThirdStartDegrees + BorderEpsilonDegrees;
            fromStart = ((fromStart % 360f) + 360f) % 360f;
            return Mathf.Min(2, Mathf.FloorToInt(fromStart / ThirdDegrees));
        }
    }
}
