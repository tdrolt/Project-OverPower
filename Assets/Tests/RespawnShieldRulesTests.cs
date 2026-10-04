using NUnit.Framework;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 1: what the respawn shield blocks and when it ends.</summary>
    public class RespawnShieldRulesTests
    {
        [Test] public void TheShieldIsUpUntilItsEndTimeAndNotAfter()
        {
            Assert.IsTrue(RespawnShieldRules.IsUp(10000, 9999));
            Assert.IsFalse(RespawnShieldRules.IsUp(10000, 10000));
            Assert.IsFalse(RespawnShieldRules.IsUp(10000, 12000));
        }

        [Test] public void ZeroMeansNoShieldWasEverUp() => Assert.IsFalse(RespawnShieldRules.IsUp(0, 5));

        [Test] public void DealingDamageClearsTheShieldToZeroWhichIsDown()
        {
            int cleared = RespawnShieldRules.EndAfterDamageDealt();
            Assert.AreEqual(0, cleared);
            Assert.IsFalse(RespawnShieldRules.IsUp(cleared, 1));
        }

        [Test] public void AnUpShieldBlocksDamageAndADownOneDoesNot()
        {
            Assert.IsTrue(RespawnShieldRules.BlocksDamage(true));
            Assert.IsFalse(RespawnShieldRules.BlocksDamage(false));
        }

        [Test] public void AShieldedPlayerCountsForNoCaptureAndAnUnshieldedOneDoes()
        {
            Assert.IsFalse(RespawnShieldRules.CountsForCapture(true));
            Assert.IsTrue(RespawnShieldRules.CountsForCapture(false));
        }

        [Test] public void CastingAnAbilityWithoutHittingKeepsTheShield() => Assert.IsFalse(RespawnShieldRules.EndsOnAbilityWithoutHit);

        [Test] public void TheShieldCompareWorksAcrossTheIntWrap()
        {
            int end = unchecked(int.MaxValue - 1000 + 5000); // wrapped to negative
            Assert.IsTrue(RespawnShieldRules.IsUp(end, int.MaxValue - 500));
            Assert.IsFalse(RespawnShieldRules.IsUp(end, unchecked(end + 1)));
        }
    }
}
