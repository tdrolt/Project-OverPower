using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>
    /// Spreading out at the start and when pushed back to Tier II with nobody to defend against, and holding the zone picked a moment.
    /// Literal numbers made up for the test; the real ones live in BotConfig.
    /// </summary>
    public class BotSpreadRulesTests
    {
        [Test]
        public void ATeamHoldingNothingAboveTierTwoIsPushedBack()
        {
            Assert.IsTrue(BotSpreadRules.PushedBack(1), "only its capital");
            Assert.IsTrue(BotSpreadRules.PushedBack(2));
            Assert.IsFalse(BotSpreadRules.PushedBack(3), "it holds a Tier III");
        }

        [Test]
        public void BotsSpreadOutAtTheStartAndWhenPushedBackWithNothingToDefend()
        {
            Assert.IsTrue(BotSpreadRules.Spreading(true, false, true), "the start");
            Assert.IsTrue(BotSpreadRules.Spreading(false, true, false));
            Assert.IsFalse(BotSpreadRules.Spreading(false, true, true), "a zone of its team is being taken: defend it together");
            Assert.IsFalse(BotSpreadRules.Spreading(false, false, false));
        }

        [Test]
        public void TheZonePickedIsHeldAMomentWhileItIsStillOpenAndNothingIsUrgent()
        {
            Assert.IsTrue(BotSpreadRules.HoldZone(1f, 2.5f, true, false));
            Assert.IsFalse(BotSpreadRules.HoldZone(3f, 2.5f, true, false), "held long enough");
            Assert.IsFalse(BotSpreadRules.HoldZone(1f, 2.5f, false, false), "taken already, or closed");
            Assert.IsFalse(BotSpreadRules.HoldZone(1f, 2.5f, true, true), "something urgent came up");
        }
    }
}
