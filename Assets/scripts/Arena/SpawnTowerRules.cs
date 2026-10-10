using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Sizes of the Dominion spawn tower's look. Its solid footprint (the capsule collider) is never changed, so nothing here may reach past it.</summary>
    public static class SpawnTowerRules
    {
        public const int MaxColumns = 6;

        /// <summary>A requested radius, kept between nothing and the footprint's radius.</summary>
        public static float WithinCollider(float radius, float colliderRadius) => Mathf.Clamp(radius, 0f, colliderRadius);

        /// <summary>Where a column stands: on the body's corner, pulled in only as far as needed to keep its cap inside the footprint.</summary>
        public static float ColumnRingRadius(float bodyRadius, float capRadius, float colliderRadius) =>
            Mathf.Max(0f, Mathf.Min(bodyRadius, colliderRadius - capRadius));

        /// <summary>One column per corner at most, one at least.</summary>
        public static int ColumnCount(int requested) => Mathf.Clamp(requested, 1, MaxColumns);
    }
}
