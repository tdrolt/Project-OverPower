using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// One player's armor pool: a buffer in front of health that damage eats first and that refills
    /// on its own once the player disengages. Plain C#, unit tested without the Unity engine;
    /// PlayerHealth feeds it Time.deltaTime and the out-of-combat timer. Capacity, the recharge
    /// delay and the refill duration are passed in, never read from ArmorConfig, so tiers can swap
    /// at runtime via SetTier.
    ///
    /// The refill is GRADUAL: once secondsSinceCombat clears the recharge delay, the pool climbs at a
    /// constant Capacity / refillSeconds per second, so a part-drained pool finishes sooner and
    /// re-engaging mid-refill keeps what has ticked back. refillSeconds is shared by every tier; only
    /// the WAIT before a refill starts differs per tier.
    /// </summary>
    public sealed class ArmorState
    {
        private float capacity;
        private float rechargeDelaySeconds;
        private readonly float refillSeconds;

        public float Current { get; private set; }

        public float Capacity => capacity;

        /// <summary>
        /// Broken means Current is exactly 0 and the next hit reaches health unfiltered. It clears the
        /// moment a refill ticks Current above 0, even if the pool is far from full.
        /// </summary>
        public bool IsBroken => Current <= 0f;

        public ArmorState(float capacity, float rechargeDelaySeconds, float refillSeconds)
        {
            this.capacity = capacity;
            this.rechargeDelaySeconds = rechargeDelaySeconds;
            this.refillSeconds = refillSeconds;

            Current = capacity;
        }

        /// <summary>
        /// Switches tier and fills the pool to the new capacity: buying an upgrade mid-match must not
        /// leave the player on the old armor amount waiting through a gradual refill. A purchase, not
        /// a recharge, so it is instant. Downgrading also guarantees Current never exceeds Capacity.
        /// </summary>
        public void SetTier(float capacity, float rechargeDelaySeconds)
        {
            this.capacity = capacity;
            this.rechargeDelaySeconds = rechargeDelaySeconds;
            Current = capacity;
        }

        /// <summary>
        /// Eats what it can of a hit and returns how much it took, for the caller to pass the
        /// remainder to health. Only takes what it currently holds, not its capacity.
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
        /// Waits out rechargeDelaySeconds, then climbs toward Capacity. secondsSinceCombat is owned by
        /// PlayerHealth and reset to 0 the instant this player deals OR takes damage, so a hit
        /// mid-refill needs no special handling here: the next Tick arrives below the delay and
        /// progress halts.
        /// </summary>
        public void Tick(float deltaTime, float secondsSinceCombat)
        {
            if (secondsSinceCombat < rechargeDelaySeconds || Current >= capacity)
                return;

            if (refillSeconds <= 0f)
            {
                // Safety net, not a supported tuning value: 0 would divide by zero below.
                Current = capacity;
                return;
            }

            float ratePerSecond = capacity / refillSeconds;
            Current = Mathf.Min(capacity, Current + ratePerSecond * deltaTime);
        }

        /// <summary>
        /// Empties the pool; called the instant a player dies (PlayerHealth.ApplyDamage). At respawn
        /// ArmorConfig.RespawnWithFullArmor decides: off keeps it empty so armor is earned back
        /// through the out-of-combat timer, on calls RefillToFull. Upgrade levels live on
        /// PlayerHealth and survive death; only the fill clears here.
        /// </summary>
        public void Clear()
        {
            Current = 0f;
        }

        /// <summary>
        /// Fills the pool to its current Capacity without touching the tier (the "respawn with full
        /// armor" path, ArmorConfig.RespawnWithFullArmor). Separate from SetTier because a respawn
        /// never changes which tier a player owns.
        /// </summary>
        public void RefillToFull()
        {
            Current = capacity;
        }

        /// <summary>
        /// Adopts a value replicated from the owning client, clamped to the current capacity. A remote
        /// client's copy never runs Absorb or Tick (PlayerHealth.Update skips both for non-owners),
        /// so PlayerNetSync's receive side uses this. Not a general setter: a capacity change (buying
        /// a tier) must go through SetTier.
        ///
        /// A transient clamp is expected: armor LEVELS (which change Capacity) travel separately as
        /// Custom Properties, so a sync packet can land while a level bump is still in flight and
        /// clamp to the stale lower Capacity for one frame. It self-heals on the next sync tick.
        /// </summary>
        public void SetFromNetwork(float current)
        {
            Current = Mathf.Clamp(current, 0f, capacity);
        }
    }
}
