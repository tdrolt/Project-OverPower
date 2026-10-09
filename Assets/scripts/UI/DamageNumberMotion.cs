using UnityEngine;

namespace Overpower.UI
{
    /// <summary>One damage number's pose at a given moment (scale, rise, alpha), so DamageNumberView only applies
    /// numbers to a RectTransform/Graphic, never computes them. Immutable, as DamageInfo is.</summary>
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
    /// Pure pop/rise/fade/rounding rules for a damage number, unit tested without the engine beyond Mathf;
    /// DamageNumberView is the only adapter that touches a Graphic. ONE live number per enemy that ADDS each hit,
    /// re-pops and restarts its own life; damageNumberHoldSeconds is how long it stays fully solid after the LAST
    /// hit, before rising and fading over damageNumberLifetimeSeconds. DamageNumberView restarts the "age since
    /// last hit" and "pop age" clocks on every added hit; this class only turns the two ages into a pose.
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

        /// <summary>The integer a damage number shows: rounded half up (never banker's rounding), never zero for a
        /// hit that landed - a 0.3 splash tick still reads "1".</summary>
        public static int Shown(float amount) => amount > 0f ? Mathf.Max(1, (int)System.Math.Floor(amount + 0.5)) : 0;
    }
}
