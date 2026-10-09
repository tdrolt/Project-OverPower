using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Whether THIS moment should land an electric fence hit on ONE target, as plain C# provable
    /// without a scene. One instance per target, owned by ElectricFence (victim-side and every-client:
    /// each client's copy only matters for the targets that client can actually damage).
    ///
    /// TWO WAYS TO GET HIT: standing in the band (within Ring Thickness / 2 of Radius), or CROSSING
    /// it: the distance from the ring's centre was on one side of Radius last sample and is on the
    /// other now. A flat in-band sample misses a fast mover (a dash crosses the band in a few physics
    /// frames, and samples can straddle it entirely); remembering the side catches that pass.
    ///
    /// ONE COOLDOWN SHARED BY BOTH: standing on the ring's edge cannot be hit every frame, and a
    /// crossing that lingers in the band is not hit twice.
    ///
    /// NO CROSSING ON THE FIRST SAMPLE: there is no last side yet, so a target merely existing
    /// somewhere when the fence spawns must not read as having crossed; only the in-band rule applies.
    /// </summary>
    public sealed class FenceCrossingState
    {
        private readonly float radius;
        private readonly float halfThickness;
        private readonly float perTargetCooldownSeconds;

        private bool hasSample;
        private bool wasOutside;
        private float cooldownUntil = float.NegativeInfinity;

        public FenceCrossingState(float radius, float ringThickness, float perTargetCooldownSeconds)
        {
            this.radius = radius;
            halfThickness = ringThickness * 0.5f;
            this.perTargetCooldownSeconds = perTargetCooldownSeconds;
        }

        /// <summary>
        /// Call once per tick with the target's flat distance from the fence centre and the caller's
        /// Time.time (a per-target cooldown is real seconds on whichever client asks, never a
        /// networked clock). True when this tick should apply a hit.
        /// </summary>
        public bool ShouldHit(float distance, float now)
        {
            bool outside = distance > radius;
            bool crossed = hasSample && outside != wasOutside;
            bool inBand = Mathf.Abs(distance - radius) <= halfThickness;

            hasSample = true;
            wasOutside = outside;

            if (!inBand && !crossed)
                return false;

            if (now < cooldownUntil)
                return false;

            cooldownUntil = now + perTargetCooldownSeconds;
            return true;
        }

        /// <summary>
        /// Forgets the recorded side, so a target that died while tracked and respawns elsewhere does
        /// not read as having crossed between its last living position and the new one; the next call
        /// can only hit through the in-band rule. The cooldown is untouched: this forgets a stale
        /// POSITION, it must not grant a free hit.
        /// </summary>
        public void Reset()
        {
            hasSample = false;
            wasOutside = false;
        }
    }
}
