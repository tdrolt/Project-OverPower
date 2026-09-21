using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The pure geometry behind the raybeam's three converging shots - where each beam starts and
    /// which way it aims, and whether a given beam actually passes close enough to strike a point.
    /// Pulled out of RaybeamAbility (a MonoBehaviour, untestable in edit mode without a scene)
    /// exactly the way BeamResolver sits apart from Hitscan: "three beams converge at a fixed range
    /// along the shooter's own aim direction" (2026-09-20 rework - see RaybeamAbility's own class
    /// comment, "THE SPREAD") is the one claim in this ability that a test can pin down instead of
    /// eyeballing in Play mode. Before that rework the two outer beams chased the cursor's own exact
    /// depth instead; that worked at any range for a pixel-perfect click, but a real player cannot
    /// place a ground cursor pixel-perfectly at distance, so the old ~3m working range was the
    /// cursor's own depth precision running out, not a limit of this geometry.
    ///
    /// Every method here is a plain function of its arguments - no Physics call, no MonoBehaviour
    /// state - so every client that runs it against the same synced Origin/Direction/Point agrees,
    /// which matters because raybeam damage is victim-side (see RaybeamAbility's class comment).
    /// </summary>
    public static class RaybeamGeometry
    {
        /// <summary>
        /// The three beam origins, addendum wording exactly: "Origin ± right x beamOriginSpacing,
        /// plus the centre". Right is derived from Direction rather than read off the caster's own
        /// transform.right, because Direction is the one value that actually crosses the wire in the
        /// cast payload - every client must compute the identical perpendicular from the identical
        /// input, and a player's transform can be a frame stale or (for a dead/respawning caster)
        /// meaningless by the time a remote client processes the cast.
        /// </summary>
        public static void BeamOrigins(Vector3 origin, Vector3 direction, float spacing,
                                       out Vector3 left, out Vector3 centre, out Vector3 right)
        {
            Vector3 perpendicular = FlatPerpendicular(direction);

            centre = origin;
            left = origin - perpendicular * spacing;
            right = origin + perpendicular * spacing;
        }

        /// <summary>
        /// The direction ONE beam fires along: from its own origin toward the shared convergence
        /// Point, never the caster's raw aim direction - only the centre beam's origin actually sits
        /// on that line, so the two outer beams must re-aim inward to converge at all. Falls back to
        /// fallbackDirection when a beam's origin already sits on Point (a degenerate cast with
        /// Point clamped back onto Origin itself), so a beam never fires along a zero vector.
        /// </summary>
        public static Vector3 AimFromOriginToPoint(Vector3 beamOrigin, Vector3 point, Vector3 fallbackDirection)
        {
            Vector3 toPoint = point - beamOrigin;
            return toPoint.sqrMagnitude > 0.0001f ? toPoint.normalized : fallbackDirection.normalized;
        }

        /// <summary>
        /// True when a beam of the given radius, travelling from beamOrigin along beamDirection for
        /// up to range metres, passes close enough to strike targetPosition - the same "distance from
        /// a point to a bounded ray" test a SphereCast performs, done here without Physics so the
        /// convergence claim (every beam lands at Point; only the near beam lands elsewhere) can be
        /// checked with a test instead of a play-mode measurement.
        /// </summary>
        public static bool BeamCrosses(Vector3 beamOrigin, Vector3 beamDirection, float range,
                                       float beamRadius, Vector3 targetPosition)
        {
            Vector3 direction = beamDirection.sqrMagnitude > 0.0001f ? beamDirection.normalized : Vector3.forward;
            Vector3 toTarget = targetPosition - beamOrigin;
            float along = Vector3.Dot(toTarget, direction);

            // Behind the origin or past where the beam ends - a sphere sweep can still clip a target
            // up to one radius before/after those bounds, so the bounds themselves are widened by it.
            if (along < -beamRadius || along > range + beamRadius)
                return false;

            Vector3 closestPointOnBeam = beamOrigin + direction * Mathf.Clamp(along, 0f, range);
            return Vector3.Distance(closestPointOnBeam, targetPosition) <= beamRadius;
        }

        /// <summary>
        /// Where the two outer beams are aimed to cross the centre beam's line: a fixed distance
        /// along the shooter's own aim direction, clamped to beamRange so a beam is never asked to
        /// converge past where it stops existing (RaybeamAbility's beamConvergenceRange tooltip
        /// explains why this is fixed rather than the cursor's own depth). Pulled out of
        /// RaybeamAbility.ExecuteCast, which used to inline this one formula, so the convergence
        /// point itself has a test independent of the three-beams-cross-it cases above.
        /// </summary>
        public static Vector3 ConvergencePoint(Vector3 origin, Vector3 aimDirection, float convergenceRange, float beamRange)
        {
            return origin + aimDirection * Mathf.Min(convergenceRange, beamRange);
        }

        /// <summary>Perpendicular to Direction, flattened onto the ground plane and normalised -
        /// Vector3.right when Direction has no flat component at all (aiming straight up or down,
        /// which should not happen for a ground-based cast, but must never divide by zero).</summary>
        private static Vector3 FlatPerpendicular(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude <= 0.0001f)
                return Vector3.right;

            return Vector3.Cross(Vector3.up, flat.normalized);
        }
    }
}
