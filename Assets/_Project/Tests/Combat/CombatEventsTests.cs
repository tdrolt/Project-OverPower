using System.Reflection;
using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>
    /// CombatEvents is the screen of the human at this keyboard only. What a body earns goes through CombatFeed.For(actor): one static
    /// "local" event would charge the host's human and every bot it plays from each other's hits.
    /// </summary>
    public class CombatEventsTests
    {
        [Test]
        public void NothingABodyEarnsIsAStaticLocalEvent()
        {
            const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string earned in new[] { "LocalDamageDealt", "LocalTakedown", "LocalPlayerCredit", "LocalEnemyAffected" })
                Assert.IsNull(typeof(CombatEvents).GetEvent(earned, Statics), earned + " belongs on CombatFeed");
            foreach (string raise in new[] { "RaiseDamageDealt", "RaiseTakedown", "RaisePlayerCredit", "RaiseEnemyAffected" })
                Assert.IsNull(typeof(CombatEvents).GetMethod(raise, Statics), raise + " belongs on CombatFeed");
        }

        [Test]
        public void EveryKindOfEarnedNewsHasAHomeOnTheFeed()
        {
            foreach (string earned in new[] { nameof(CombatFeed.DamageDealt), nameof(CombatFeed.Takedown), nameof(CombatFeed.Credit), nameof(CombatFeed.EnemyAffected) })
                Assert.IsNotNull(typeof(CombatFeed).GetEvent(earned), earned);
        }
    }
}
