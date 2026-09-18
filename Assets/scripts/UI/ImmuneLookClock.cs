namespace Overpower.UI
{
    /// <summary>
    /// How long the yellow "shield immunity" look lasts, on its own - the bar follows the
    /// Invulnerability shield's bubble for exactly invincibleSeconds (Mark plan step 1, Decision 15),
    /// so the timing is pulled out into this pure class and unit tested instead of being buried in
    /// PlayerHealth's own Time.time bookkeeping. Sealed and stateless beyond the one field: every
    /// client (owner and remote alike) runs the identical clock off the identical trigger, the phase
    /// message every client receives (What exists D) - there is nothing here that only the owner
    /// could know.
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
