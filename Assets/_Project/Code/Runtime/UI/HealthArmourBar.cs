using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// How much of a health-and-armour bar each part takes, as fractions of the bar's full length, where the full length is max
    /// health. Armour is measured in the same hit points: its track is as long as the armour this player can hold now, so full
    /// armour always fills its whole track and no empty track is drawn past it. PlayerHud's bars draw from this; the plate over
    /// every head (PlayerHealth.UpdateOverheadBar), which has no armour track, uses only health, armourOfTrack and armourShown.
    /// </summary>
    public static class HealthArmourBar
    {
        public struct Lengths
        {
            public float health;
            /// <summary>The armour this player can hold now; 0 when they can hold none.</summary>
            public float armourTrack;
            /// <summary>The armour they hold now.</summary>
            public float armour;
            /// <summary>How full the armour track is (armour held / armour possible), for a fill drawn inside the track.</summary>
            public float armourOfTrack;
            /// <summary>False when they can hold no armour: nothing of it is drawn, not even an empty track.</summary>
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
            float held = Mathf.Clamp(armour, 0f, capacity);
            l.armourShown = true;
            l.armourTrack = Mathf.Clamp01(capacity / maxHealth);
            l.armour = Mathf.Clamp01(held / maxHealth);
            l.armourOfTrack = held / capacity;
            return l;
        }
    }
}
