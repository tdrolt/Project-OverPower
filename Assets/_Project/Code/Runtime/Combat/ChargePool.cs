using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Backs every multi-charge cooldown (dash, mines) so none hand-rolls its own timer, the same
    /// divergence DamageResolver and StatusEffectState avoid. Plain C#, unit tested without the Unity
    /// engine.
    ///
    /// Charges recharge one at a time, sequentially, which is what makes two charges a resource to
    /// spend rather than a free double-jump: spending both starts a rechargeSeconds timer for the
    /// first, and only when it completes does the second begin; they never come back together.
    /// </summary>
    public sealed class ChargePool
    {
        // Not readonly: an ability module's cooldownSeconds can be retuned live in Play mode, and
        // SetRechargeSeconds is how that edit reaches the running pool.
        private float rechargeSeconds;

        // Seconds accumulated toward returning the next missing charge. Only meaningful while
        // Available is below MaxCharges - a full pool has nothing to time.
        private float timer;

        public int Available { get; private set; }
        public int MaxCharges { get; private set; }

        /// <summary>0..1 toward the next charge, for the HUD. 0 when full, and 0 (never NaN) for a
        /// 0-second recharge, where the next Tick fills outright.</summary>
        public float RechargeProgress => Available >= MaxCharges || rechargeSeconds <= 0f ? 0f : timer / rechargeSeconds;

        // Charges that must be back after the pool runs dry before it can be used again; 0 or 1
        // means no lock-out (D8).
        private int chargesNeededAfterRunningDry;

        /// <summary>True from the moment a use empties the pool until enough charges have refilled.
        /// While locked nothing can be consumed, even if one charge is already back.</summary>
        public bool IsLocked { get; private set; }

        public bool CanConsume => Available > 0 && !IsLocked;

        // The needed number can never ask for more than the pool can hold.
        private int EffectiveNeeded => Mathf.Min(chargesNeededAfterRunningDry, MaxCharges);

        public ChargePool(int maxCharges, float rechargeSeconds, int chargesNeededAfterRunningDry = 0)
        {
            this.chargesNeededAfterRunningDry = chargesNeededAfterRunningDry;
            MaxCharges = maxCharges;
            this.rechargeSeconds = rechargeSeconds;
            Available = maxCharges;
        }

        public bool TryConsume()
        {
            if (!CanConsume)
                return false;

            Available--;
            if (Available == 0 && EffectiveNeeded > 1)
                IsLocked = true;
            return true;
        }

        /// <summary>Retunes the lock-out (0 or 1 = off). Turning it off, or asking for fewer than
        /// are already back, unlocks straight away.</summary>
        public void SetChargesNeededAfterRunningDry(int needed)
        {
            chargesNeededAfterRunningDry = Mathf.Max(0, needed);
            UpdateLock();
        }

        private void UpdateLock()
        {
            if (IsLocked && (EffectiveNeeded <= 1 || Available >= EffectiveNeeded))
                IsLocked = false;
        }

        public void Tick(float deltaTime)
        {
            if (Available >= MaxCharges)
                return;

            timer += deltaTime;

            // A loop rather than a single division: a large deltaTime (a lag spike, or a test ticking
            // several recharge periods at once) can complete more than one charge, and each still has
            // to pay the full rechargeSeconds before the next starts timing; that sequencing is the
            // whole point of this class.
            while (timer >= rechargeSeconds && Available < MaxCharges)
            {
                timer -= rechargeSeconds;
                Available++;
            }

            UpdateLock();

            if (Available >= MaxCharges)
                timer = 0f; // fully charged - no partial progress left to report
        }

        /// <summary>The zip gun's reset-on-takedown, and respawn: fill instantly.</summary>
        public void RefillAll()
        {
            Available = MaxCharges;
            timer = 0f;
            IsLocked = false;
        }

        /// <summary>
        /// Retunes how long one charge takes to return, keeping Available as it was: a designer
        /// changing cooldownSeconds mid-match should not reset or refund anything. Clamped
        /// non-negative, since a negative time would make the Tick loop spin forever.
        /// </summary>
        public void SetRechargeSeconds(float seconds)
        {
            rechargeSeconds = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Raising the cap grants the new slot(s) immediately, like a respawn refill: a player who just
        /// unlocked a second dash charge should not wait rechargeSeconds to feel it. Lowering the cap
        /// clamps Available down to fit and grants no extra recharge time for the removed slots.
        /// </summary>
        public void SetMaxCharges(int max)
        {
            int delta = max - MaxCharges;
            MaxCharges = max;
            Available = delta > 0
                ? Mathf.Min(MaxCharges, Available + delta)
                : Mathf.Min(Available, MaxCharges);

            UpdateLock();

            if (Available >= MaxCharges)
                timer = 0f;
        }
    }
}
