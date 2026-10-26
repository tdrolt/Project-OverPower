using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Combines every source of damage reduction one player carries into a single fraction. Keyed
    /// like PlayerMotor's speed multipliers, so a dash buff and an armor upgrade add and remove their
    /// own reduction without knowing each other. Multiplies what gets THROUGH (two 60% sources:
    /// 0.4 x 0.4 = 84% reduced) instead of adding fractions, so the total can never pass 100%.
    /// </summary>
    public sealed class DamageReductionStack
    {
        private readonly Dictionary<object, float> reductions = new Dictionary<object, float>();

        /// <summary>0..1 fraction of a hit removed; 0 with nothing applied.</summary>
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

        /// <summary>Adds or replaces one source's reduction; the same key applied twice (a buff
        /// refreshing itself) replaces rather than stacks.</summary>
        public void Set(object key, float fraction) => reductions[key] = fraction;

        public void Remove(object key) => reductions.Remove(key);

        public void Clear() => reductions.Clear();
    }
}
