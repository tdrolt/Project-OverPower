using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class DamageResolverTests
    {
        [Test]
        public void ArmorAbsorbsFirst()
        {
            var r = DamageResolver.Resolve(10f, false, health: 100f, armor: 25f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.AreEqual(10f, r.ArmorAbsorbed, 0.001f);
            Assert.AreEqual(0f, r.HealthLost, 0.001f);
            Assert.IsFalse(r.ArmorBroke);
        }

        [Test]
        public void DamageSpillsIntoHealthWhenArmorBreaks()
        {
            var r = DamageResolver.Resolve(40f, false, health: 100f, armor: 25f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.AreEqual(25f, r.ArmorAbsorbed, 0.001f);
            Assert.AreEqual(15f, r.HealthLost, 0.001f);
            Assert.IsTrue(r.ArmorBroke);
        }

        [Test]
        public void IgnoresArmorSkipsTheArmorPool()
        {
            var r = DamageResolver.Resolve(10f, true, health: 100f, armor: 25f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.AreEqual(0f, r.ArmorAbsorbed, 0.001f);
            Assert.AreEqual(10f, r.HealthLost, 0.001f);
        }

        [Test]
        public void VulnerabilityAppliesBeforeArmor()
        {
            // +60% on 20 damage is 32, which breaks 25 armor and spills 7.
            var r = DamageResolver.Resolve(20f, false, health: 100f, armor: 25f,
                                           vulnerability: 0.6f, reduction: 0f);
            Assert.AreEqual(25f, r.ArmorAbsorbed, 0.001f);
            Assert.AreEqual(7f, r.HealthLost, 0.001f);
        }

        [Test]
        public void ReductionAppliesBeforeArmor()
        {
            var r = DamageResolver.Resolve(40f, false, health: 100f, armor: 0f,
                                           vulnerability: 0f, reduction: 0.5f);
            Assert.AreEqual(20f, r.HealthLost, 0.001f);
        }

        [Test]
        public void VulnerabilityAndReductionMultiplyRatherThanCancel()
        {
            // 100 * 1.5 * 0.5 = 75
            var r = DamageResolver.Resolve(100f, true, health: 200f, armor: 0f,
                                           vulnerability: 0.5f, reduction: 0.5f);
            Assert.AreEqual(75f, r.HealthLost, 0.001f);
        }

        [Test]
        public void LethalWhenHealthReachesZero()
        {
            var r = DamageResolver.Resolve(100f, true, health: 100f, armor: 0f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.IsTrue(r.Lethal);
            Assert.AreEqual(100f, r.HealthLost, 0.001f);
        }

        [Test]
        public void OverkillDoesNotReportMoreHealthLostThanTheTargetHad()
        {
            var r = DamageResolver.Resolve(500f, true, health: 30f, armor: 0f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.AreEqual(30f, r.HealthLost, 0.001f);
            Assert.IsTrue(r.Lethal);
        }

        [Test]
        public void FullReductionDealsNothingAndIsNotLethal()
        {
            var r = DamageResolver.Resolve(999f, true, health: 1f, armor: 0f,
                                           vulnerability: 0f, reduction: 1f);
            Assert.AreEqual(0f, r.HealthLost, 0.001f);
            Assert.IsFalse(r.Lethal);
        }
    }
}
