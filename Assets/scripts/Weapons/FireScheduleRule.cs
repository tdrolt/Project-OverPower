namespace Overpower.Weapons
{
    /// <summary>
    /// Decides nextFireTime's next value for one WeaponFiring.TryFire tick. Pulled out on its own
    /// (Task 2.6 review follow-up) so both the frame-rate-independence trick and the bug found in
    /// it are exhaustively edit-mode tested without a live player.
    ///
    /// Carrying the schedule forward (rather than always rebasing off Time.time) is what lets a
    /// fast weapon's fire-rate buff actually speed it up: TryFire only ever runs once per rendered
    /// frame, so re-basing every shot silently capped the buffed cadence at "once a frame" too -
    /// see WeaponFiring.TryFire's own comment for the full story.
    ///
    /// The bug this class fixes: a CastGate interruption (silenced, stunned, dead) used to leave
    /// nextFireTime frozen at whatever it was scheduled to before the block started, for however
    /// long the block lasted. If the block lifted less than one interval before "now" happened to
    /// land relative to that STALE deadline, the carry-over branch fired anyway on the very first
    /// unblocked tick, scheduling the next shot only a sliver of an interval later - a real, if
    /// narrow, catch-up shot about one frame after the interruption ended. The fix: every BLOCKED
    /// tick also runs through here, clamping the deadline up to at least "now" so it can never fall
    /// further behind while nothing is actually being fired - by the time the block lifts, the
    /// deadline is close enough to "now" that the very next shot lands roughly one full interval
    /// later, matching what a freshly re-pressed trigger would get, off by at most one blocked
    /// tick's own granularity rather than nearly a whole interval.
    /// </summary>
    public static class FireScheduleRule
    {
        /// <param name="previousNextFireTime">nextFireTime's value going into this tick.</param>
        /// <param name="now">Time.time this tick.</param>
        /// <param name="interval">The effective fire interval for this tick
        /// (weapon.FireInterval / fireRateMultiplier) - passed in fresh every tick so a fire-rate
        /// buff that changes mid-hold takes effect immediately rather than waiting for the next
        /// full cycle.</param>
        /// <param name="triggerHeldContinuously">True for the ordinary held-trigger firing path -
        /// Update's PrimaryHeld poll calls TryFire once every frame for a weapon that cannot
        /// charge. False for a fire that is not part of that per-frame cadence; a charge weapon's
        /// single release (HandlePrimaryReleased) is the one caller that passes false today. Only a
        /// continuously-held trigger's schedule is ever carried forward - anything else always
        /// rebases off now, exactly like a freshly pressed trigger, regardless of how the numbers
        /// alone might otherwise read.</param>
        /// <param name="blockedThisTick">True when CastGate refused this tick (dead, stunned or
        /// silenced) - no shot fires, but the deadline still must not fall behind now, or the FIRST
        /// tick after the block lifts wrongly reads as "still mid-cadence" and carries over into a
        /// too-soon second shot. triggerHeldContinuously is not consulted in this case.</param>
        public static float NextFireTime(float previousNextFireTime, float now, float interval,
                                         bool triggerHeldContinuously, bool blockedThisTick)
        {
            if (blockedThisTick)
                return previousNextFireTime > now ? previousNextFireTime : now;

            if (triggerHeldContinuously && previousNextFireTime >= now - interval)
                return previousNextFireTime + interval;

            return now + interval;
        }
    }
}
