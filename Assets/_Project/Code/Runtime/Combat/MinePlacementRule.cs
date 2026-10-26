using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Where a mine actually lands (A9): clamps the player's aim point on the floor to a maximum
    /// horizontal distance from the player (mines go anywhere within range, not only at the feet).
    /// Same idea as TeleportAbility's private ClampToRange, public here for MineAbility and provable
    /// without a scene. Only XZ moves; the caller resolves landing height afterwards with GroundSnap.
    /// </summary>
    public static class MinePlacementRule
    {
        // Not a tuning value: below this the direction toward the aim point is meaningless (normalising
        // would divide by ~0); the same guard as BlinkDestinationSearch and TeleportAbility.ClampToRange.
        private const float MinDirectionSqrMagnitude = 0.0001f;

        /// <summary>The aim point unchanged if it is already within <paramref name="range"/> of the player (or the
        /// player is aiming at their own feet); otherwise the point exactly <paramref name="range"/> metres from the
        /// player, in the aim point's own horizontal direction.</summary>
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
