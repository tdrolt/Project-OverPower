using UnityEngine;
using Overpower.Arena;

namespace Overpower.Combat
{
    /// <summary>
    /// GDD p.29: a jersey barrier blocks walking but lets a dash, a zip pull, a blink or a portal cross it. A
    /// crossing move sweeps with the Barrier layer excluded (PlayerDisplacement.StartMove), so it can end up
    /// overlapping the barrier's own footprint when it stops mid-crossing; this rule says which way to nudge the
    /// body clear.
    ///
    /// "The side you're already on" (Amendment 1, Decision 23): the exit is picked by which side of the barrier's
    /// own middle line the body's centre already sits on when it stopped - not which way the move started, or which
    /// end is nearer along the barrier's length. That is the smallest correction there is: a body already past the
    /// middle finishes the crossing it was mostly through, and a body still short of the middle is nudged back the
    /// way it came - never a surprise sideways step along the barrier's own run. Exactly on the middle line (within
    /// 1 mm), the direction the move itself was heading decides it, so a dead-centre stop still resolves the same
    /// way a moment earlier or later would have.
    /// </summary>
    public static class BarrierCrossingRule
    {
        private const float OnMiddleLineToleranceMetres = 0.001f;

        /// <summary>
        /// The two candidate points that clear <paramref name="barrier"/>'s footprint by <paramref name="radius"/>
        /// plus a skin width, keeping <paramref name="centre"/>'s own position along the barrier's length exactly as
        /// it is. <paramref name="first"/> is the side to try first (the side the centre is already on, or the side
        /// <paramref name="moveDir"/> was heading if it is exactly on the line); <paramref name="other"/> is the far
        /// side, tried only if the first doesn't fit.
        /// </summary>
        public static void ExitPoints(Vector2 centre, Vector2 moveDir, float radius, in BoxFootprint barrier,
                                       out Vector2 first, out Vector2 other)
        {
            Vector2 offset = centre - barrier.Centre;
            float s = Vector2.Dot(offset, barrier.Across);

            float side = Mathf.Abs(s) < OnMiddleLineToleranceMetres
                ? SignOf(Vector2.Dot(moveDir, barrier.Across))
                : Mathf.Sign(s);

            float clearance = barrier.HalfWidth + radius + DisplacementSweepRule.SkinMetres;
            first = centre + barrier.Across * (side * clearance - s);
            other = centre + barrier.Across * (-side * clearance - s);
        }

        // Mathf.Sign(0) returns 1, a fine, deterministic default for a move running exactly along the barrier's own
        // length (its Across component is 0): "the side it was heading" then falls back to the same side every
        // time, rather than an arbitrary or undefined step.
        private static float SignOf(float value) => value >= 0f ? 1f : -1f;
    }
}
