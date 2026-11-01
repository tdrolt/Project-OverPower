using UnityEngine;

namespace Overpower.Match
{
    /// <summary>How a pack shows whether it can be taken: its colour glowing while ready and dimmed while it waits to come back, so the colour
    /// says what it gives in both states and only the brightness says "ready"; the rings an ultimate
    /// pack's take shows.</summary>
    public static class HealthPackLook
    {
        public static Color CrossColour(bool ready, Color healGreen, float readyGlow, float respawningBrightness)
        {
            float k = EdgeGlow(ready, readyGlow, respawningBrightness);
            return new Color(healGreen.r * k, healGreen.g * k, healGreen.b * k, 1f);
        }

        public static float EdgeGlow(bool ready, float readyGlow, float respawningBrightness) => ready ? readyGlow : respawningBrightness;

        /// <summary>How far ring <paramref name="index"/> is through its rise (0..1), Delay after the ring before it; below 0 not started.</summary>
        public static float PickupRingProgress(float elapsed, int index, float delay, float duration) =>
            duration > 0f ? (elapsed - index * delay) / duration : 1f;

        /// <summary>A ring's height: fast off the floor, slowing towards the top.</summary>
        public static float PickupRingHeight(float progress, float rise)
        {
            float p = Mathf.Clamp01(progress);
            return rise * p * (2f - p);
        }

        public static float PickupRingAlpha(float progress) => 1f - Mathf.Clamp01(progress);
    }
}
