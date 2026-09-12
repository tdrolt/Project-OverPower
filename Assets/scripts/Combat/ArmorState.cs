using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// One player's armor pool: a buffer in front of health that damage eats through first and
    /// that comes back on its own once the player disengages. Plain C# for the same reason as the
    /// rest of Combat - it is unit tested without touching the Unity engine, and a MonoBehaviour
    /// wrapper feeds it Time.deltaTime and the out-of-combat timer in a later task.
    ///
    /// The refill is instant, not gradual. The design document says "armor regenerates after 6
    /// seconds out of combat", and a gradual refill would be a materially different design: it
    /// would reward re-engaging early with partial armor, where an instant refill makes the
    /// decision binary - you either waited long enough or you did not. That binary read is what
    /// makes the out-of-combat timer something a player can feel.
    ///
    /// Capacity and the recharge delay are passed in rather than read from ArmorConfig, so this
    /// class never depends on the asset layer and tiers can be swapped at runtime via SetTier.
    /// </summary>
    public sealed class ArmorState
    {
        private float capacity;
        private float rechargeDelaySeconds;

        public float Current { get; private set; }

        public float Capacity => capacity;

        /// <summary>Broken means there is nothing left to absorb with; damage now reaches health.</summary>
        public bool IsBroken => Current <= 0f;

        public ArmorState(float capacity, float rechargeDelaySeconds)
        {
            this.capacity = capacity;
            this.rechargeDelaySeconds = rechargeDelaySeconds;

            // A fresh pool starts full: a player who has just bought armor should have it.
            Current = capacity;
        }

        /// <summary>
        /// Switches to a different tier and fills the pool to the new capacity. Filling is the
        /// point: buying an upgrade mid-match must not leave the player sitting on the old, lower
        /// armor amount until the next time they happen to go out of combat. Downgrading fills to
        /// the new, smaller capacity, which also guarantees Current can never exceed Capacity.
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
        /// Refills the pool the moment the out-of-combat timer reaches the tier's delay.
        /// deltaTime is accepted so this ticks like every other state object in Combat, but it is
        /// deliberately unused: an instant refill has nothing to integrate over time, and the
        /// caller already owns the secondsSinceCombat timer that does the accumulating.
        /// </summary>
        public void Tick(float deltaTime, float secondsSinceCombat)
        {
            if (secondsSinceCombat >= rechargeDelaySeconds)
                Current = capacity;
        }

        /// <summary>
        /// Empties the pool. Called on death, so a respawning player does not keep the armor they
        /// had when they went down - they earn it back through the out-of-combat timer like
        /// anyone else.
        /// </summary>
        public void Clear()
        {
            Current = 0f;
        }
    }
}
