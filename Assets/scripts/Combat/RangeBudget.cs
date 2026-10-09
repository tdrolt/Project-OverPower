using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// How far one projectile is still allowed to travel: every projectile owns one, spends it a
    /// frame-step at a time, and dies the moment it runs out. Without a range cap and lifetime every
    /// missed shot stayed in the scene forever on every client (a real cause of playtest freezes).
    ///
    /// Consume returns the distance actually allowed, so the projectile stops EXACTLY at Max Range: a
    /// later weapon scales its damage with Fraction, so overshooting would quietly deal more than its
    /// stat block caps it at.
    ///
    /// Plain C# with no Unity types in its API, unit tested without an engine or a scene.
    /// </summary>
    public sealed class RangeBudget
    {
        /// <summary>
        /// How close to Max Range counts as having reached it: a float-precision tolerance, not a
        /// tuning number, and deliberately not smaller. Summing a few hundred small steps lands up to
        /// ~1e-4 short of the exact sum, so without it a projectile could reach the end of its range
        /// and report IsSpent == false forever, the never-despawning object this class exists to
        /// prevent. DamageResolver carries the same kind of tolerance.
        /// </summary>
        private const float ReachedTolerance = 0.001f;

        /// <summary>Metres travelled so far. Never exceeds MaxRange.</summary>
        public float Travelled { get; private set; }

        /// <summary>Metres this projectile was ever allowed. Clamped to zero or more, so a negative
        /// Max Range gives a projectile that expires instantly rather than one that flies forever.</summary>
        public float MaxRange { get; }

        /// <summary>
        /// How far along its flight the projectile is, 0 at the muzzle and 1 when spent; the hook a
        /// distance-scaling weapon reads (nothing uses it yet). A zero MaxRange reports 1 rather than
        /// dividing by zero: a NaN here would show up as a projectile that never despawns.
        /// </summary>
        public float Fraction => MaxRange <= 0f ? 1f : Mathf.Clamp01(Travelled / MaxRange);

        public bool IsSpent => MaxRange - Travelled <= ReachedTolerance;

        public RangeBudget(float maxRange)
        {
            MaxRange = Mathf.Max(0f, maxRange);
            Travelled = 0f;
        }

        /// <summary>
        /// Books one step of travel and returns how much was actually allowed: the whole step while
        /// there is room, only the remainder on the step that reaches Max Range, zero once spent.
        /// Callers move by the returned distance, never the one they asked for. A negative distance is
        /// ignored: a projectile must not buy extra range by travelling backwards.
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

            // Snap the last sliver away (see ReachedTolerance): accumulated rounding error would
            // otherwise leave the projectile a hair short of its range and IsSpent never true.
            if (MaxRange - Travelled <= ReachedTolerance)
                Travelled = MaxRange;

            return allowed;
        }

        /// <summary>
        /// Gives back distance Consume booked but the projectile never travelled. ProjectileMotor.Step
        /// charges the WHOLE nominal step up front on a step that ends in KeepFlying (a bounce, a
        /// pierce), then moves only hit.distance if a hit cut it short; without the refund a lower frame
        /// rate (bigger nominal step) forfeits more range per hit, so the identical bounced path would
        /// travel a different total distance per client. Clamped so Travelled never goes negative: a
        /// refund must not buy extra range.
        /// </summary>
        public void Refund(float distance)
        {
            if (distance <= 0f)
                return;

            Travelled = Mathf.Max(0f, Travelled - distance);
        }
    }
}
