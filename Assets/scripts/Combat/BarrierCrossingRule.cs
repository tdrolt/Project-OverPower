using UnityEngine;
using Overpower.Arena;

namespace Overpower.Combat
{
    /// <summary>
    /// GDD p.29: a jersey barrier blocks walking but lets a dash, a zip pull, a blink or a portal cross
    /// it. A crossing move sweeps with the Barrier layer excluded (PlayerDisplacement.StartMove), so it
    /// can stop mid-crossing overlapping the barrier's own footprint; this rule says which way to nudge
    /// the body clear.
    ///
    /// The exit is the side the body's centre already sits on, relative to the barrier's middle line
    /// (Decision 23) - not which way the move started, or which end is nearer along the barrier. That
    /// is the smallest correction: a body past the middle finishes the crossing, one short of it goes
    /// back the way it came, never a sideways step along the barrier. Exactly on the middle line
    /// (within the tolerance below) the move's own heading decides, so a dead-centre stop resolves the
    /// same way a moment earlier or later would have.
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
