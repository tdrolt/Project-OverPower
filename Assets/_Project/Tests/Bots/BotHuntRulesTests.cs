using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>
    /// Finishing a team: a team with no capital left is in its last stand, and its players are hunted wherever the bot's team knows of
    /// them. Literal numbers made up for the test; the real ones live in BotConfig.
    /// </summary>
    public class BotHuntRulesTests
    {
        [Test]
        public void ATeamStillPlayingWithNoCapitalIsInItsLastStand()
        {
            Assert.IsTrue(BotHuntRules.LastStand(true, false, false));
            Assert.IsFalse(BotHuntRules.LastStand(true, false, true), "it still holds a capital");
            Assert.IsFalse(BotHuntRules.LastStand(true, true, false), "already knocked out");
            Assert.IsFalse(BotHuntRules.LastStand(false, false, false), "not a team of this match");
        }

        [Test]
        public void APlayerOfATeamInItsLastStandIsHuntedWhileItsTeamKnowsWhereItIs()
        {
            Assert.IsTrue(BotHuntRules.Hunted(true, 0f, 6f), "in sight now");
            Assert.IsTrue(BotHuntRules.Hunted(true, 5.5f, 6f), "seen a moment ago");
            Assert.IsFalse(BotHuntRules.Hunted(true, 6.5f, 6f), "lost track of it");
            Assert.IsFalse(BotHuntRules.Hunted(false, 0f, 6f), "its team still has a capital");
        }
    }
}
