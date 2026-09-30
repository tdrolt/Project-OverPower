using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class UltimateChargeStateTests
    {
        // The plan's own numbers (Task 1.11 table): max 1000, 1.0/damage dealt, 0.5/damage taken,
        // 150/kill, 75/assist.
        private static UltimateChargeState NewState() =>
            new UltimateChargeState(maxCharge: 1000f, perDamageDealt: 1f, perDamageTaken: 0.5f,
                                     perKill: 150f, perAssist: 75f);

        [Test]
        public void NewStateStartsEmpty()
        {
            var s = NewState();

            Assert.AreEqual(0f, s.Current);
            Assert.IsFalse(s.IsFull);
            Assert.AreEqual(0f, s.Normalised, 0.0001f);
        }

        [Test]
        public void DamageDealtAccruesAtItsOwnRate()
        {
            var s = NewState();
            s.AddDamageDealt(100f);

            Assert.AreEqual(100f, s.Current, 0.001f); // 1.0 per point
        }

        [Test]
        public void DamageTakenAccruesAtHalfTheDealtRate()
        {
            var s = NewState();
            s.AddDamageTaken(100f);

            Assert.AreEqual(50f, s.Current, 0.001f); // 0.5 per point
        }

        [Test]
        public void AKillAddsTheFlatKillAmount()
        {
            var s = NewState();
            s.AddKill();

            Assert.AreEqual(150f, s.Current, 0.001f);
        }

        [Test]
        public void AnAssistAddsTheFlatAssistAmount()
        {
            var s = NewState();
            s.AddAssist();

            Assert.AreEqual(75f, s.Current, 0.001f);
        }

        [Test]
        public void SourcesAccumulateTogether()
        {
            var s = NewState();
            s.AddDamageDealt(200f);   // +200
            s.AddDamageTaken(100f);   // +50
            s.AddKill();              // +150

            Assert.AreEqual(400f, s.Current, 0.001f);
        }

        [Test]
        public void ChargeClampsAtTheCap()
        {
            var s = NewState();
            for (int i = 0; i < 8; i++)
                s.AddKill(); // 8 * 150 = 1200, past the 1000 cap

            Assert.AreEqual(1000f, s.Current, 0.001f);
            Assert.IsTrue(s.IsFull);
        }

        [Test]
        public void ZeroOrNegativeAmountsAddNothing()
        {
            var s = NewState();
            s.AddDamageDealt(0f);
            s.AddDamageDealt(-50f);

            Assert.AreEqual(0f, s.Current);
        }

        [Test]
        public void SpendIsRefusedWhenNotFull()
        {
            var s = NewState();
            s.AddDamageDealt(500f);

            bool spent = s.Spend();

            Assert.IsFalse(spent);
            Assert.AreEqual(500f, s.Current, 0.001f); // untouched by the refused spend
        }

        [Test]
        public void SpendEmptiesTheMeterWhenFull()
        {
            var s = NewState();
            s.Fill();

            bool spent = s.Spend();

            Assert.IsTrue(spent);
            Assert.AreEqual(0f, s.Current);
            Assert.IsFalse(s.IsFull);
        }

        [Test]
        public void FillSetsTheMeterToTheCap()
        {
            var s = NewState();
            s.Fill();

            Assert.AreEqual(1000f, s.Current, 0.001f);
            Assert.IsTrue(s.IsFull);
        }

        [Test]
        public void NormalisedIsHalfwayAtHalfTheCap()
        {
            var s = NewState();
            s.AddDamageDealt(500f);

            Assert.AreEqual(0.5f, s.Normalised, 0.001f);
        }

        [Test]
        public void RetuneLoweringTheCapClampsCurrentDown()
        {
            var s = NewState();
            s.AddDamageDealt(800f);

            s.Retune(newMaxCharge: 500f, newPerDamageDealt: 1f, newPerDamageTaken: 0.5f,
                     newPerKill: 150f, newPerAssist: 75f);

            Assert.AreEqual(500f, s.Current, 0.001f);
            Assert.IsTrue(s.IsFull);
        }

        [Test]
        public void RetuneChangesTheRateForFutureAccrual()
        {
            var s = NewState();
            s.Retune(newMaxCharge: 1000f, newPerDamageDealt: 2f, newPerDamageTaken: 0.5f,
                     newPerKill: 150f, newPerAssist: 75f);

            s.AddDamageDealt(100f);

            Assert.AreEqual(200f, s.Current, 0.001f);
        }

        [Test]
        public void ClearEmptiesTheMeterForAFreshStart()
        {
            // 2.7b step 4: the owner-side fresh start (Decision 6) empties the ultimate meter.
            var s = NewState();
            s.Fill();

            s.Clear();

            Assert.AreEqual(0f, s.Current);
            Assert.IsFalse(s.IsFull);
        }
    }
}
