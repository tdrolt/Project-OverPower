using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Pure fade/pulse rule for the mark diamond (mark plan step 5) - unit tested without touching the
    /// engine beyond Mathf, same convention as DamageNumberMotion. No Image/RectTransform reference
    /// here; MarkIndicatorView (the shooter's pooled diamonds, one per marked enemy) and
    /// SelfMarkIndicatorView (Tudor's answer 1: the marked player's own diamond over their own head)
    /// are the only two adapters that ever touch a Graphic, and both call this same rule.
    /// </summary>
    public static class MarkIndicatorRule
    {
        /// <summary>
        /// secondsLeft/totalSeconds: how much of the mark's own window is left, and how long that
        /// window was when the diamond most recently started (or restarted) fading from full. 0 or
        /// negative secondsLeft is a hard cut to fully hidden (0), not a fade to invisible - the
        /// caller's own LateUpdate additionally hides the GameObject at that point (belt and braces,
        /// same pattern DamageNumberView's own hold/fade split uses).
        ///
        /// Otherwise: a base ramp from 1 (secondsLeft == totalSeconds, freshly marked) down toward
        /// minAlpha (secondsLeft near 0, about to run out), multiplied by a pulse that oscillates
        /// between minAlpha and 1. The pulse uses COS, not a raw sine, specifically so it always reads
        /// 1 (no attenuation at all) the instant time*2π*pulseSpeed is 0 - which is EVERY instant when
        /// pulseSpeed is 0 (the argument is 0 regardless of time), matching pulseSpeed's own tooltip:
        /// "0 = steady". The final Clamp is what actually guarantees the floor/ceiling promise (the raw
        /// product of two [minAlpha,1] terms can dip under minAlpha at their shared trough - e.g. both
        /// terms at minAlpha 0.35 multiply to about 0.12), not the two Lerps on their own.
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
        /// Tudor's answer 1 (mark plan, top-of-plan table): the marked player's own diamond has no
        /// message to learn its mark's ORIGINAL window from - PlayerHealth.LongestMarkSecondsLeft only
        /// ever reports how much time is LEFT (a plain per-frame poll of the victim's own MarkLedger,
        /// no network message at all - Decision 1, the ledger already lives on the victim), never how
        /// long the live mark's own window WAS when it started. Alpha above still needs a "total" to
        /// fade the ramp against, so SelfMarkIndicatorView keeps whatever the LARGEST secondsLeft it has
        /// polled since the diamond was last fully hidden, and this is the one decision that says when
        /// to bump that running total back up: whenever the freshly polled value is BIGGER than what is
        /// already tracked - a mark just landed (secondsLeft jumps from 0 to a fresh window), or a
        /// second attacker's own longer-lived mark just became the longest one on this target (see
        /// MarkLedger.LongestSecondsLeft's own comment on why "longest, not per-attacker" is enough).
        /// Otherwise the total holds steady while secondsLeft counts down under it - which is exactly
        /// what makes Alpha's ramp read as "empties toward the floor" instead of silently recomputing a
        /// shorter window every single frame and never actually fading.
        /// </summary>
        public static float TrackedTotal(float previousTotal, float secondsLeft) =>
            secondsLeft > previousTotal ? secondsLeft : previousTotal;
    }
}
