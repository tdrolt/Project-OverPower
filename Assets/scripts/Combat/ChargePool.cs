using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Backs every multi-charge cooldown - dash (2 charges, 5s) and mines (2 charges, 10s) both
    /// sit on this instead of each hand-rolling its own timer, the same divergence problem
    /// DamageResolver and StatusEffectState were built to avoid for their own systems. Plain C#
    /// for the same reason as the rest of Combat: unit tested without touching the Unity engine.
    ///
    /// The rule that makes two charges a resource to spend rather than a free double-jump:
    /// charges recharge one at a time, sequentially. Spending both starts a rechargeSeconds
    /// timer for the first charge, and only when that completes does the timer for the second
    /// begin - they never come back together.
    /// </summary>
    public sealed class ChargePool
    {
        // Not readonly: an ability module's designer-facing cooldownSeconds can be retuned on the
        // live component in Play mode, and SetRechargeSeconds below is how that retune reaches the
        // pool it actually drives - a value fixed at construction would make that Inspector edit
        // silently do nothing.
        private float rechargeSeconds;

        // Seconds accumulated toward returning the next missing charge. Only meaningful while
        // Available is below MaxCharges - a full pool has nothing to time.
        private float timer;

        public int Available { get; private set; }
        public int MaxCharges { get; private set; }

        /// <summary>0..1 toward the next charge, for the HUD. 0 whenever the pool is full, and also
        /// 0 - never NaN - for a 0-second recharge: dividing timer by a zero rechargeSeconds has no
        /// meaningful "partway there" to report, since the very next Tick fills it outright.</summary>
        public float RechargeProgress => Available >= MaxCharges || rechargeSeconds <= 0f ? 0f : timer / rechargeSeconds;

        // Charges that must be back after the pool runs dry before it can be used again. 0 or 1 means
        // no lock-out (today's behaviour). Tudor's Dash rule (D8): use all 3, locked until 2 refill.
        private int chargesNeededAfterRunningDry;

        /// <summary>True from the moment a use empties the pool until enough charges have refilled.
        /// While locked nothing can be consumed, even if one charge is already back.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>The cast gate's question: can a charge be spent right now.</summary>
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
                return; // full - nothing is recharging

            timer += deltaTime;

            // A loop rather than a single division: a large deltaTime (a lag spike, or a test
            // ticking several recharge periods at once) can complete more than one charge, and
            // each one still has to pay the full rechargeSeconds before the next starts timing -
            // that sequencing is the whole point of this class, so it cannot be shortcut here.
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
        /// Retunes how long one charge takes to return, keeping Available exactly as it was - a
        /// designer changing cooldownSeconds mid-match should not reset or refund anything, only
        /// change the pace of what happens next. Clamped to never go negative, since a negative
        /// recharge time has no sensible meaning and would make the Tick loop below spin forever
        /// subtracting a negative number from timer instead of ever finishing.
        /// </summary>
        public void SetRechargeSeconds(float seconds)
        {
            rechargeSeconds = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Raising the cap grants the new slot(s) immediately, the same way a respawn refill
        /// would - a player who just unlocked a second dash charge should not have to wait
        /// rechargeSeconds to feel it. Lowering the cap clamps Available down to fit; it never
        /// grants extra recharge time for the slots that were removed.
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
