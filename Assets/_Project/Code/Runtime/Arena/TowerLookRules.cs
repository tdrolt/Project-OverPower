using System;
using Overpower.Match;
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

        /// <summary>What a zone's tower and ring show. A spawn can never be taken, so an enemy standing in it or a drain must not make it
        /// pulse or flicker as if it were under attack: it keeps its owner's steady colour. Every other zone shows its state as it is.</summary>
        public static CaptureRingState Shown(CaptureRingState state, bool spawn) =>
            spawn ? new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, state.OutlineTeam, TerritoryMap.Neutral, false, state.OutOfPlay)
                  : state;

        /// <summary>The colour brightened by glow keeping its hue on screen, in light and under the fog. Up to full strength the glow
        /// scales the whole colour; past it the brightest channel keeps growing (what the bloom picks up) while a weaker one grows only
        /// by the glow to the power WeakChannelGrowth, and no further once the brightest reaches the tonemapper's white. In light the
        /// tonemapper squeezes bright channels together (a weaker one grown in full turns orange yellow, purple pink); the fog dims
        /// them apart again (one held back turns orange red, purple blue): the power sits between. A channel close to the brightest
        /// grows with it in full, or white would turn lavender and cyan sky blue.</summary>
        public static Color GlowKeepingHue(Color colour, float glow)
        {
            float peak = colour.maxColorComponent;
            if (peak <= 0f)
                return colour;
            if (glow <= 0f)
                return new Color(0f, 0f, 0f, colour.a); // the shares below would be 0/0, and a NaN glow spreads as blocks under the Bloom
            float whole = Math.Min(glow, Math.Max(1f, 1f / peak));
            float past = Math.Min(glow / whole, Math.Max(1f, ScreenWhite / (peak * whole)));
            float weak = whole * Mathf.Pow(past, WeakChannelGrowth);
            return new Color(Channel(colour.r, peak, glow, weak), Channel(colour.g, peak, glow, weak), Channel(colour.b, peak, glow, weak), colour.a);
        }

        // Measured on the project's own screen (TowerGlowTests): 0.35 keeps every team's glow within 8 degrees of its hue in light and fog.
        private const float WeakChannelGrowth = 0.35f;

        // URP's Neutral tonemapper reaches white at 5.3 linear; a brighter channel only feeds the bloom.
        private static readonly float ScreenWhite = Mathf.LinearToGammaSpace(5.3f);

        // A channel this close to the brightest (a share of it) starts to grow with it, and from the second share on grows fully.
        private const float NearPeakFrom = 0.85f, NearPeakFull = 0.92f;

        private static float Channel(float value, float peak, float glow, float weak)
        {
            float near = Mathf.Clamp01((value / peak - NearPeakFrom) / (NearPeakFull - NearPeakFrom));
            return value * Mathf.Lerp(weak, glow, near);
        }
    }
}
