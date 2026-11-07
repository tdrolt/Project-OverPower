using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>
    /// Capturing comes first: which zone is free to take, staying on a free zone until it is taken, and the only fights on the way to one.
    /// Literal numbers made up for the test; the real ones live in BotConfig.
    /// </summary>
    public class BotCaptureRulesTests
    {
        [Test]
        public void AFreeZoneIsOneItsTeamMayTakeNowThatIsNoCapitalWithNoEnemyOnIt()
        {
            Assert.IsTrue(BotCaptureRules.FreeZone(true, false, false));
            Assert.IsFalse(BotCaptureRules.FreeZone(false, false, false), "its team may not take it now (or holds it)");
            Assert.IsFalse(BotCaptureRules.FreeZone(true, true, false), "a capital, or the Dominion centre");
            Assert.IsFalse(BotCaptureRules.FreeZone(true, false, true), "an enemy is on it");
        }

        [Test]
        public void ABotInAZonesCircleStaysThereWhileTheZoneIsFree()
        {
            Assert.IsTrue(BotCaptureRules.StayOnZone(true, true));
            Assert.IsFalse(BotCaptureRules.StayOnZone(false, true), "not on it yet");
            Assert.IsFalse(BotCaptureRules.StayOnZone(true, false), "taken already, or an enemy stepped on");
        }

        [Test]
        public void OnItsWayToAFreeZoneItFightsOnlyAnEnemyThatHitItOrIsInWeaponRange()
        {
            Assert.IsFalse(BotCaptureRules.EngageOnFreeZone(false, 20f, 15f), "out of range and it did not shoot: it keeps going");
            Assert.IsTrue(BotCaptureRules.EngageOnFreeZone(true, 20f, 15f), "it hit the bot lately");
            Assert.IsTrue(BotCaptureRules.EngageOnFreeZone(false, 15f, 15f), "at the edge of weapon range");
            Assert.IsTrue(BotCaptureRules.EngageOnFreeZone(false, 6f, 15f));
        }
    }
}
