using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The meter behind the Space-bar ultimate: accrues from damage dealt, damage taken, kills and
    /// assists, clamps at a cap, and is spent only when full - Task 1.11's own "resource meter
    /// generated through active combat participation" (the GDD's wording). Plain C#, unit tested
    /// without touching the Unity engine - the same reasoning as ChargePool and StatusEffectState.
    /// UltimateCharge (Player) is the thin owner-only MonoBehaviour that feeds this from
    /// PlayerHealth.Damaged (taken) and CombatEvents (dealt, kill, assist) and exposes it to the
    /// three ultimate modules.
    ///
    /// Deliberately its own type rather than a ChargePool: a ChargePool counts discrete charges that
    /// recharge on a timer, one at a time. This is one continuous meter, filled by amounts of very
    /// different sizes in the same currency (a single point of damage vs. a whole kill) and spent
    /// all at once, never partially and never on a clock.
    /// </summary>
    public sealed class UltimateChargeState
    {
        // Not readonly: Retune below lets a designer's live Inspector edit reach the meter that is
        // actually running, the same reasoning ChargePool.SetMaxCharges/SetRechargeSeconds already
        // follow for cooldowns.
        private float maxCharge;
        private float perDamageDealt;
        private float perDamageTaken;
        private float perKill;
        private float perAssist;

        public float Current { get; private set; }
        public float Max => maxCharge;

        /// <summary>True only at the cap - the sole gate every ultimate's IsReady reads.</summary>
        public bool IsFull => Current >= maxCharge;

        /// <summary>0..1, for the HUD meter. 0 for a zero-or-negative cap rather than dividing by
        /// zero - the same defensive shape as ChargePool.RechargeProgress.</summary>
        public float Normalised => maxCharge > 0f ? Current / maxCharge : 0f;

        public UltimateChargeState(float maxCharge, float perDamageDealt, float perDamageTaken,
                                   float perKill, float perAssist)
        {
            this.maxCharge = Mathf.Max(0f, maxCharge);
            this.perDamageDealt = perDamageDealt;
            this.perDamageTaken = perDamageTaken;
            this.perKill = perKill;
            this.perAssist = perAssist;
        }

        /// <summary>CombatEvents.LocalDamageDealt - armor + health actually removed from the victim.</summary>
        public void AddDamageDealt(float amount) => Add(amount * perDamageDealt);

        /// <summary>PlayerHealth.Damaged - armor + health actually removed from this player. Never
        /// called while invulnerable: the damage funnel already returns default() before Damaged
        /// fires, so there is nothing to add in that window.</summary>
        public void AddDamageTaken(float amount) => Add(amount * perDamageTaken);

        /// <summary>CombatEvents.LocalTakedown(true).</summary>
        public void AddKill() => Add(perKill);

        /// <summary>CombatEvents.LocalTakedown(false).</summary>
        public void AddAssist() => Add(perAssist);

        /// <summary>
        /// Refused unless the meter is already full - a partial spend would leave the caster with an
        /// ultimate that neither fires nor keeps the charge it had. True and empties the meter back
        /// to 0 only when it was full; otherwise Current is left exactly where it was.
        /// </summary>
        public bool Spend()
        {
            if (!IsFull)
                return false;

            Current = 0f;
            return true;
        }

        /// <summary>F1's "Fill Ultimate" - instantly full, for testing without farming a dummy.</summary>
        public void Fill() => Current = maxCharge;

        /// <summary>2.7b fresh start (Decision 6): empties the meter back to 0, the same as never having
        /// earned anything this match - called once, at match-live, never mid-match.</summary>
        public void Clear() => Current = 0f;

        /// <summary>
        /// Applies a live Inspector retune. Current is clamped to the new cap (down, never up) rather
        /// than reset - a designer lowering the cap mid-session should not hand out a free ultimate
        /// nor take away one that was already earned above the new cap; the rates apply to accrual
        /// from this point on only.
        /// </summary>
        public void Retune(float newMaxCharge, float newPerDamageDealt, float newPerDamageTaken,
                           float newPerKill, float newPerAssist)
        {
            maxCharge = Mathf.Max(0f, newMaxCharge);
            perDamageDealt = newPerDamageDealt;
            perDamageTaken = newPerDamageTaken;
            perKill = newPerKill;
            perAssist = newPerAssist;
            Current = Mathf.Min(Current, maxCharge);
        }

        private void Add(float amount)
        {
            if (amount <= 0f)
                return; // A zero or negative contribution (a misconfigured rate, say) never drains the meter.

            Current = Mathf.Min(maxCharge, Current + amount);
        }
    }
}
