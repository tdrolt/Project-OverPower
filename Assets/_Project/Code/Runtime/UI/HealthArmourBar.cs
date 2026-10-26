using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// How full a health-and-armour bar is: health against max health, armour against the armour this player can hold now, so
    /// full armour always fills its whole bar however much an upgrade raised it. Your HUD (PlayerHud) and the plate over every
    /// head (PlayerHealth.UpdateOverheadBar) both draw from this.
    /// </summary>
    public static class HealthArmourBar
    {
        public struct Lengths
        {
            public float health;
            /// <summary>Armour held / armour this player can hold now.</summary>
            public float armourOfCapacity;
            /// <summary>False when they can hold no armour: nothing of it is drawn, not even an empty bar.</summary>
            public bool armourShown;
        }

        public static Lengths Compute(float health, float maxHealth, float armour, float capacity)
        {
            if (maxHealth <= 0f)
                return default;

            var l = new Lengths { health = Mathf.Clamp01(health / maxHealth) };
            if (capacity <= 0f)
                return l;

            // A remote copy can briefly hold more armour than a capacity that has not caught up yet (ArmorState.SetFromNetwork).
            l.armourShown = true;
            l.armourOfCapacity = Mathf.Clamp(armour, 0f, capacity) / capacity;
            return l;
        }
    }
}
