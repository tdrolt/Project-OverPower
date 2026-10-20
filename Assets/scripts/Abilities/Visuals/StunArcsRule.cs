using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>When a player crackles with stun arcs and what shape the arcs take. The arcs follow the STUNNED label every client already
    /// shows (stun beats slow), and their shape comes only from the server clock and the player's actor number through an integer hash,
    /// never UnityEngine.Random, so every client draws the same arcs at the same moment.</summary>
    public static class StunArcsRule
    {
        private const float GoldenAngle = 137.5f; // spreads the arcs round the body whatever their count
        private const float StartScatter = 120f; // a random turn on top of that spread so the arcs never sit in fixed slots; not a look number

        public static bool Show(StatusLabel label) => label == StatusLabel.Stunned;

        /// <summary>One value per 1/<paramref name="ratePerSecond"/> s of server time and per player. Floor division, so the step that
        /// contains the moment ServerTimestamp wraps through zero is as long as any other.</summary>
        public static int Seed(int serverMs, int actor, float ratePerSecond)
        {
            int stepMs = Mathf.Max(1, Mathf.RoundToInt(1000f / Mathf.Max(0.1f, ratePerSecond)));
            int step = serverMs / stepMs - (serverMs % stepMs < 0 ? 1 : 0);
            return unchecked(step * 7919 + actor * 104729);
        }

        /// <summary>Point <paramref name="k"/> of <paramref name="n"/> on arc <paramref name="arc"/>, relative to the middle of the body: a jagged
        /// run part way round the body (between the two sweeps, in degrees) between two heights. The two ends sit on the radius; the points
        /// between jump in and out by up to <paramref name="jitter"/> of it.</summary>
        public static Vector3 ArcPoint(int seed, int arc, int k, int n, float radius, float halfHeight, float jitter, float shortestSweep, float longestSweep)
        {
            float u = n > 1 ? Mathf.Clamp01(k / (float)(n - 1)) : 0f;
            float start = arc * GoldenAngle + Hash01(seed, arc, 0) * StartScatter;
            float sweep = Mathf.Lerp(shortestSweep, longestSweep, Hash01(seed, arc, 1));
            float y0 = (Hash01(seed, arc, 2) * 2f - 1f) * halfHeight;
            float y1 = (Hash01(seed, arc, 3) * 2f - 1f) * halfHeight;
            bool end = k <= 0 || k >= n - 1;
            float r = end ? radius : radius * (1f + (Hash01(seed, arc, 100 + k) * 2f - 1f) * jitter);
            float y = Mathf.Lerp(y0, y1, u) * (1f - jitter) + (end ? 0f : (Hash01(seed, arc, 200 + k) * 2f - 1f) * jitter * halfHeight);
            Vector3 p = AbilityVisualGeometry.CirclePoint(r, start + sweep * u);
            p.y = y;
            return p;
        }

        private static float Hash01(int seed, int a, int b)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)a * 0x85EBCA77u ^ (uint)b * 0xC2B2AE3Du;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }
    }
}
