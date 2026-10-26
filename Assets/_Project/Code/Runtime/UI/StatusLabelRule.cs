using UnityEngine;

namespace Overpower.UI
{
    /// <summary>Which status label a player wears. The numbers are what travels in the player's Custom
    /// Property (see StatusLabelProperty), so they must never be reordered.</summary>
    public enum StatusLabel { None = 0, Slowed = 1, Stunned = 2 }

    /// <summary>
    /// The pure rule for the STUNNED and SLOWED labels over a player's head and on their own HUD (D18): which
    /// label shows (stun wins), how full its countdown bar is, and how a remote copy turns the server-time end it
    /// was told about into seconds left. No engine beyond Mathf.
    /// </summary>
    public static class StatusLabelRule
    {
        /// <summary>Stun beats slow; neither running means no label. A status is running while its
        /// remaining time is above zero.</summary>
        public static StatusLabel Choose(float stunRemaining, float slowRemaining)
        {
            if (stunRemaining > 0f)
                return StatusLabel.Stunned;
            if (slowRemaining > 0f)
                return StatusLabel.Slowed;
            return StatusLabel.None;
        }

        /// <summary>The bar's fill: remaining over the duration the status started with, clamped 0..1.
        /// No known duration reads as empty rather than dividing by zero.</summary>
        public static float Fill(float remaining, float duration)
        {
            if (duration <= 0f || remaining <= 0f)
                return 0f;
            return Mathf.Clamp01(remaining / duration);
        }

        /// <summary>The longest window seen since the status last ran out: a fresh or longer status
        /// raises it, a status counting down holds it, and nothing running resets it. Only the victim's
        /// own client sees the apply, so it works out the duration from what it polls (the same idea as
        /// MarkIndicatorRule.TrackedTotal).</summary>
        public static float TrackedTotal(float previousTotal, float remaining)
        {
            if (remaining <= 0f)
                return 0f;
            return remaining > previousTotal ? remaining : previousTotal;
        }

        /// <summary>Whether the owner should write a new value to its Player Property: the first time ever, when
        /// the label changes (including to None), or, while a label shows, when its end moved by more than the
        /// tolerance (a refresh) or its window changed. Jitter under the tolerance never republishes.</summary>
        public static bool ShouldPublish(bool publishedOnce, StatusLabel publishedLabel, int publishedEndMs, int publishedTotalMs,
                                         StatusLabel label, int endMs, int totalMs, int toleranceMs)
        {
            if (!publishedOnce || label != publishedLabel)
                return true;
            if (label == StatusLabel.None)
                return false;
            return Mathf.Abs(unchecked(endMs - publishedEndMs)) > toleranceMs || totalMs != publishedTotalMs;
        }

        /// <summary>Seconds left until endServerMs, read against the current server time. The 32-bit
        /// subtraction wraps on purpose: Photon's server clock rolls over about every 49.7 days and a
        /// plain subtraction would then read a huge wrong number.</summary>
        public static float SecondsLeft(int endServerMs, int nowServerMs)
        {
            int leftMs = unchecked(endServerMs - nowServerMs);
            return leftMs / 1000f;
        }
    }
}
