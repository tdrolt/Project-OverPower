using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>
    /// Team play: falling back to regroup when outnumbered, joining a teammate's fight, and the centre scan's dots for the bot's team.
    /// Literal numbers made up for the test; Tudor's numbers live in BotConfig.
    /// </summary>
    public class BotTeamRulesTests
    {
        [Test]
        public void ItIsOutnumberedWhenTheEnemiesNearReachItsTeamPlusTheMargin()
        {
            Assert.IsTrue(BotTeamRules.Outnumbered(2, 1, 1));
            Assert.IsFalse(BotTeamRules.Outnumbered(1, 1, 1), "one on one");
            Assert.IsTrue(BotTeamRules.Outnumbered(3, 2, 1));
            Assert.IsFalse(BotTeamRules.Outnumbered(3, 2, 2), "a larger margin");
        }

        [Test]
        public void OutnumberedItRegroupsButLowHealthBacksOffAndCorneredItFights()
        {
            Assert.AreEqual(BotStance.Regroup, BotTeamRules.Stance(BotStance.Fight, true, false));
            Assert.AreEqual(BotStance.Fight, BotTeamRules.Stance(BotStance.Fight, false, false));
            Assert.AreEqual(BotStance.Retreat, BotTeamRules.Stance(BotStance.Retreat, true, false), "backing off beats regrouping");
            Assert.AreEqual(BotStance.Fight, BotTeamRules.Stance(BotStance.Fight, true, true), "cornered");
        }

        [Test]
        public void AnEnemyNearATeammateNearTheBotIsAFightToJoin()
        {
            Assert.IsTrue(BotTeamRules.FightingNearTeammate(8f, 10f, 12f, 14f));
            Assert.IsFalse(BotTeamRules.FightingNearTeammate(11f, 10f, 12f, 14f), "the teammate is too far from the bot");
            Assert.IsFalse(BotTeamRules.FightingNearTeammate(8f, 10f, 16f, 14f), "the enemy is too far from the teammate");
        }

        [Test]
        public void OnlyATravellingWaveOfItsOwnTeamShowsEnemies()
        {
            Assert.IsTrue(BotTeamRules.ScanShowsUs(true, 1, 1));
            Assert.IsFalse(BotTeamRules.ScanShowsUs(true, 2, 1), "another team held the centre when the wave started");
            Assert.IsFalse(BotTeamRules.ScanShowsUs(true, -1, 1), "a neutral centre shows nobody");
            Assert.IsFalse(BotTeamRules.ScanShowsUs(false, 1, 1), "the wave is past the arena");
            Assert.IsFalse(BotTeamRules.ScanShowsUs(true, -1, -1), "a bot with no team yet");
        }

        [Test]
        public void TheBandSweptStartsWhereTheWaveWasLastTimeOrFromTheCentreOnANewWave()
        {
            Assert.AreEqual(17f, BotTeamRules.ScanSweptFrom(5000, 5000, 17f), 1e-4f);
            Assert.AreEqual(0f, BotTeamRules.ScanSweptFrom(5000, 9000, 17f), 1e-4f);
        }
    }
}
