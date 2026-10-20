using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The jagged arcs between an Electric Fence's pylons: one closed loop per arc height, pinned to every pylon, the points between
    /// jumping in, out, up and down. The shape comes only from a seed (StunArcsRule.Seed on the server clock), so every client draws the same.</summary>
    public static class FenceArcsRule
    {
        public static int PointCount(int posts, int pointsPerGap) => Mathf.Max(3, posts) * Mathf.Max(1, pointsPerGap);

        /// <summary>Point <paramref name="index"/> of arc <paramref name="level"/>, relative to the fence's centre at the arc's own height.
        /// Every <paramref name="pointsPerGap"/>-th point stands on a pylon; the others keep their place round the ring and jump by up to
        /// <paramref name="jitter"/> metres.</summary>
        public static Vector3 Point(int seed, int level, int index, int posts, int pointsPerGap, float radius, float jitter)
        {
            int count = PointCount(posts, pointsPerGap);
            float yaw = index * 360f / count;
            if (index % Mathf.Max(1, pointsPerGap) == 0)
                return AbilityVisualGeometry.CirclePoint(radius, yaw);
            float r = radius + (StunArcsRule.Hash01(seed, level, 2 * index) * 2f - 1f) * jitter;
            Vector3 p = AbilityVisualGeometry.CirclePoint(r, yaw);
            p.y = (StunArcsRule.Hash01(seed, level, 2 * index + 1) * 2f - 1f) * jitter;
            return p;
        }

        public static Color PylonBody(Color team, float shade) => new Color(team.r * shade, team.g * shade, team.b * shade, 1f);

        public static Color PylonGlow(Color team, float glow) => new Color(team.r * glow, team.g * glow, team.b * glow, 1f);
    }
}
