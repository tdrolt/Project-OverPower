using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>How the Blink's marks move: shards burst evenly round the spot the player left, fast first and slowing down, alternately
    /// tilted up and down so the burst has depth; the landing ring snaps out to full size, then widens as it fades. Fixed spread, no random, so
    /// every client draws the same burst.</summary>
    public static class BlinkLook
    {
        /// <summary>The way shard <paramref name="index"/> of <paramref name="count"/> flies: evenly round the spot, neighbours tilted
        /// <paramref name="tiltDegrees"/> up and down in turn.</summary>
        public static Vector3 ShardDirection(int index, int count, float tiltDegrees)
        {
            float yaw = count > 0 ? index * 360f / count : 0f;
            float tilt = index % 2 == 0 ? tiltDegrees : -tiltDegrees;
            return Quaternion.Euler(-tilt, yaw, 0f) * Vector3.forward;
        }

        /// <summary>How far the shards have flown <paramref name="ageSeconds"/> in: a quick burst that eases to a stop at
        /// <paramref name="reach"/> when <paramref name="seconds"/> are up.</summary>
        public static float ShardReach(float ageSeconds, float seconds, float reach)
        {
            float u = seconds > 0f ? Mathf.Clamp01(ageSeconds / seconds) : 1f;
            float left = 1f - u;
            return reach * (1f - left * left);
        }

        /// <summary>The landing ring's size <paramref name="ageSeconds"/> in: it snaps out to <paramref name="radius"/> over
        /// <paramref name="snapSeconds"/>, most of the way in the first third, then keeps widening by <paramref name="spread"/> of its size
        /// while it fades over <paramref name="fadeSeconds"/>.</summary>
        public static float RingRadius(float ageSeconds, float snapSeconds, float fadeSeconds, float radius, float spread)
        {
            float u = snapSeconds > 0f ? Mathf.Clamp01(ageSeconds / snapSeconds) : 1f;
            if (u < 1f)
            {
                float left = 1f - u;
                return radius * (1f - left * left * left);
            }
            float fading = fadeSeconds > 0f ? Mathf.Clamp01((ageSeconds - snapSeconds) / fadeSeconds) : 1f;
            return radius * (1f + Mathf.Max(0f, spread) * fading);
        }

        /// <summary>Solid while the ring snaps out, then fading to nothing over <paramref name="fadeSeconds"/>.</summary>
        public static float RingAlpha(float ageSeconds, float snapSeconds, float fadeSeconds) =>
            ageSeconds <= snapSeconds ? 1f : AbilityVisualGeometry.Fade01(ageSeconds - snapSeconds, fadeSeconds);
    }
}
