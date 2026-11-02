using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>
    /// Objective first and a calmer pace: what turns a bot away from its zone, where it never goes (an enemy spawn), how long it keeps a
    /// goal, and when it stands still. Literal numbers made up for the test; Tudor's numbers live in BotConfig.
    /// </summary>
    public class BotFocusRulesTests
    {
        [Test]
        public void ABotOnItsWayToAZoneFightsOnlyATargetCloseToItOrToTheZone()
        {
            Assert.IsTrue(BotFocusRules.ShouldEngage(true, 9f, 10f, 40f, 11f, false, false), "within its range plus a little");
            Assert.IsFalse(BotFocusRules.ShouldEngage(true, 15f, 10f, 40f, 11f, false, false), "far from it and from the zone: it keeps walking");
            Assert.IsTrue(BotFocusRules.ShouldEngage(true, 15f, 10f, 7f, 11f, false, false), "near its zone: it defends the zone");
        }

        [Test]
        public void ABotOnItsWayToAZoneStillFightsBackAndHelpsATeammate()
        {
            Assert.IsTrue(BotFocusRules.ShouldEngage(true, 15f, 10f, 40f, 11f, true, false), "that enemy hit it lately");
            Assert.IsTrue(BotFocusRules.ShouldEngage(true, 15f, 10f, 40f, 11f, false, true), "that enemy is fighting a teammate near it");
        }

        [Test]
        public void ABotWithNoZoneToGoForFightsAnyTarget()
        {
            Assert.IsTrue(BotFocusRules.ShouldEngage(false, 30f, 10f, float.MaxValue, 11f, false, false));
        }

        [Test]
        public void ItLooksWhereAnEnemyWasOnlyNearTheZoneItIsGoingFor()
        {
            Assert.IsTrue(BotFocusRules.ChaseMemory(true, 6f, 11f));
            Assert.IsFalse(BotFocusRules.ChaseMemory(true, 20f, 11f), "away from its zone: no chase");
            Assert.IsTrue(BotFocusRules.ChaseMemory(false, 20f, 11f), "no zone to go for");
        }

        [Test]
        public void AnEnemySpawnIsOffLimitsUnlessTheBotIsPushingThatCapital()
        {
            Assert.IsTrue(BotSpawnRules.OffLimits(true, false));
            Assert.IsFalse(BotSpawnRules.OffLimits(true, true), "it is pushing that capital with its team");
            Assert.IsFalse(BotSpawnRules.OffLimits(false, false));
        }

        [Test]
        public void AChosenGoalIsKeptForTheHoldTime()
        {
            Assert.IsTrue(BotPaceRules.KeepGoal(0.4f, 1.2f, false, false));
            Assert.IsFalse(BotPaceRules.KeepGoal(1.3f, 1.2f, false, false), "held long enough");
        }

        [Test]
        public void AFightStartingOrEndingOrSomethingUrgentChangesTheGoalAtOnce()
        {
            Assert.IsFalse(BotPaceRules.KeepGoal(0.4f, 1.2f, true, false), "a fight started or ended");
            Assert.IsFalse(BotPaceRules.KeepGoal(0.4f, 1.2f, false, true), "its capital or a zone of its team is being taken");
        }

        [Test]
        public void ItStandsStillOnlyWhenAllowedAndTheRollIsUnderTheChance()
        {
            Assert.IsTrue(BotPaceRules.Pause(true, 0.1f, 0.2f));
            Assert.IsFalse(BotPaceRules.Pause(true, 0.3f, 0.2f));
            Assert.IsFalse(BotPaceRules.Pause(false, 0.1f, 0.2f), "busy");
        }
    }
}
