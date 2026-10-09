using UnityEngine;

namespace Overpower.Match
{
    /// <summary>
    /// The bounty shown above a tower. Every client decides it from the replicated TerritorySnapshot and the server clock, with the same
    /// BountyRule the master pays by, so the number shown is the number paid. The hold times are in server milliseconds and may wrap.
    /// </summary>
    public static class BountyLabelRules
    {
        public struct Offer
        {
            public int Amount;
            public int HoldMs;
        }

        /// <summary>The bounty to show above a zone now, 0 for none: an owned zone once its hold has reached the hold time, or a neutral zone
        /// whose last owner had held it that long (its bounty goes to whoever takes it from that owner).</summary>
        public static int LabelAmount(int owner, int heldSinceMs, int lastOwner, int lastHeldMs, int nowMs, int holdMs, int bounty)
        {
            if (bounty <= 0) return 0;
            if (owner >= 0) return unchecked(nowMs - heldSinceMs) >= holdMs ? bounty : 0;
            return lastOwner >= 0 && lastHeldMs >= holdMs ? bounty : 0;
        }

        /// <summary>What a capture just paid, from the zone as it stood before the capture (its old holder and how long it held).</summary>
        public static int PopAmount(int newOwner, int lastOwner, int lastHeldMs, int holdMs, int bounty) =>
            BountyRule.PayoutOnCapture(newOwner, lastOwner, lastHeldMs, bounty, holdMs);

        /// <summary>The bounty a zone carries and the hold time it needs, in the mode's own units (Dominion points, Conquest gold per player).
        /// The hold time is converted the way each mode's payout converts it. Nothing is offered where it cannot be paid now.</summary>
        public static Offer OfferFor(bool dominion, bool paysNow, int tierBounty, float tierHoldSeconds, int dominionPoints, float dominionHoldSeconds)
        {
            if (!paysNow) return default;
            return dominion
                ? new Offer { Amount = dominionPoints, HoldMs = Mathf.RoundToInt(dominionHoldSeconds * 1000f) }
                : new Offer { Amount = tierBounty, HoldMs = (int)(tierHoldSeconds * 1000f) };
        }

        /// <summary>Solid for the first 40 % of the pop, then fading to nothing.</summary>
        public static float PopAlpha(float ageSeconds, float durationSeconds)
        {
            float t = durationSeconds > 0f ? Mathf.Clamp01(ageSeconds / durationSeconds) : 1f;
            return 1f - Mathf.Clamp01((t - 0.4f) / 0.6f);
        }

        public static float PopRise(float ageSeconds, float durationSeconds, float riseMetres)
        {
            float t = durationSeconds > 0f ? Mathf.Clamp01(ageSeconds / durationSeconds) : 1f;
            return riseMetres * t;
        }
    }
}
