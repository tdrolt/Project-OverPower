using System.Text;
using NUnit.Framework;
using Overpower.Data;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>
    /// Relationship checks over the 13 real weapon assets - the rules WeaponDefinition.OnValidate only WARNS about
    /// in the Console, where nobody reads them (charge step 2, which moved every laser's wind-up to within 0.1 s of
    /// its fire interval).
    ///
    /// Deliberately pins no tuning number: every assert here is "A must stay below B", never "A must be 0.6", so
    /// Tudor can retune any weapon from the Inspector without a test going red. A test that fails here means a
    /// weapon is genuinely misconfigured, not merely re-tuned.
    /// </summary>
    public class WeaponConfigRuleTests
    {
        private static WeaponCatalogue Catalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WeaponCatalogue>(
                "Assets/Gameplay/Weapons/WeaponCatalogue.asset");
            Assert.NotNull(catalogue, "Expected the real WeaponCatalogue at Assets/Gameplay/Weapons/WeaponCatalogue.asset - if it moved, update this path.");
            return catalogue;
        }

        [Test]
        public void EveryWindUpEndsBeforeItsOwnWeaponCanFireAgain()
        {
            // Task 11b's rule: a wind-up at or past the fire interval lets a second pull start its own warning line
            // before the first beam has fired, and two overlapping warnings from one shooter read as a glitch.
            var bad = new StringBuilder();
            foreach (WeaponDefinition weapon in Catalogue().Weapons)
            {
                if (weapon.WindupSeconds > 0f && weapon.WindupSeconds >= weapon.FireInterval)
                    bad.Append($"\n{weapon.name}: windup {weapon.WindupSeconds} >= fire interval {weapon.FireInterval}");
            }
            Assert.IsEmpty(bad.ToString(), "Lower Windup Seconds below Fire Interval, or raise Fire Interval:" + bad);
        }

        [Test]
        public void EveryChargingWeaponHasAChargeTimeToReach()
        {
            // canCharge with Max Charge Seconds at 0 makes ChargeFraction() return 0 forever (WeaponFiring:525),
            // so every charge field on that weapon silently does nothing.
            var bad = new StringBuilder();
            foreach (WeaponDefinition weapon in Catalogue().Weapons)
            {
                if (weapon.CanCharge && weapon.MaxChargeSeconds <= 0f)
                    bad.Append($"\n{weapon.name}: canCharge is on but Max Charge Seconds is {weapon.MaxChargeSeconds}");
            }
            Assert.IsEmpty(bad.ToString(), "Set Max Charge Seconds, or turn Can Charge off:" + bad);
        }

        [Test]
        public void AWeaponThatStacksRoundsHasAStepForEachOne()
        {
            // Charge Steps that do not match the round span make a lopsided ramp (see ChargeCountRule.Rounds):
            // with 2 steps across 3 -> 5 rounds every step is exactly one more round, which is what a player counts.
            var bad = new StringBuilder();
            foreach (WeaponDefinition weapon in Catalogue().Weapons)
            {
                if (!weapon.CanCharge || weapon.ChargeMaxProjectiles <= weapon.ProjectilesPerShot)
                    continue;
                int span = weapon.ChargeMaxProjectiles - weapon.ProjectilesPerShot;
                if (weapon.ChargeSteps != span)
                    bad.Append($"\n{weapon.name}: {span} extra rounds across {weapon.ChargeSteps} charge steps");
            }
            Assert.IsEmpty(bad.ToString(), "Set Charge Steps to Charge Max Projectiles minus Projectiles Per Shot:" + bad);
        }
    }
}
