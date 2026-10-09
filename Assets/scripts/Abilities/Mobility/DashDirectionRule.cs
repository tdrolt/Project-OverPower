using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Which direction a dash actually travels: along the held WASD movement direction, else toward the cursor (the aim
    /// direction when the cursor is on top of the player). Blink keeps aiming at the cursor. A pure class so the decision
    /// is tested without a scene.
    /// </summary>
    public static class DashDirectionRule
    {
        /// <summary>
        /// moveDirection: CastContext.MoveDirection - camera-relative WASD, zero when standing still, already clamped to
        /// length <= 1; flattened here defensively. towardCursor: TargetPoint - Origin, raw (this method flattens and
        /// normalises). aimDirection: already flat and normalised (PlayerAim's contract), used only once towardCursor is
        /// too short to mean anything.
        ///
        /// Moving (past movementThreshold; a tiny stick drift counts as still) wins outright, wherever the cursor sits.
        /// Otherwise: toward the cursor, or the aim direction inside cursorOnSelfThreshold. Always returns a flat,
        /// normalised, non-zero direction: aimDirection is always a real facing, so no zero vector is normalised.
        /// </summary>
        public static Vector3 Choose(Vector3 moveDirection, Vector3 towardCursor, Vector3 aimDirection,
                                      float movementThreshold, float cursorOnSelfThreshold)
        {
            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude > movementThreshold * movementThreshold)
                return moveDirection.normalized;

            towardCursor.y = 0f;
            return towardCursor.sqrMagnitude > cursorOnSelfThreshold * cursorOnSelfThreshold
                ? towardCursor.normalized
                : aimDirection.normalized;
        }
    }
}
