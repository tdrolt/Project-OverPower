using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Weapons;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Hitscan.ChargedRange is the one home for "how far does a charge weapon's beam reach right
    /// now" - Task 7's AimConeView calls it to draw the range arc rather than re-deriving the
    /// formula, and Hitscan itself calls it to resolve what a shot actually hits. Pinned here so a
    /// future change to either caller can't quietly drift from the other.
    ///
    /// WeaponDefinition is seeded through reflection into its private serialized fields, the same
    /// approach CatalogueTests uses and for the same reason: no test-only setter on the real asset.
    /// </summary>
    public class HitscanChargedRangeTests
    {
        private WeaponDefinition weapon;

        [TearDown]
        public void DestroyWeapon()
        {
            // CreateInstance'd ScriptableObjects are not garbage collected on their own.
            if (weapon != null)
                Object.DestroyImmediate(weapon);
        }

        private static void SetPrivate(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Expected a private field '{name}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private WeaponDefinition NewWeapon(float maxRange, bool canCharge, float chargeRangeMultiplier)
        {
            weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            SetPrivate(weapon, "maxRange", maxRange);
            SetPrivate(weapon, "canCharge", canCharge);
            SetPrivate(weapon, "chargeRangeMultiplier", chargeRangeMultiplier);
            return weapon;
        }

        [Test]
        public void ANonChargingWeaponAlwaysReturnsMaxRange()
        {
            var w = NewWeapon(maxRange: 26f, canCharge: false, chargeRangeMultiplier: 1.6f);

            // The multiplier is set but never read - CanCharge false is the whole gate, matching
            // ProjectileContext, which also never scales range for a weapon that cannot charge.
            Assert.AreEqual(26f, Hitscan.ChargedRange(w, chargeFraction: 0f), 1e-4f);
            Assert.AreEqual(26f, Hitscan.ChargedRange(w, chargeFraction: 1f), 1e-4f);
        }

        [Test]
        public void AChargingWeaponAtZeroFractionReturnsMaxRange()
        {
            var w = NewWeapon(maxRange: 26f, canCharge: true, chargeRangeMultiplier: 1.6f);

            Assert.AreEqual(26f, Hitscan.ChargedRange(w, chargeFraction: 0f), 1e-4f);
        }

        [Test]
        public void AChargingWeaponAtFullFractionReturnsMaxRangeTimesTheMultiplier()
        {
            var w = NewWeapon(maxRange: 26f, canCharge: true, chargeRangeMultiplier: 1.6f);

            Assert.AreEqual(41.6f, Hitscan.ChargedRange(w, chargeFraction: 1f), 1e-4f);
        }

        [Test]
        public void AChargingWeaponRampsLinearlyBetweenTheTwoEnds()
        {
            var w = NewWeapon(maxRange: 26f, canCharge: true, chargeRangeMultiplier: 1.6f);

            // 26 * lerp(1, 1.6, 0.5) = 26 * 1.3 = 33.8 - the exact reading AimConeView's own
            // verification pass measured mid-charge (Task 7).
            Assert.AreEqual(33.8f, Hitscan.ChargedRange(w, chargeFraction: 0.5f), 1e-3f);
        }

        [Test]
        public void ChargeFractionOutsideZeroToOneIsClamped()
        {
            var w = NewWeapon(maxRange: 26f, canCharge: true, chargeRangeMultiplier: 1.6f);

            Assert.AreEqual(26f, Hitscan.ChargedRange(w, chargeFraction: -1f), 1e-4f);
            Assert.AreEqual(41.6f, Hitscan.ChargedRange(w, chargeFraction: 2f), 1e-4f);
        }
    }
}
