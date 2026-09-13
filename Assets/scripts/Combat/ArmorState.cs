using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// One player's armor pool: a buffer in front of health that damage eats through first and
    /// that comes back on its own once the player disengages. Plain C# for the same reason as the
    /// rest of Combat - it is unit tested without touching the Unity engine, and a MonoBehaviour
    /// wrapper feeds it Time.deltaTime and the out-of-combat timer in a later task.
    ///
    /// The refill is GRADUAL, not instant - Tudor's 2026-09-13 revision of the original design.
    /// Once secondsSinceCombat clears the recharge delay, the pool climbs toward Capacity at a
    /// constant rate of Capacity / refillSeconds per second, rather than snapping to full the
    /// instant the delay is up. That means a pool that was only partly drained finishes refilling
    /// sooner than one that broke completely, and re-engaging mid-refill keeps whatever has ticked
    /// back in rather than losing it. refillSeconds is one value shared by every armor tier - only
    /// the WAIT before a refill starts (the recharge delay) differs per tier, not the speed of the
    /// climb once it does.
    ///
    /// Capacity, the recharge delay and the refill duration are passed in rather than read from
    /// ArmorConfig, so this class never depends on the asset layer and tiers can be swapped at
    /// runtime via SetTier.
    /// </summary>
    public sealed class ArmorState
    {
        private float capacity;
        private float rechargeDelaySeconds;
        private readonly float refillSeconds;

        public float Current { get; private set; }

        public float Capacity => capacity;

        /// <summary>
        /// Broken means the pool cannot absorb anything right now - Current is exactly 0, so the
        /// next hit reaches health unfiltered. A refill in progress clears this the moment Current
        /// ticks above 0, even though the pool may still be far from full: Broken describes whether
        /// there is currently anything to absorb with, not whether the refill has finished.
        /// </summary>
        public bool IsBroken => Current <= 0f;

        public ArmorState(float capacity, float rechargeDelaySeconds, float refillSeconds)
        {
            this.capacity = capacity;
            this.rechargeDelaySeconds = rechargeDelaySeconds;
            this.refillSeconds = refillSeconds;

            // A fresh pool starts full: a player who has just bought armor should have it.
            Current = capacity;
        }

        /// <summary>
        /// Switches to a different tier and fills the pool to the new capacity. Filling is the
        /// point: buying an upgrade mid-match must not leave the player sitting on the old, lower
        /// armor amount, waiting through a gradual refill for armor they already paid for. This is
        /// a purchase, not a recharge, so it is still instant. Downgrading fills to the new,
        /// smaller capacity, which also guarantees Current can never exceed Capacity.
        /// </summary>
        public void SetTier(float capacity, float rechargeDelaySeconds)
        {
            this.capacity = capacity;
            this.rechargeDelaySeconds = rechargeDelaySeconds;
            Current = capacity;
        }

        /// <summary>
        /// Eats what it can of an incoming hit and reports how much it actually took, so the
        /// caller can pass the remainder on to health. It can only ever take what it currently
        /// holds, not what its capacity is, so a part-drained pool absorbs proportionally less.
        /// </summary>
        public float Absorb(float amount)
        {
            // Guards against a heal or a zeroed-out hit arriving here and refilling armor as a
            // side effect of subtracting a negative number.
            if (amount <= 0f)
                return 0f;

            float taken = Mathf.Min(amount, Current);
            Current -= taken;
            return taken;
        }

        /// <summary>
        /// Waits out rechargeDelaySeconds, then climbs Current toward Capacity at a constant rate
        /// of Capacity / refillSeconds per second. secondsSinceCombat is owned by the caller
        /// (PlayerHealth) and is reset to 0 the instant this player deals OR takes damage, so a hit
        /// landing mid-refill needs no special handling here: the caller's next Tick call simply
        /// arrives with secondsSinceCombat back below the delay, which halts progress until the
        /// delay elapses again. deltaTime is only consulted once the delay has passed - before
        /// that there is nothing to integrate.
        /// </summary>
        public void Tick(float deltaTime, float secondsSinceCombat)
        {
            if (secondsSinceCombat < rechargeDelaySeconds || Current >= capacity)
                return;

            if (refillSeconds <= 0f)
            {
                // A designer-facing safety net, not a supported tuning value: 0 would otherwise
                // divide by zero below. Treat it as "as fast as possible" rather than throwing.
                Current = capacity;
                return;
            }

            float ratePerSecond = capacity / refillSeconds;
            Current = Mathf.Min(capacity, Current + ratePerSecond * deltaTime);
        }

        /// <summary>
        /// Empties the pool. Called on death, so a respawning player does not keep the armor they
        /// had when they went down - they earn it back through the out-of-combat timer like
        /// anyone else. Upgrade levels are a separate, longer-lived thing PlayerHealth keeps
        /// through death; only the current fill of the pool clears here.
        /// </summary>
        public void Clear()
        {
            Current = 0f;
        }

        /// <summary>
        /// Adopts a value replicated from the owning client, clamped to this pool's current
        /// capacity. This exists for one purpose - a remote client's copy of this player never
        /// runs Absorb or Tick (Update on PlayerHealth skips both when the player is not mine), so
        /// PlayerNetSync's receive side needs some way to make a non-owner's ArmorState agree with
        /// what the owner actually has.
        ///
        /// Do not reach for this as a general setter: a capacity change (buying an armor tier)
        /// must still go through SetTier, which fills to the new capacity rather than clamping an
        /// old value into it.
        /// </summary>
        public void SetFromNetwork(float current)
        {
            Current = Mathf.Clamp(current, 0f, capacity);
        }
    }
}
