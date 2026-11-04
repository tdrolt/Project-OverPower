using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>A hurt bot walks into a teammate's Heal Zone close by. Literal numbers made up for the test; the real ones live in BotConfig.</summary>
    public class BotHealZoneRulesTests
    {
        [Test]
        public void AHurtBotWalksIntoATeammatesHealZoneCloseBy()
        {
            Assert.IsTrue(BotHealZoneRules.WalkIn(true, 0.4f, 0.55f, 10f, 12f));
            Assert.IsFalse(BotHealZoneRules.WalkIn(true, 0.6f, 0.55f, 10f, 12f), "healthy enough");
            Assert.IsFalse(BotHealZoneRules.WalkIn(true, 0.4f, 0.55f, 13f, 12f), "too far away");
            Assert.IsFalse(BotHealZoneRules.WalkIn(false, 0.4f, 0.55f, 10f, 12f), "an enemy's zone, or its own");
        }
    }
}
