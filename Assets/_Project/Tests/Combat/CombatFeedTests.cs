using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>
    /// What one body earned in a fight reaches that body only: on the host the human and every bot each hear their own damage,
    /// takedowns, credit and shield-ending hits, never each other's.
    /// </summary>
    public class CombatFeedTests
    {
        [TearDown]
        public void Forget() => CombatFeed.ForgetAll();

        [Test]
        public void EachActorHearsOnlyItsOwnNews()
        {
            float mine = 0f, theirs = 0f;
            CombatFeed.For(3).DamageDealt += a => mine += a;
            CombatFeed.For(1001).DamageDealt += a => theirs += a;

            CombatFeed.For(1001).RaiseDamageDealt(12f);

            Assert.AreEqual(0f, mine);
            Assert.AreEqual(12f, theirs);
        }

        [Test]
        public void TheSameActorAlwaysGetsTheSameFeed()
        {
            Assert.AreSame(CombatFeed.For(7), CombatFeed.For(7));
        }

        [Test]
        public void EveryKindOfNewsIsCarried()
        {
            bool? takedownKill = null;
            float creditAmount = 0f;
            int creditTakedown = -1;
            bool? shielded = null;
            var feed = CombatFeed.For(1002);
            feed.Takedown += isKill => takedownKill = isKill;
            feed.Credit += (amount, takedown) => { creditAmount = amount; creditTakedown = takedown; };
            feed.EnemyAffected += (victimShielded, beforeRespawn) => shielded = victimShielded;

            feed.RaiseTakedown(true);
            feed.RaiseCredit(30f, 2);
            feed.RaiseEnemyAffected(true, false);

            Assert.AreEqual(true, takedownKill);
            Assert.AreEqual(30f, creditAmount);
            Assert.AreEqual(2, creditTakedown);
            Assert.AreEqual(true, shielded);
        }
    }
}
