using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Pure fade/pulse rule for the mark diamond, unit tested without the engine beyond Mathf. MarkIndicatorView
    /// (the shooter's pooled diamonds) and SelfMarkIndicatorView (the marked player's own diamond) are the only
    /// adapters that touch a Graphic, and both call this rule.
    /// </summary>
    public static class MarkIndicatorRule
    {
        /// <summary>
        /// secondsLeft/totalSeconds: how much of the mark's window is left, and how long that window was when the
        /// diamond last started fading from full. 0 or negative secondsLeft is a hard cut to hidden (0), not a
        /// fade; the caller's LateUpdate also hides the GameObject then.
        ///
        /// Otherwise: a base ramp from 1 (freshly marked) toward minAlpha (about to run out), times a pulse between
        /// minAlpha and 1. The pulse uses COS so it reads 1 when time*2π*pulseSpeed is 0 - every instant when
        /// pulseSpeed is 0 ("0 = steady"). The final Clamp is what guarantees the floor: the product of two
        /// [minAlpha,1] terms can dip under minAlpha at their shared trough (0.35 x 0.35 is about 0.12).
        /// </summary>
        public static float Alpha(float secondsLeft, float totalSeconds, float minAlpha, float pulseSpeed, float time)
        {
            if (secondsLeft <= 0f)
                return 0f;

            float ramp01 = totalSeconds > 0f ? Mathf.Clamp01(secondsLeft / totalSeconds) : 0f;
            float baseAlpha = Mathf.Lerp(minAlpha, 1f, ramp01);

            float wave01 = (Mathf.Cos(time * 2f * Mathf.PI * pulseSpeed) + 1f) * 0.5f;
            float pulse = Mathf.Lerp(minAlpha, 1f, wave01);

            return Mathf.Clamp(baseAlpha * pulse, minAlpha, 1f);
        }

        /// <summary>
        /// The marked player's own diamond can only poll how much time is LEFT on its mark (PlayerHealth.
        /// LongestMarkSecondsLeft, a per-frame read of the victim's own MarkLedger, no network message), never the
        /// window's original length, yet Alpha needs a total to fade against. SelfMarkIndicatorView keeps the
        /// LARGEST secondsLeft polled since the diamond was last hidden; this bumps it whenever a fresh value is
        /// bigger (a new mark, or a second attacker's longer one; "longest, not per-attacker" is enough, see
        /// MarkLedger.LongestSecondsLeft). Otherwise the total holds while secondsLeft counts down, so the ramp
        /// empties instead of recomputing a shorter window every frame and never fading.
        /// </summary>
        public static float TrackedTotal(float previousTotal, float secondsLeft) =>
            secondsLeft > previousTotal ? secondsLeft : previousTotal;
    }
}
