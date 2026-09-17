using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Where a mine actually lands (A9, Tudor 2026-09-17 evening: mines can be placed anywhere within 2 m of the
    /// player, not only at their feet - Task 3's own comment on MineAbility.TryBuildCast used to say the opposite;
    /// that spec changed). Clamps the player's aim point on the floor to a maximum horizontal distance from the
    /// player - identical in spirit to TeleportAbility's own private ClampToRange, pulled out pure and public here so
    /// MineAbility can share the idea (and so "does a 5 m aim clamp to 2 m" is provable without a scene).
    ///
    /// HEIGHT IS UNTOUCHED. This only ever moves the point in XZ; the caller resolves the real landing height
    /// afterwards with GroundSnap, the same as every other placement in this codebase.
    /// </summary>
    public static class MinePlacementRule
    {
        // Not a tuning value: below this the aim point is close enough to the player that "the direction toward it"
        // stops meaning anything (dividing by ~0 to normalise it would blow up) - the same guard
        // BlinkDestinationSearch and TeleportAbility.ClampToRange both use for an identical degenerate case.
        private const float MinDirectionSqrMagnitude = 0.0001f;

        /// <summary>The aim point unchanged if it is already within <paramref name="range"/> of the player (or the
        /// player is aiming at their own feet - see class comment); otherwise the point exactly <paramref
        /// name="range"/> metres from the player, in the aim point's own horizontal direction.</summary>
        public static Vector3 ClampToRange(Vector3 playerPosition, Vector3 aimPoint, float range)
        {
            Vector3 originXZ = new Vector3(playerPosition.x, 0f, playerPosition.z);
            Vector3 aimXZ = new Vector3(aimPoint.x, 0f, aimPoint.z);
            Vector3 toAim = aimXZ - originXZ;

            if (toAim.sqrMagnitude <= MinDirectionSqrMagnitude || toAim.magnitude <= range)
                return aimPoint;

            Vector3 clampedXZ = originXZ + toAim.normalized * range;
            return new Vector3(clampedXZ.x, aimPoint.y, clampedXZ.z);
        }
    }
}
