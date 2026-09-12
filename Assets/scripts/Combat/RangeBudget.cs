using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// How far one projectile is still allowed to travel. Every projectile owns one, spends it a
    /// frame-step at a time, and dies the moment it runs out.
    ///
    /// This exists because the bullets it replaces had no range cap and no lifetime at all: every
    /// shot that missed stayed in the scene forever, on every client. In a nine-player match that
    /// is a few hundred permanent objects a minute, and it was a real cause of freezes in playtest.
    ///
    /// Consume returns the distance actually allowed rather than just reporting "spent", so the
    /// projectile stops EXACTLY at Max Range instead of somewhere past it. That precision is not
    /// cosmetic: a later weapon scales its damage linearly with Fraction up to +50%, so a
    /// projectile allowed to overshoot would quietly deal more than the damage its stat block caps
    /// it at.
    ///
    /// Plain C# with no Unity types in its API, for the same reason as the rest of Combat: it is
    /// unit tested without an engine, a scene or a play-mode run.
    /// </summary>
    public sealed class RangeBudget
    {
        /// <summary>
        /// How close to Max Range counts as having reached it - one millimetre.
        ///
        /// This is a float-precision tolerance, not a tuning number, and it is deliberately not
        /// smaller. A projectile consumes its budget in a few hundred small steps, and adding
        /// several hundred floats together lands the total up to roughly a ten-thousandth short of
        /// the exact sum. Without this, a projectile could reach the very end of its range and
        /// still report IsSpent == false forever, which is exactly the never-despawning object
        /// this class exists to prevent. A unit test caught it. DamageResolver carries the same
        /// kind of tolerance for the same reason.
        /// </summary>
        private const float ReachedTolerance = 0.001f;

        /// <summary>Metres travelled so far. Never exceeds MaxRange.</summary>
        public float Travelled { get; private set; }

        /// <summary>Metres this projectile was ever allowed. Clamped to zero or more, so a
        /// designer typing a negative Max Range gets a projectile that expires instantly rather
        /// than one that flies forever.</summary>
        public float MaxRange { get; }

        /// <summary>
        /// How far along its flight the projectile is, 0 at the muzzle and 1 when spent. This is
        /// the hook a distance-scaling weapon reads; nothing uses it yet.
        ///
        /// A zero MaxRange reports 1 rather than dividing by zero. A NaN escaping here would show
        /// up as a projectile that never despawns, which is a miserable thing to trace back to a
        /// division.
        /// </summary>
        public float Fraction => MaxRange <= 0f ? 1f : Mathf.Clamp01(Travelled / MaxRange);

        public bool IsSpent => MaxRange - Travelled <= ReachedTolerance;

        public RangeBudget(float maxRange)
        {
            MaxRange = Mathf.Max(0f, maxRange);
            Travelled = 0f;
        }

        /// <summary>
        /// Books one step of travel and returns how much of it was actually allowed - the whole
        /// step while there is room, only the remainder on the step that reaches Max Range, and
        /// zero once spent. Callers move by the returned distance, never by the one they asked for.
        ///
        /// A negative distance is ignored rather than winding the budget back: a projectile must
        /// not be able to buy itself extra range by travelling backwards.
        /// </summary>
        public float Consume(float distance)
        {
            if (distance <= 0f)
                return 0f;

            float remaining = MaxRange - Travelled;
            if (remaining <= ReachedTolerance)
            {
                Travelled = MaxRange; // Settle it exactly, so Fraction reports a clean 1.
                return 0f;
            }

            float allowed = Mathf.Min(distance, remaining);
            Travelled += allowed;

            // Snap the last sliver away. See ReachedTolerance: without this the accumulated
            // rounding error of hundreds of per-frame additions leaves the projectile a fraction
            // of a millimetre short of its range and IsSpent never becomes true.
            if (MaxRange - Travelled <= ReachedTolerance)
                Travelled = MaxRange;

            return allowed;
        }
    }
}
