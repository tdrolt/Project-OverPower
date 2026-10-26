using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Whether a player is being healed, read from the health every client already shows in the overhead bar. A rise counts only
    /// while the player was alive before and after it and came from a living health; a rise that lands together with a jump of the body
    /// is a respawn or a fresh round putting them back at their spawn, not healing. Every reset path teleports and refills in one frame,
    /// and another player's update carries both, so the two arrive together.</summary>
    public static class HealRule
    {
        // Regeneration adds a fraction of a point per frame; anything smaller is float noise from the network.
        private const float SmallestRise = 0.001f;
        private const float GoldenAngleDegrees = 137.50776f;

        public static bool IsHealRise(float previousHealth, float health, bool wasAlive, bool isAlive, float jumpedMetres, float spawnJumpMetres) =>
            wasAlive && isAlive && previousHealth > 0f && health > previousHealth + SmallestRise && jumpedMetres <= spawnJumpMetres;

        /// <summary>The tail bridges the gaps between another player's health updates and lets one big rise (a health pack) show.</summary>
        public static bool IsHealing(bool isAlive, float secondsSinceLastRise, float tailSeconds) =>
            isAlive && secondsSinceLastRise <= tailSeconds;

        /// <summary>Plus sign <paramref name="index"/> of <paramref name="count"/>: its spot from the feet (on a ring of
        /// <paramref name="radius"/>, each sign a golden angle round from the last, so they never stack) and its strength 0..1, which is 0
        /// where it appears and where it ends so it grows in and fades out. The signs are spread over one rise, so they show at every
        /// height the moment healing starts.</summary>
        public static void PlaceSpark(int index, int count, float time, float riseSeconds, float radius, float startHeight, float riseMetres,
                                      out Vector3 local, out float strength)
        {
            float phase = Mathf.Repeat(time / Mathf.Max(0.01f, riseSeconds) + (float)index / Mathf.Max(1, count), 1f);
            float angle = index * GoldenAngleDegrees * Mathf.Deg2Rad;
            local = new Vector3(Mathf.Sin(angle) * radius, startHeight + phase * riseMetres, Mathf.Cos(angle) * radius);
            strength = Mathf.Sin(phase * Mathf.PI);
        }
    }
}
