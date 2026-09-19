using UnityEngine;
using Overpower.Arena;

namespace Overpower.Abilities
{
    /// <summary>
    /// "Does a player fit here, and can I reach it" for every ability that puts the caster, or something the caster
    /// places, on a chosen spot (movement step 3): Blink's landing, a portal's placement and arrival, and (ability
    /// visuals step 3) a mine's placement. One copy, so those abilities cannot disagree about what blocked means.
    /// GroundProbe answers the other half, "is there floor here".
    /// </summary>
    public static class PlayerSpaceProbe
    {
        /// <summary>Height above the floor the path check looks along. Not a tuning value: below a crate's or a wall's
        /// top and above the arena floor's own bumps (0.33 m of relief), so it sees walls and crates but not ground.</summary>
        public const float KneeHeightMetres = 0.5f;

        /// <summary>The path check's thickness, so it can't slip through the hairline seam between two wall pieces.
        /// Not a tuning value.</summary>
        public const float PathProbeRadiusMetres = 0.1f;

        // One buffer, reused: these checks run on the caster's own client, one at a time, on the main thread.
        private static readonly Collider[] overlapBuffer = new Collider[16];

        /// <summary>The root height that stands a capsule on <paramref name="groundY"/>: its bottom (centre.y minus
        /// half its height, below the root) sits exactly on the floor. The derivation Blink has always used, in one
        /// place now that portal arrival needs it too.</summary>
        public static float RootHeightOnGround(float groundY, float capsuleCentreY, float capsuleHeight) =>
            groundY - (capsuleCentreY - capsuleHeight * 0.5f);

        /// <summary>The root position that stands this capsule on a ground point.</summary>
        public static Vector3 RootOnGround(CapsuleCollider capsule, Vector3 groundPoint) =>
            new Vector3(groundPoint.x,
                        RootHeightOnGround(groundPoint.y, capsule.center.y, capsule.height),
                        groundPoint.z);

        /// <summary>The floor point under a player's root: the capsule's own bottom.</summary>
        public static Vector3 FeetOf(CapsuleCollider capsule, Vector3 rootPosition) =>
            rootPosition + Vector3.up * (capsule.center.y - capsule.height * 0.5f);

        /// <summary>
        /// True when a player-sized capsule rooted at <paramref name="rootPosition"/> would overlap anything on
        /// <paramref name="mask"/> other than <paramref name="self"/>'s own colliders. Assumes the player prefab is not
        /// scaled and the capsule stands upright, the same simplification Blink has always made for this capsule.
        /// </summary>
        public static bool IsCapsuleBlocked(CapsuleCollider capsule, Vector3 rootPosition, int mask, Transform self)
        {
            Vector3 centre = rootPosition + capsule.center;
            float halfSegment = Mathf.Max(capsule.height * 0.5f - capsule.radius, 0f);
            int count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * halfSegment, centre - Vector3.up * halfSegment,
                capsule.radius, overlapBuffer, mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (self != null && overlapBuffer[i].transform.IsChildOf(self))
                    continue; // never blocked by the caster's own body

                return true;
            }

            return false;
        }

        /// <summary>Amendment 1 (arena step 4a): true when a player-sized capsule rooted here overlaps a jersey
        /// barrier. One name for the one check every barrier-aware caller needs (PlayerMotor's grounded check,
        /// PlayerDisplacement's Finish/Settle), so "is this player inside a barrier" is never re-spelled as its own
        /// IsCapsuleBlocked call with the mask typed out by hand.</summary>
        public static bool IsInsideBarrier(CapsuleCollider capsule, Vector3 rootPosition, Transform self) =>
            IsCapsuleBlocked(capsule, rootPosition, ArenaLayers.Barrier, self);

        /// <summary>
        /// True when nothing on the Building layer - a wall, a house, a crate, deployable cover - stands between two
        /// floor points, checked at knee height. Building only on purpose: another player standing in the way must not
        /// stop you placing something, and the floor itself is on Default.
        /// </summary>
        public static bool IsPathClear(Vector3 fromFeet, Vector3 toFeet)
        {
            Vector3 from = fromFeet + Vector3.up * KneeHeightMetres;
            Vector3 to = toFeet + Vector3.up * KneeHeightMetres;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
                return true;

            return !Physics.SphereCast(from, PathProbeRadiusMetres, delta / distance, out _, distance,
                LayerMask.GetMask("Building"), QueryTriggerInteraction.Ignore);
        }
    }
}
