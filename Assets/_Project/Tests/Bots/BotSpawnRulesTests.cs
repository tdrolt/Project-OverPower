using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>Spawns: an enemy firing from its own spawn is shot back, and only Dominion keeps a bot out of an enemy spawn.</summary>
    public class BotSpawnRulesTests
    {
        [Test]
        public void AnEnemyInItsSpawnIsLeftAloneUntilItShootsTheBot()
        {
            Assert.IsTrue(BotSpawnRules.LeaveAlone(true, false));
            Assert.IsFalse(BotSpawnRules.LeaveAlone(true, true), "it hit the bot lately: the bot shoots back");
            Assert.IsFalse(BotSpawnRules.LeaveAlone(false, false), "not in its spawn");
        }

        [Test]
        public void OnlyDominionKeepsABotOutOfAnEnemySpawn()
        {
            Assert.IsTrue(BotSpawnRules.KeepOut(true, true));
            Assert.IsFalse(BotSpawnRules.KeepOut(false, true), "Conquest: pushing a team is fine");
            Assert.IsFalse(BotSpawnRules.KeepOut(true, false));
        }
    }
}
