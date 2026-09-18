using NUnit.Framework;
using Overpower.Weapons;

namespace Overpower.Tests
{
    /// <summary>
    /// Weapon 06's numbers (Assets/Gameplay/Weapons/06 Burst - Charge.asset) as they stand for charge step 2:
    /// 3 rounds uncharged, 5 at full, 2 charge steps, a 0.7 s bar. They are named here as local constants rather
    /// than loaded from the asset ON PURPOSE - this file tests the RULE, and Tudor must stay free to retune the
    /// asset without a test going red (plan rule 8).
    /// </summary>
    public class ChargeCountRuleTests
    {
        private const int Rounds0 = 3;
        private const int RoundsMax = 5;
        private const int Steps = 2;

        private static int RoundsAt(float fraction) =>
            ChargeCountRule.Rounds(Rounds0, RoundsMax, Steps, fraction);

        [Test]
        public void AnOrdinaryClickBuysNoExtraRounds()
        {
            // Tudor's bug: a ~90 ms click on a 0.7 s bar is 13% of the hold and must stay a plain 3-round burst.
            Assert.AreEqual(3, RoundsAt(0.09f / 0.7f));
        }

        [Test]
        public void JustUnderHalfIsStillThree()
        {
            // The whole point of flooring: a step is EARNED by completing it, not by getting close to it.
            Assert.AreEqual(3, RoundsAt(0.4999f));
        }

        [Test]
        public void ExactlyHalfIsFour()
        {
            Assert.AreEqual(4, RoundsAt(0.5f));
        }

        [Test]
        public void JustUnderFullIsStillFour()
        {
            Assert.AreEqual(4, RoundsAt(0.9999f));
        }

        [Test]
        public void AFullHoldIsFive()
        {
            Assert.AreEqual(5, RoundsAt(1f));
        }

        [Test]
        public void HoldingPastFullIsStillFive()
        {
            Assert.AreEqual(5, RoundsAt(4f));
        }

        [Test]
        public void ZeroAndNegativeAndNaNAreAllAPlainBurst()
        {
            // chargeFraction crosses the wire as an RPC parameter, so none of these is impossible.
            Assert.AreEqual(3, RoundsAt(0f));
            Assert.AreEqual(3, RoundsAt(-1f));
            Assert.AreEqual(3, RoundsAt(float.NaN));
            Assert.AreEqual(0f, ChargeCountRule.QuantisedFraction(float.NaN, Steps));
        }

        [Test]
        public void AWeaponThatStacksNoRoundsIgnoresTheChargeEntirely()
        {
            // Weapon 12: canCharge, but Charge Max Projectiles 0 - it charges damage and range, not a count.
            Assert.AreEqual(1, ChargeCountRule.Rounds(1, 0, 0, 1f));
            // And weapons 05 / 07, which cannot charge at all, never reach this rule - guarded again anyway.
            Assert.AreEqual(3, ChargeCountRule.Rounds(3, 3, 0, 1f));
        }

        [Test]
        public void ARoundCountIsNeverZero()
        {
            Assert.AreEqual(1, ChargeCountRule.Rounds(0, 2, 2, 0f));
        }

        [Test]
        public void WithoutStepsTheHoldStaysSmooth()
        {
            Assert.AreEqual(0.37f, ChargeCountRule.QuantisedFraction(0.37f, 0), 1e-5f);
            Assert.AreEqual(1f, ChargeCountRule.QuantisedFraction(2f, 0), 1e-5f);
        }

        [Test]
        public void QuantisedLevelsAreTheOnlyValuesAHoldEverReports()
        {
            Assert.AreEqual(0f, ChargeCountRule.QuantisedFraction(0.49f, Steps), 1e-5f);
            Assert.AreEqual(0.5f, ChargeCountRule.QuantisedFraction(0.5f, Steps), 1e-5f);
            Assert.AreEqual(0.5f, ChargeCountRule.QuantisedFraction(0.99f, Steps), 1e-5f);
            Assert.AreEqual(1f, ChargeCountRule.QuantisedFraction(1f, Steps), 1e-5f);
        }

        [Test]
        public void StepFractionsAreWhereTheRingsTicksGo()
        {
            // The charge ring draws a tick at every internal boundary - one tick, at half, for weapon 06.
            Assert.AreEqual(0f, ChargeCountRule.StepFraction(0, Steps), 1e-5f);
            Assert.AreEqual(0.5f, ChargeCountRule.StepFraction(1, Steps), 1e-5f);
            Assert.AreEqual(1f, ChargeCountRule.StepFraction(2, Steps), 1e-5f);
            Assert.AreEqual(0f, ChargeCountRule.StepFraction(1, 0), 1e-5f);
        }
    }
}
