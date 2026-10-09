namespace Overpower.UI
{
    /// <summary>
    /// How long the yellow "shield immunity" look lasts: the bar follows the Invulnerability bubble for exactly
    /// invincibleSeconds. Pure and unit tested rather than buried in PlayerHealth's Time.time bookkeeping. Every
    /// client (owner and remote alike) runs the identical clock off the identical trigger, the phase message every
    /// client receives; nothing here is owner-only knowledge.
    /// </summary>
    public sealed class ImmuneLookClock
    {
        private float until = float.NegativeInfinity;

        /// <summary>Starts (or restarts) the look from now, for `seconds` more. A non-positive
        /// duration shows nothing at all rather than turning it on for zero time and then instantly
        /// clearing it - see ZeroOrNegativeSecondsShowsNothing.</summary>
        public void Show(float now, float seconds) => until = seconds > 0f ? now + seconds : float.NegativeInfinity;

        /// <summary>Ends the look at once, whatever time is left on it.</summary>
        public void Clear() => until = float.NegativeInfinity;

        public bool IsOn(float now) => now < until;
    }
}
