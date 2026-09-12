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
        private readonly float rechargeSeconds;

        // Seconds accumulated toward returning the next missing charge. Only meaningful while
        // Available is below MaxCharges - a full pool has nothing to time.
        private float timer;

        public int Available { get; private set; }
        public int MaxCharges { get; private set; }

        /// <summary>0..1 toward the next charge, for the HUD. 0 whenever the pool is full.</summary>
        public float RechargeProgress => Available >= MaxCharges ? 0f : timer / rechargeSeconds;

        public ChargePool(int maxCharges, float rechargeSeconds)
        {
            MaxCharges = maxCharges;
            this.rechargeSeconds = rechargeSeconds;
            Available = maxCharges;
        }

        public bool TryConsume()
        {
            if (Available <= 0)
                return false;

            Available--;
            return true;
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

            if (Available >= MaxCharges)
                timer = 0f; // fully charged - no partial progress left to report
        }

        /// <summary>The zip gun's reset-on-takedown, and respawn: fill instantly.</summary>
        public void RefillAll()
        {
            Available = MaxCharges;
            timer = 0f;
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

            if (Available >= MaxCharges)
                timer = 0f;
        }
    }
}
