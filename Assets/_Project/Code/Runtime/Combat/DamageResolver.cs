using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The only place damage maths happens: the bullet path and the AoE path must both go through it
    /// (the dash damage-reduction buff was once applied on one path only).
    /// Order, fixed and tested:
    ///   1. multiply by (1 + vulnerability)
    ///   2. multiply by (1 - reduction)
    ///   3. armor absorbs what it can, unless the hit ignores armor
    ///   4. the remainder comes off health, clamped so overkill is not reported
    /// </summary>
    public static class DamageResolver
    {
        public static DamageResult Resolve(float amount, bool ignoresArmor, float health,
                                           float armor, float vulnerability, float reduction)
        {
            float final = Mathf.Max(0f, amount)
                        * (1f + Mathf.Max(0f, vulnerability))
                        * (1f - Mathf.Clamp01(reduction));

            float absorbed = 0f;
            if (!ignoresArmor && armor > 0f)
            {
                absorbed = Mathf.Min(armor, final);
                final -= absorbed;
            }

            float healthLost = Mathf.Min(Mathf.Max(0f, health), final);
            bool armorBroke = !ignoresArmor && armor > 0f && absorbed >= armor - 0.0001f;
            bool lethal = health - healthLost <= 0.0001f && healthLost > 0f;

            return new DamageResult(absorbed, healthLost, armorBroke, lethal);
        }
    }
}
