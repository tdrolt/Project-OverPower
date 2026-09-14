using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Whether THIS moment should land an electric fence hit on ONE target - pulled out of
    /// ElectricFence's per-client FixedUpdate (Task 1.11b) so the rule is provable without a scene.
    /// One instance per target, owned by the fence itself (the fence is victim-side and every-client,
    /// so each client's own copy of this state only ever matters for the targets that client can
    /// actually damage - see ElectricFence's own class comment).
    ///
    /// TWO WAYS TO GET HIT: standing in the band (within Ring Thickness / 2 of Radius), or CROSSING
    /// it - the target's distance from the ring's centre was on one side of Radius last sample and is
    /// on the other side now. The crossing check exists because a flat "are you in the band right
    /// now" sample alone misses a fast mover: the Task 1.11 addendum's own example is an 18 m/s dash
    /// crossing a 1m band in about three physics frames, which a per-frame band check can straddle
    /// entirely if the sample points happen to land just inside and just outside it. Recording which
    /// side the target was on catches that pass even when no single sample ever fell inside the band.
    ///
    /// ONE COOLDOWN, SHARED BY BOTH TRIGGERS: a hit - by either rule - starts the same cooldown, so
    /// standing and vibrating right on the ring's edge cannot be hit every frame, and a crossing that
    /// happens to also linger in the band afterwards is not hit a second time for it.
    ///
    /// NO CROSSING ON THE FIRST SAMPLE: there is no "last side" to compare against yet, so the first
    /// call can only ever hit through the in-band rule - exactly right, since a target already
    /// standing in the band the moment the fence appears should be hit, but a target merely existing
    /// somewhere in the world when the fence spawns must not read as having "crossed" into whatever
    /// side it already happened to be on.
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
        /// Call once per tick with the target's current flat distance from the fence's own centre and
        /// the current time (the caller's own Time.time - a per-target cooldown is measured in real
        /// seconds on whichever client is asking, never a networked clock). Returns true exactly when
        /// this tick should apply a hit.
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
    }
}
