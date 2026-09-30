using System.Reflection;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Data;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Exercises the upgrade rule against the REAL ArmorConfig defaults (absorb 25/50/100, recharge
    /// 6/4/2s, a combined cap of 2) rather than a hand-rolled stand-in, so a change to those field
    /// initializers is caught here too. SetMaxUpgrades reaches into the private field the same way
    /// CatalogueTests does, for the one test that needs to move the cap.
    /// </summary>
    public class ArmorUpgradePathTests
    {
        private ArmorConfig config;

        [SetUp]
        public void CreateConfig() => config = ScriptableObject.CreateInstance<ArmorConfig>();

        [TearDown]
        public void DestroyConfig() => Object.DestroyImmediate(config);

        private static void SetMaxUpgrades(ArmorConfig cfg, int value)
        {
            FieldInfo field = typeof(ArmorConfig).GetField(
                "maxArmorUpgrades", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field,
                "Expected a private field 'maxArmorUpgrades' on ArmorConfig. If it was renamed, " +
                "update this helper rather than widening the field's access.");

            field.SetValue(cfg, value);
        }

        [Test]
        public void ANewPathStartsAtLevelZeroOnBothPaths()
        {
            var path = new ArmorUpgradePath(config);

            Assert.AreEqual(0, path.AbsorbLevel);
            Assert.AreEqual(0, path.RechargeLevel);
            Assert.AreEqual(25f, path.Capacity, 0.001f);
            Assert.AreEqual(6f, path.RechargeDelaySeconds, 0.001f);
        }

        [Test]
        public void TwoAbsorbUpgradesGiveFullCapacityAtTheStartingRechargeDelay()
        {
            var path = new ArmorUpgradePath(config);

            Assert.IsTrue(path.TryUpgradeAbsorb());
            Assert.IsTrue(path.TryUpgradeAbsorb());

            Assert.AreEqual(100f, path.Capacity, 0.001f);
            Assert.AreEqual(6f, path.RechargeDelaySeconds, 0.001f);
        }

        [Test]
        public void TwoRechargeUpgradesGiveTheFastestDelayAtTheStartingCapacity()
        {
            var path = new ArmorUpgradePath(config);

            Assert.IsTrue(path.TryUpgradeRecharge());
            Assert.IsTrue(path.TryUpgradeRecharge());

            Assert.AreEqual(25f, path.Capacity, 0.001f);
            Assert.AreEqual(2f, path.RechargeDelaySeconds, 0.001f);
        }

        [Test]
        public void OneOfEachUpgradeGivesTheMiddleOfBothPaths()
        {
            var path = new ArmorUpgradePath(config);

            Assert.IsTrue(path.TryUpgradeAbsorb());
            Assert.IsTrue(path.TryUpgradeRecharge());

            Assert.AreEqual(50f, path.Capacity, 0.001f);
            Assert.AreEqual(4f, path.RechargeDelaySeconds, 0.001f);
        }

        [Test]
        public void AThirdUpgradeIsRefusedWhileTheConfigCapsAtTwo()
        {
            var path = new ArmorUpgradePath(config);
            path.TryUpgradeAbsorb();
            path.TryUpgradeAbsorb();

            bool third = path.TryUpgradeAbsorb();

            Assert.IsFalse(third);
            Assert.AreEqual(2, path.AbsorbLevel);
            Assert.AreEqual(100f, path.Capacity, 0.001f);
        }

        [Test]
        public void AThirdUpgradeIsAllowedOnceTheConfigCapIsRaisedToThree()
        {
            SetMaxUpgrades(config, 3);
            var path = new ArmorUpgradePath(config);
            path.TryUpgradeAbsorb();
            path.TryUpgradeAbsorb();

            bool third = path.TryUpgradeRecharge();

            Assert.IsTrue(third);
            Assert.AreEqual(1, path.RechargeLevel);
            Assert.AreEqual(4f, path.RechargeDelaySeconds, 0.001f);
        }

        [Test]
        public void TheCombinedCapIsSharedAcrossBothPathsNotOnePerPath()
        {
            // Two absorb upgrades already spend the whole cap of 2, so recharge is refused too -
            // this is the GDD's "one choice each time", not two separate currencies.
            var path = new ArmorUpgradePath(config);
            path.TryUpgradeAbsorb();
            path.TryUpgradeAbsorb();

            Assert.IsFalse(path.TryUpgradeRecharge());
            Assert.AreEqual(0, path.RechargeLevel);
        }

        [Test]
        public void AnUpgradeIsRefusedPastThePathsOwnTopLevelEvenIfTheCombinedCapAllowsMore()
        {
            SetMaxUpgrades(config, 10); // Far above the array length, so only the array limits this.
            var path = new ArmorUpgradePath(config);
            path.TryUpgradeAbsorb();
            path.TryUpgradeAbsorb();

            bool third = path.TryUpgradeAbsorb(); // absorbLevels has only 3 entries: indices 0, 1, 2

            Assert.IsFalse(third);
            Assert.AreEqual(2, path.AbsorbLevel);
        }

        [Test]
        public void ResetLevelsReturnsBothPathsToZero()
        {
            var path = new ArmorUpgradePath(config);
            path.TryUpgradeAbsorb();
            path.TryUpgradeRecharge();

            path.ResetLevels();

            Assert.AreEqual(0, path.AbsorbLevel);
            Assert.AreEqual(0, path.RechargeLevel);
            Assert.AreEqual(25f, path.Capacity, 0.001f);
            Assert.AreEqual(6f, path.RechargeDelaySeconds, 0.001f);
        }

        [Test]
        public void StartingAtNonZeroLevelsRestoresThemUnchanged()
        {
            // PlayerHealth builds one of these from replicated Custom Property values for a late
            // joiner - it must not silently reset a remote player's levels back to 0.
            var path = new ArmorUpgradePath(config, absorbLevel: 2, rechargeLevel: 1);

            Assert.AreEqual(2, path.AbsorbLevel);
            Assert.AreEqual(1, path.RechargeLevel);
            Assert.AreEqual(100f, path.Capacity, 0.001f);
            Assert.AreEqual(4f, path.RechargeDelaySeconds, 0.001f);
        }

        [Test]
        public void ConstructorClampsAnOutOfRangeStartingLevelIntoTheConfigsOwnArrayLength()
        {
            // A malformed or stale replicated level (this can arrive over the network) must never
            // leave TotalUpgrades reading higher than the config can support - that would silently
            // refuse every future upgrade for this player, since TotalUpgrades would already look
            // like it exceeded MaxArmorUpgrades.
            var path = new ArmorUpgradePath(config, absorbLevel: 99, rechargeLevel: 99);

            Assert.AreEqual(2, path.AbsorbLevel);   // absorbLevels has 3 entries: indices 0, 1, 2
            Assert.AreEqual(2, path.RechargeLevel); // rechargeSeconds likewise
        }

        [Test]
        public void ANullConfigRefusesEveryUpgradeAndReadsAsZero()
        {
            var path = new ArmorUpgradePath(null);

            Assert.IsFalse(path.TryUpgradeAbsorb());
            Assert.IsFalse(path.TryUpgradeRecharge());
            Assert.AreEqual(0f, path.Capacity, 0.001f);
            Assert.AreEqual(0f, path.RechargeDelaySeconds, 0.001f);
        }
    }
}
