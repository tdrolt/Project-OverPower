using UnityEngine;

namespace Overpower.UI
{
    /// <summary>One damage number's pose at a given moment - scale, how far it has risen, and its
    /// alpha - so DamageNumberView only ever has to apply numbers to a RectTransform/Graphic, never
    /// compute them. Immutable, same reasoning as DamageInfo (Combat/DamageInfo.cs).</summary>
    public readonly struct DamageNumberPose
    {
        public readonly float Scale;
        public readonly float Rise;
        public readonly float Alpha;

        public DamageNumberPose(float scale, float rise, float alpha)
        {
            Scale = scale;
            Rise = rise;
            Alpha = alpha;
        }
    }

    /// <summary>
    /// Pure pop/rise/fade/rounding rules for a damage number - unit tested without touching the engine
    /// beyond Mathf (same convention as DamageResolver). No UnityEngine.UI or TMPro reference here;
    /// DamageNumberView is the only adapter that ever touches a Graphic.
    ///
    /// Mark plan step 2, Tudor's override (top-of-plan table, answer 4): "one for each enemy that you
    /// are hitting" - ONE live number per enemy that ADDS each hit, re-pops (see ThePopStartsBig...)
    /// and restarts its own life, rather than the plan's original per-hit-with-merge-window design.
    /// That override drops damageNumberMergeSeconds/damageNumberSpread and ShouldMerge/Side entirely
    /// and adds one field instead, damageNumberHoldSeconds: how long the number stays fully solid (no
    /// rise, no fade) after the LAST hit that touched it, before it starts rising and fading over
    /// damageNumberLifetimeSeconds. DamageNumberView is what restarts the "age since last hit" and
    /// "pop age" clocks on every added hit - this class only ever answers "given these two ages, what
    /// does the number look like right now".
    /// </summary>
    public static class DamageNumberMotion
    {
        /// <summary>
        /// ageSinceLastHit: seconds since the most recent hit that added to this number (0 the instant
        ///   a hit lands or re-pops it) - drives Rise/Alpha, which only start moving once this passes
        ///   holdSeconds.
        /// popAge: seconds since the number was last (re)popped - drives Scale only, independent of
        ///   the hold/rise/fade clock, so a rapid follow-up hit re-pops the SIZE even if the number was
        ///   already well past its hold and starting to rise/fade.
        /// </summary>
        public static DamageNumberPose Evaluate(float ageSinceLastHit, float popAge, float holdSeconds, float lifetimeSeconds,
            float popSeconds, float popScale, float rise, float fadeStart01, float markedScale, bool marked)
        {
            // Guarded (not divided by) a zero/negative popSeconds: the pop is simply already settled.
            float scale = popSeconds > 0f ? Mathf.Lerp(popScale, 1f, Mathf.Clamp01(popAge / popSeconds)) : 1f;
            if (marked)
                scale *= markedScale;

            float postHold = ageSinceLastHit - holdSeconds;
            if (postHold <= 0f)
                return new DamageNumberPose(scale, 0f, 1f); // Solid: still within the hold window.

            // Guarded (not divided by) a zero/negative lifetime: past the hold with nothing left to
            // rise or fade over reads as already at the very end of its life (t = 1).
            float t = lifetimeSeconds > 0f ? Mathf.Clamp01(postHold / lifetimeSeconds) : 1f;
            float actualRise = rise * (1f - (1f - t) * (1f - t)); // Eases out - fast at first, settling toward the top.

            float alpha;
            if (fadeStart01 >= 1f)
                alpha = 1f; // Never fades - kept opaque its whole post-hold life.
            else if (t <= fadeStart01)
                alpha = 1f;
            else
                alpha = Mathf.Clamp01(1f - (t - fadeStart01) / (1f - fadeStart01));

            return new DamageNumberPose(scale, actualRise, alpha);
        }

        /// <summary>The integer a damage number actually shows: rounded half up (never banker's
        /// rounding), and never zero for a hit that landed at all - a 0.3 splash tick still reads "1",
        /// not nothing.</summary>
        public static int Shown(float amount) => amount > 0f ? Mathf.Max(1, (int)System.Math.Floor(amount + 0.5)) : 0;
    }
}
