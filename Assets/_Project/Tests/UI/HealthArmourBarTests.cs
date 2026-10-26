using System.Reflection;
using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>The health-and-armour bar's lengths, the same rule for the HUD and the plate over every head. Literal values only.</summary>
    public class HealthArmourBarTests
    {
        private const float Tol = 1e-4f;

        [Test]
        public void NoArmourToHoldDrawsNoArmourAtAll()
        {
            HealthArmourBar.Lengths l = HealthArmourBar.Compute(80f, 100f, 0f, 0f);
            Assert.IsFalse(l.armourShown);
            Assert.AreEqual(0f, l.armourOfCapacity, Tol);
            Assert.AreEqual(0.8f, l.health, Tol);
        }

        [Test]
        public void FullArmourFillsTheWholeArmourBarWhateverItsSize()
        {
            foreach (float capacity in new[] { 25f, 50f, 125f })
            {
                HealthArmourBar.Lengths l = HealthArmourBar.Compute(100f, 100f, capacity, capacity);
                Assert.IsTrue(l.armourShown);
                Assert.AreEqual(1f, l.armourOfCapacity, Tol, capacity + " armour");
            }
        }

        [Test]
        public void ArmourFillsItsBarAsAShareOfWhatCanBeHeld()
        {
            Assert.AreEqual(0.28f, HealthArmourBar.Compute(100f, 100f, 35f, 125f).armourOfCapacity, Tol);
            Assert.AreEqual(0.5f, HealthArmourBar.Compute(100f, 100f, 12.5f, 25f).armourOfCapacity, Tol);
        }

        [Test]
        public void ARaisedCapacityShowsTheNewPartEmptyUntilItRecharges()
        {
            HealthArmourBar.Lengths after = HealthArmourBar.Compute(100f, 100f, 25f, 50f);
            Assert.AreEqual(0.5f, after.armourOfCapacity, Tol);
        }

        [Test]
        public void ArmourAboveItsCapacityNeverOverfillsTheBar()
        {
            Assert.AreEqual(1f, HealthArmourBar.Compute(100f, 100f, 40f, 25f).armourOfCapacity, Tol);
        }

        [Test]
        public void NoMaxHealthDrawsNothing()
        {
            HealthArmourBar.Lengths l = HealthArmourBar.Compute(50f, 0f, 25f, 25f);
            Assert.AreEqual(0f, l.health, Tol);
            Assert.IsFalse(l.armourShown);
        }

        [Test]
        public void HealthIsClampedToTheBar()
        {
            Assert.AreEqual(1f, HealthArmourBar.Compute(120f, 100f, 0f, 0f).health, Tol);
            Assert.AreEqual(0f, HealthArmourBar.Compute(-5f, 100f, 0f, 0f).health, Tol);
        }

        private static MethodInfo Rule => typeof(HealthArmourBar).GetMethod(nameof(HealthArmourBar.Compute));

        [Test]
        public void TheHudDrawsItsBarsFromTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHud), "UpdateHealthAndArmor", Rule));
        }

        [Test]
        public void ThePlateOverEveryHeadDrawsItsBarFromTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "UpdateOverheadBar", Rule));
        }
    }
}
