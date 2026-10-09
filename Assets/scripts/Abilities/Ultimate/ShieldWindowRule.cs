namespace Overpower.Abilities
{
    /// <summary>What the Invulnerability shield's armed window shows right now.</summary>
    public readonly struct ShieldWindowView
    {
        public readonly bool RingShown;
        public readonly bool WastedShown;

        public ShieldWindowView(bool ringShown, bool wastedShown)
        {
            RingShown = ringShown;
            WastedShown = wastedShown;
        }
    }

    /// <summary>
    /// The pure rule (D20) for the Invulnerability shield's armed window on screen. No engine.
    /// A ring shows from the cast until the window ends. A hit that sets the shield off ends the window
    /// early: ring and "Wasted" are both gone (the shield sphere takes over). A window that runs out with
    /// no hit shows "Wasted" for a few seconds, then nothing.
    ///
    /// LAG RULE: a remote client times the window from when it heard the cast and hears the hit
    /// separately, so a hit that landed just inside the window can arrive just after it ends there. To keep
    /// that from flashing a false "Wasted", a remote copy waits a short grace after the window before
    /// showing "Wasted" (the caster's own copy knows the truth and passes 0), and a hit that still arrives
    /// after "Wasted" appeared removes it at once. The ring itself never outlives the window.
    /// </summary>
    public static class ShieldWindowRule
    {
        /// <param name="elapsed">Seconds since this client learned of the cast.</param>
        /// <param name="window">The armed window length.</param>
        /// <param name="triggered">A hit has set the shield off (sticky).</param>
        /// <param name="wastedSeconds">How long "Wasted" stays.</param>
        /// <param name="grace">Extra seconds before "Wasted" may show (0 on the caster's own copy).</param>
        public static ShieldWindowView View(float elapsed, float window, bool triggered, float wastedSeconds, float grace)
        {
            if (triggered || elapsed < 0f)
                return new ShieldWindowView(false, false);

            bool ring = elapsed < window;
            float wastedStart = window + grace;
            bool wasted = elapsed >= wastedStart && elapsed < wastedStart + wastedSeconds;
            return new ShieldWindowView(ring, wasted);
        }

        /// <summary>True once nothing more will ever show for this window: a hit set the shield off, or the ring
        /// and then "Wasted" (after the grace) have both run their course. Not finished during the grace, so a
        /// remote copy's "Wasted" can still start.</summary>
        public static bool IsFinished(float elapsed, float window, bool triggered, float wastedSeconds, float grace)
        {
            if (triggered)
                return true;
            return elapsed >= window + grace + wastedSeconds;
        }

        /// <summary>The armed ring is for the caster and their team always, and for everyone else only while the
        /// "Enemies see the armed ring" switch is on. An unknown team counts as not on the team.</summary>
        public static bool CanSee(bool viewerIsCaster, int viewerTeam, int casterTeam, bool enemiesSeeIt)
        {
            if (viewerIsCaster || enemiesSeeIt)
                return true;
            return viewerTeam >= 0 && viewerTeam == casterTeam;
        }
    }
}
