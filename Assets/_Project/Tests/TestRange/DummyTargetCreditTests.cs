using System.Reflection;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.TestRange;

namespace Overpower.Tests
{
    /// <summary>
    /// A test-range dummy credits the local player straight away (no RPC), and does so through that player's own CombatFeed, so their ultimate
    /// meter and Zip refill hear it the same way they hear a hit on a real player.
    /// </summary>
    public class DummyTargetCreditTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void ADummyCreditsTheLocalPlayersOwnFeed()
        {
            Assert.IsNotNull(typeof(DummyTarget).GetMethod("NotifyLocalCombatCredit", Any), "DummyTarget.NotifyLocalCombatCredit is where a dummy credits");
            Assert.IsTrue(IlWiring.Uses(typeof(DummyTarget), "NotifyLocalCombatCredit", typeof(CombatFeed).GetMethod(nameof(CombatFeed.For))), "the feed of an actor");
            Assert.IsTrue(IlWiring.Uses(typeof(DummyTarget), "NotifyLocalCombatCredit", typeof(CombatFeed).GetMethod(nameof(CombatFeed.RaiseDamageDealt))),
                "carries the damage dealt");
            Assert.IsTrue(IlWiring.Uses(typeof(DummyTarget), "NotifyLocalCombatCredit", typeof(CombatFeed).GetMethod(nameof(CombatFeed.RaiseTakedown))),
                "and the kill");
        }

        [Test]
        public void ADummyNeverCreditsTheScoreboard()
        {
            Assert.IsFalse(IlWiring.Uses(typeof(DummyTarget), typeof(CombatFeed).GetMethod(nameof(CombatFeed.RaiseCredit))),
                "the scoreboard counts hits on real players only");
        }
    }
}
