using System;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Columns per tier, where each column stands, and how big each column is.</summary>
    public static class TowerLookRules
    {
        public const int MaxColumns = 4;

        /// <summary>Round towers with as many columns as their tiers: Tier I (a capital) shows 1, Tier IV (the centre)
        /// 4 - the numeral the minimap prints on the zone's bubble. The one line to change if the count goes the other
        /// way (Open #2). Out-of-range tiers are clamped, never thrown.</summary>
        public static int ColumnsForTier(int tier) => Math.Max(1, Math.Min(MaxColumns, tier));

        /// <summary>Columns stand evenly round the tower; the first faces the tower's own front (+Z).</summary>
        public static float ColumnYawDegrees(int index, int count) => 360f * index / Math.Max(1, count);

        /// <summary>Only the capital's single column is big; every other tier's columns share the one normal size.
        /// Clamped the same way ColumnsForTier is, so an out-of-range tier still reads as its nearest real one.</summary>
        public static float ColumnRadius(int tier, float columnRadius, float capitalColumnRadius) =>
            ColumnsForTier(tier) == 1 ? capitalColumnRadius : columnRadius;

        /// <summary>The ring a column of this radius stands on: its outer edge is tangent to the tower's collider,
        /// never past it, because columns must never add cover over the plain round tower (Decision 6).</summary>
        public static float ColumnRingRadius(float columnRadius, float towerRadius) => towerRadius - columnRadius;

        /// <summary>The colour brightened by glow without changing its hue on screen. The tonemapper squeezes every bright channel
        /// toward the same level, so a weaker channel stops at its share of the colour at full strength (or purple's red would catch
        /// up with its blue and turn it pink), while a channel close to the brightest grows with it (or white's blue alone would glow
        /// and turn it lavender, cyan's blue alone sky blue).</summary>
        public static Color GlowKeepingHue(Color colour, float glow)
        {
            float peak = colour.maxColorComponent;
            if (peak <= 0f)
                return colour;
            return new Color(Channel(colour.r, peak, glow), Channel(colour.g, peak, glow), Channel(colour.b, peak, glow), colour.a);
        }

        // A channel this close to the brightest (a share of it) starts to grow with it, and from the second share on grows fully.
        private const float NearPeakFrom = 0.85f, NearPeakFull = 0.92f;

        private static float Channel(float value, float peak, float glow)
        {
            float capped = Math.Min(value * glow, Math.Max(value, value / peak));
            float near = Mathf.Clamp01((value / peak - NearPeakFrom) / (NearPeakFull - NearPeakFrom));
            return capped + (value * glow - capped) * near;
        }
    }
}
