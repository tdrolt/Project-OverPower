using UnityEngine;
using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>
    /// The Raybeam's mark: a ring of red spikes over an enemy the local player's beam left vulnerable, drawn round the Mark gun's pink
    /// diamond at the same anchor. The ring's hole is sized from the diamond, so the two never overlap and both colours always show.
    /// Pure; SpikeRingGraphic draws the spikes and PlayerHud sizes the ring.
    /// </summary>
    public static class RaybeamMarkRule
    {
        /// <summary>reportedOnEnemy/victimShielded: RespawnShieldRules.EffectReport's answer for this client's own effect on its copy of
        /// a living enemy. A stopped effect left nothing behind, so it gets no mark.</summary>
        public static bool ShowsOnEnemy(StatusKind kind, bool reportedOnEnemy, bool victimShielded) =>
            kind == StatusKind.Vulnerability && reportedOnEnemy && !victimShielded;

        /// <summary>Radius of the circle the spikes stand on. A spike's base is a straight chord, which dips inside that circle by the
        /// cosine of its half-angle, so the circle is pushed out until even the chord's middle keeps the gap clear of the diamond's points
        /// (a square of diamondSize turned 45 degrees).</summary>
        public static float InnerRadius(float diamondSize, float gap, int spikeCount, float spikeWidth)
        {
            float corner = Mathf.Max(0f, diamondSize) * Mathf.Sqrt(2f) * 0.5f;
            return (corner + Mathf.Max(0f, gap)) / Mathf.Cos(HalfBase(spikeCount, spikeWidth));
        }

        /// <summary>Spike <paramref name="index"/>'s triangle, centred on the mark: the first points straight up, the rest follow
        /// clockwise at even steps. spikeWidth is the share of each spike's slice its base fills, held to 0..1 so neighbours never cross.</summary>
        public static void Spike(int index, int spikeCount, float innerRadius, float length, float spikeWidth,
                                 out Vector2 left, out Vector2 tip, out Vector2 right)
        {
            int count = Mathf.Max(3, spikeCount);
            float centre = Mathf.PI * 0.5f - index * 2f * Mathf.PI / count;
            float half = HalfBase(count, spikeWidth);
            left = Polar(innerRadius, centre + half);
            right = Polar(innerRadius, centre - half);
            tip = Polar(innerRadius + Mathf.Max(0f, length), centre);
        }

        private static float HalfBase(int spikeCount, float spikeWidth) =>
            Mathf.PI * Mathf.Clamp01(spikeWidth) / Mathf.Max(3, spikeCount);

        private static Vector2 Polar(float radius, float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
}
