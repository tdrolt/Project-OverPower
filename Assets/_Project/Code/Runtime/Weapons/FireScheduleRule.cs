namespace Overpower.Weapons
{
    /// <summary>
    /// Decides nextFireTime's next value for one WeaponFiring.TryFire tick, on its own so the schedule maths is edit-mode testable.
    /// A held trigger carries the previous deadline forward instead of rebasing off Time.time: TryFire runs once per rendered frame,
    /// so rebasing every shot capped a fire-rate buff at once a frame. A blocked tick (silenced, stunned, dead) still clamps the deadline
    /// up to now, or the first tick after the block lifts reads as mid-cadence and fires a too-soon catch-up shot.
    /// </summary>
    public static class FireScheduleRule
    {
        /// <param name="interval">Effective interval (weapon.FireInterval / fireRateMultiplier), passed in fresh every tick so a mid-hold buff applies at once.</param>
        /// <param name="triggerHeldContinuously">True only for the per-frame held-trigger path; a charge weapon's single release passes false. Anything
        /// but a continuous hold rebases off now, like a freshly pressed trigger.</param>
        /// <param name="blockedThisTick">True when CastGate refused this tick: no shot, but the deadline still must not fall behind now.
        /// triggerHeldContinuously is not consulted.</param>
        public static float NextFireTime(float previousNextFireTime, float now, float interval,
                                         bool triggerHeldContinuously, bool blockedThisTick)
        {
            if (blockedThisTick)
                return previousNextFireTime > now ? previousNextFireTime : now;

            if (triggerHeldContinuously && previousNextFireTime >= now - interval)
                return previousNextFireTime + interval;

            return now + interval;
        }

        /// <summary>
        /// NextFireTime's triggerHeldContinuously for one TryFire call. A fresh click is not a continuing hold even for a
        /// non-charge weapon: treating it as one carried the old deadline forward and let the next held-fire tick fire almost at once.
        /// </summary>
        /// <param name="heldLastFrame">True only when the trigger was also down on the preceding Update tick, never on the press that starts a hold.</param>
        /// <param name="weaponCanCharge">A charge weapon's one-off release always rebases.</param>
        public static bool IsContinuingHold(bool heldLastFrame, bool weaponCanCharge)
        {
            return heldLastFrame && !weaponCanCharge;
        }
    }
}
