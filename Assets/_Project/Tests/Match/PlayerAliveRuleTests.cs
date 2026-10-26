using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Task 16: PlayerHealth.IsAlive follows the replicated alive state, so a dead player reads dead on every client.</summary>
    public class PlayerAliveRuleTests
    {
        [Test]
        public void ARemoteCopyOfADeadPlayerReadsDeadEvenThoughItsOwnLatchNeverTurnedOn() =>
            Assert.IsFalse(PlayerAliveRule.IsAlive(latchedDead: false, hasLifecycle: true, lifecycleAlive: false));

        [Test]
        public void ALivingPlayerReadsAliveOnEveryClient() =>
            Assert.IsTrue(PlayerAliveRule.IsAlive(latchedDead: false, hasLifecycle: true, lifecycleAlive: true));

        [Test]
        public void TheOwnersLatchStillCountsTheMomentALethalHitLandsBeforeTheSharedFlagFlips() =>
            Assert.IsFalse(PlayerAliveRule.IsAlive(latchedDead: true, hasLifecycle: true, lifecycleAlive: true));

        [Test]
        public void ARespawnedPlayerReadsAliveAgainOnceBothSayAlive() =>
            Assert.IsTrue(PlayerAliveRule.IsAlive(latchedDead: false, hasLifecycle: true, lifecycleAlive: true));

        [Test]
        public void WithNoLifecycleOnTheBodyTheLatchAloneDecides()
        {
            Assert.IsTrue(PlayerAliveRule.IsAlive(latchedDead: false, hasLifecycle: false, lifecycleAlive: false));
            Assert.IsFalse(PlayerAliveRule.IsAlive(latchedDead: true, hasLifecycle: false, lifecycleAlive: false));
        }
    }
}
