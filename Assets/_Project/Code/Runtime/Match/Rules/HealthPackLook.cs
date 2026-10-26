using UnityEngine;

namespace Overpower.Match
{
    /// <summary>How a health pack shows whether it can be taken: the healing green, glowing while ready and dimmed while it waits to come
    /// back, so the same green says "healing" in both states and only the brightness says "ready".</summary>
    public static class HealthPackLook
    {
        public static Color CrossColour(bool ready, Color healGreen, float readyGlow, float respawningBrightness)
        {
            float k = EdgeGlow(ready, readyGlow, respawningBrightness);
            return new Color(healGreen.r * k, healGreen.g * k, healGreen.b * k, 1f);
        }

        public static float EdgeGlow(bool ready, float readyGlow, float respawningBrightness) => ready ? readyGlow : respawningBrightness;
    }
}
