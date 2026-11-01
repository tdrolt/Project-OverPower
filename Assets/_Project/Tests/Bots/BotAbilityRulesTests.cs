using NUnit.Framework;
using Overpower.Bots;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Each build's simple, readable rule for its attachment, movement ability and ultimate (the approved behaviour table).
    /// </summary>
    public class BotAbilityRulesTests
    {
        [Test]
        public void TheShotgunBotFlamesATargetCloseInFront()
        {
            Assert.IsTrue(BotAbilityRules.Flame(true, 4f, 10f, 5f, 30f));
            Assert.IsFalse(BotAbilityRules.Flame(true, 6f, 10f, 5f, 30f), "too far");
            Assert.IsFalse(BotAbilityRules.Flame(true, 4f, 40f, 5f, 30f), "not in front");
            Assert.IsFalse(BotAbilityRules.Flame(false, 4f, 10f, 5f, 30f));
        }

        [Test]
        public void TheShotgunBotZipsInOnlyBetweenItsDistancesHealthyAndWithAClearLine()
        {
            Assert.IsTrue(BotAbilityRules.Zip(true, 12f, 8f, 16f, 0.8f, 0.5f, true));
            Assert.IsFalse(BotAbilityRules.Zip(true, 6f, 8f, 16f, 0.8f, 0.5f, true), "already close");
            Assert.IsFalse(BotAbilityRules.Zip(true, 18f, 8f, 16f, 0.8f, 0.5f, true), "out of reach");
            Assert.IsFalse(BotAbilityRules.Zip(true, 12f, 8f, 16f, 0.4f, 0.5f, true), "too hurt to dive");
            Assert.IsFalse(BotAbilityRules.Zip(true, 12f, 8f, 16f, 0.8f, 0.5f, false), "something in between");
        }

        [Test]
        public void TheShotgunBotDropsItsFenceOnACloseEnemyOrTwoNearOnes()
        {
            Assert.IsTrue(BotAbilityRules.Fence(1, 1));
            Assert.IsTrue(BotAbilityRules.Fence(0, 2));
            Assert.IsFalse(BotAbilityRules.Fence(0, 1));
        }

        [Test]
        public void TheBurstBotPutsUpCoverWhenHurtAgainstADistantTarget()
        {
            Assert.IsTrue(BotAbilityRules.Cover(true, 0.5f, 0.6f, 10f, 6f));
            Assert.IsFalse(BotAbilityRules.Cover(true, 0.7f, 0.6f, 10f, 6f), "still healthy");
            Assert.IsFalse(BotAbilityRules.Cover(true, 0.5f, 0.6f, 4f, 6f), "the target is too close for a wall to help");
            Assert.IsFalse(BotAbilityRules.Cover(false, 0.5f, 0.6f, 10f, 6f), "no fight");
        }

        [Test]
        public void TheBurstBotSprintsOnlyWhenTravellingQuietAndCool()
        {
            Assert.IsTrue(BotAbilityRules.Sprint(true, 5f, 3f, 0.2f, 0.5f));
            Assert.IsFalse(BotAbilityRules.Sprint(false, 5f, 3f, 0.2f, 0.5f), "not going anywhere");
            Assert.IsFalse(BotAbilityRules.Sprint(true, 1f, 3f, 0.2f, 0.5f), "saw an enemy just now");
            Assert.IsFalse(BotAbilityRules.Sprint(true, 5f, 3f, 0.6f, 0.5f), "too hot");
        }

        [Test]
        public void TheBurstBotHealsItselfOrAHurtTeammateInAFight()
        {
            Assert.IsTrue(BotAbilityRules.HealZone(true, 0.4f, 1f, 0.5f));
            Assert.IsTrue(BotAbilityRules.HealZone(true, 0.9f, 0.4f, 0.5f));
            Assert.IsFalse(BotAbilityRules.HealZone(true, 0.9f, 0.8f, 0.5f));
            Assert.IsFalse(BotAbilityRules.HealZone(false, 0.4f, 1f, 0.5f), "no fight");
        }

        [Test]
        public void TheLaserBotScopesOnlyAtLongRange()
        {
            Assert.IsTrue(BotAbilityRules.Scope(true, 18f, 15f));
            Assert.IsFalse(BotAbilityRules.Scope(true, 12f, 15f));
            Assert.IsFalse(BotAbilityRules.Scope(false, 18f, 15f));
        }

        [Test]
        public void TheLaserBotBlinksAwayOnlyWhenLowWithAnEnemyClose()
        {
            Assert.IsTrue(BotAbilityRules.Blink(0.3f, 0.35f, 6f, 10f));
            Assert.IsFalse(BotAbilityRules.Blink(0.5f, 0.35f, 6f, 10f));
            Assert.IsFalse(BotAbilityRules.Blink(0.3f, 0.35f, 14f, 10f));
        }

        [Test]
        public void TheBlinkGoesStraightAwayFromTheEnemy()
        {
            Vector3 to = BotAbilityRules.AwayFrom(new Vector3(0, 0, 0), new Vector3(3, 0, 0), 9f);
            Assert.AreEqual(-9f, to.x, 1e-4f);
            Assert.AreEqual(0f, to.z, 1e-4f);
        }

        [Test]
        public void TheLaserBotBeamsATargetItHasHadInSight()
        {
            Assert.IsTrue(BotAbilityRules.Raybeam(true, 15f, 20f, 1.2f, 1f));
            Assert.IsFalse(BotAbilityRules.Raybeam(true, 25f, 20f, 1.2f, 1f));
            Assert.IsFalse(BotAbilityRules.Raybeam(true, 15f, 20f, 0.5f, 1f));
        }
    }
}
