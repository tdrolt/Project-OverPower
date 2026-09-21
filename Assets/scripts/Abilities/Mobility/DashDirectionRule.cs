using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Which direction a dash actually travels. Tudor, 2026-09-21: "make the dash go towards the
    /// player movement direction (like using wasd and where the player is currently moving to dash
    /// that way) and keep the blink at cursor location" - reversing his own 2026-09-13 call that a
    /// dash always aims at the cursor, WASD held or not (see DashAbility's class comment for that
    /// earlier decision, now superseded).
    ///
    /// Pulled out of DashAbility.TryBuildCast into its own pure class so the decision is tested
    /// without a scene - the same reasoning as every other *Rule class in this codebase
    /// (OutOfArenaRule, DisplacementSweepRule, RemoteSnapRule, ...).
    /// </summary>
    public static class DashDirectionRule
    {
        /// <summary>
        /// moveDirection: CastContext.MoveDirection - camera-relative WASD, zero when standing still,
        /// already clamped to length <= 1 by AbilityRunner.BuildContext. Flattened here defensively
        /// even though it should already be flat.
        ///
        /// towardCursor: CastContext.TargetPoint - CastContext.Origin, raw (NOT yet flattened or
        /// normalised) - this method does both, exactly like TryBuildCast used to do inline.
        ///
        /// aimDirection: CastContext.AimDirection - already flat and normalised (PlayerAim's own
        /// contract), used only once towardCursor is too short to mean anything.
        ///
        /// Moving wins outright, regardless of where the cursor sits: past movementThreshold, the
        /// result is moveDirection, flattened and normalised. Standing still (at or below the
        /// threshold - a tiny stick drift counts as standing still) keeps 2026-09-13's original rule:
        /// toward the cursor, or the aim direction once the cursor is on top of the player (or too
        /// close for "toward it" to mean anything, inside cursorOnSelfThreshold).
        ///
        /// Always returns a flat, normalised, non-zero direction: aimDirection is always a real
        /// facing, so the fallback chain never has to normalise a zero vector.
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
