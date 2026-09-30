using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Combines every source of damage reduction one player is carrying into a single fraction.
    /// Keyed the same way PlayerMotor's speed multipliers are, and for the same reason: a dash
    /// buff and an armor upgrade must each be able to add or remove their own reduction without
    /// knowing the other exists, or without one silently overwriting the other's number.
    ///
    /// Reductions combine multiplicatively rather than by simply adding fractions - two 50%
    /// sources make you take 25% of a hit (75% total reduction), not a negative amount of it.
    /// Two 60% sources under a naive sum would claim to remove 120% of a hit; multiplying what
    /// gets THROUGH (0.4 x 0.4 = 0.16, i.e. 84% reduced) is the only version of "stacking" that
    /// can never go over 100%.
    /// </summary>
    public sealed class DamageReductionStack
    {
        private readonly Dictionary<object, float> reductions = new Dictionary<object, float>();

        /// <summary>0..1 fraction of a hit removed, once every source is combined. 0 with nothing
        /// applied; approaches but never reaches 1 unless a single source is itself clamped to 1.</summary>
        public float Total
        {
            get
            {
                float remainingFraction = 1f;
                foreach (float reduction in reductions.Values)
                    remainingFraction *= 1f - Mathf.Clamp01(reduction);

                return 1f - remainingFraction;
            }
        }

        /// <summary>Adds or replaces one source's reduction. The same key applied twice (a buff
        /// refreshing itself) replaces the old fraction rather than stacking a second copy of the
        /// same source on top of itself.</summary>
        public void Set(object key, float fraction) => reductions[key] = fraction;

        /// <summary>Removes one source - a buff ending, an armor upgrade being sold back.</summary>
        public void Remove(object key) => reductions.Remove(key);

        /// <summary>Drops every source at once, for ClearAll on death/respawn.</summary>
        public void Clear() => reductions.Clear();
    }
}
