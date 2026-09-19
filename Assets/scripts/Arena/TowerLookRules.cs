using System;

namespace Overpower.Arena
{
    /// <summary>Arena rebuild: columns per tier, where each column stands, and (Tudor's answer, 2026-09-19) how big
    /// each column is.</summary>
    public static class TowerLookRules
    {
        public const int MaxColumns = 4;

        /// <summary>Tudor, 2026-09-18: "round towers that each have as many columns as their tiers". Tier I (a capital)
        /// shows 1 column, Tier IV (the centre) 4 - the numeral the minimap prints on the zone's bubble. The one line
        /// to change if he counts the other way (plan Open #2). Out-of-range tiers are clamped, never thrown.</summary>
        public static int ColumnsForTier(int tier) => Math.Max(1, Math.Min(MaxColumns, tier));

        /// <summary>Columns stand evenly round the tower; the first faces the tower's own front (+Z).</summary>
        public static float ColumnYawDegrees(int index, int count) => 360f * index / Math.Max(1, count);

        /// <summary>Tudor's answer, 2026-09-19: "the capital gets 1 big column, the tier 2 two smaller columns, tier
        /// 3 and 4 columns of the same size and the same number as their tier 3" - so only the capital's single
        /// column is big; every other tier's columns share the one normal size. Clamped the same way
        /// ColumnsForTier is, so an out-of-range tier still reads as its nearest real one.</summary>
        public static float ColumnRadius(int tier, float columnRadius, float capitalColumnRadius) =>
            ColumnsForTier(tier) == 1 ? capitalColumnRadius : columnRadius;

        /// <summary>The ring a column of this radius stands on, kept so its outer edge never passes the tower's own
        /// collider radius - columns must never add cover over the plain round tower (Decision 6). At the exact
        /// value returned, the column's outer edge is tangent to the tower's collider, never past it.</summary>
        public static float ColumnRingRadius(float columnRadius, float towerRadius) => towerRadius - columnRadius;
    }
}
