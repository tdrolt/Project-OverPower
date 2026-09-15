using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class HealthRegenRuleTests
    {
        [Test]
        public void RegensAtTheTierRateInYourOwnZoneOutOfCombat()
        {
            Assert.AreEqual(10f, HealthRegenRule.RegenPerSecond(true, 10f, 7f, 6f), 1e-5f);
        }

        [Test]
        public void NothingInsideCombat()
        {
            Assert.AreEqual(0f, HealthRegenRule.RegenPerSecond(true, 10f, 5.9f, 6f), 1e-5f);
        }

        [Test]
        public void NothingOutsideYourOwnZone()
        {
            Assert.AreEqual(0f, HealthRegenRule.RegenPerSecond(false, 10f, 60f, 6f), 1e-5f);
        }

        [Test]
        public void NothingOnATierWithoutRegen()
        {
            Assert.AreEqual(0f, HealthRegenRule.RegenPerSecond(true, 0f, 60f, 6f), 1e-5f);
        }

        [Test]
        public void ExactlyAtTheThresholdCounts()
        {
            Assert.AreEqual(4f, HealthRegenRule.RegenPerSecond(true, 4f, 6f, 6f), 1e-5f);
        }
    }
}
