using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The blinking light on a mine's cap, counted from the mine's age since placing (which every client agrees on), so every
    /// screen blinks the same mine at the same moment.</summary>
    public static class MinePuckLook
    {
        /// <summary>1 for the first <paramref name="onSeconds"/> of every period, else <paramref name="dim"/>; a period of 0 is a steady light.</summary>
        public static float CapLight(float secondsSincePlaced, float period, float onSeconds, float dim)
        {
            if (period <= 0f)
                return 1f;
            return Mathf.Repeat(Mathf.Max(0f, secondsSincePlaced), period) < onSeconds ? 1f : dim;
        }

        public static Color CapColour(Color team, float light) => new Color(team.r * light, team.g * light, team.b * light, team.a);
    }
}
