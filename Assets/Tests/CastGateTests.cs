using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class CastGateTests
    {
        [Test]
        public void AllClearReturnsNone()
        {
            Assert.AreEqual(CastBlock.None, CastGate.ForActor(alive: true, stunned: false, silenced: false));
        }

        [Test]
        public void DeadWinsOverStunnedAndSilenced()
        {
            Assert.AreEqual(CastBlock.Dead, CastGate.ForActor(alive: false, stunned: true, silenced: true));
        }

        [Test]
        public void StunnedWinsOverSilencedWhenAlive()
        {
            Assert.AreEqual(CastBlock.Stunned, CastGate.ForActor(alive: true, stunned: true, silenced: true));
        }

        [Test]
        public void SilencedAloneIsReported()
        {
            Assert.AreEqual(CastBlock.Silenced, CastGate.ForActor(alive: true, stunned: false, silenced: true));
        }

        [Test]
        public void ForAbilityPropagatesTheActorBlockOverEverythingElse()
        {
            // Even an ability with no charge left and its own IsReady false must still read as
            // Dead - the actor-wide reason always outranks the ability-specific ones.
            CastBlock result = CastGate.ForAbility(CastBlock.Dead, hasChargeGate: true, hasCharge: false, isReady: false);
            Assert.AreEqual(CastBlock.Dead, result);
        }

        [Test]
        public void RechargingWinsOverNotReadyWhenBothApply()
        {
            CastBlock result = CastGate.ForAbility(CastBlock.None, hasChargeGate: true, hasCharge: false, isReady: false);
            Assert.AreEqual(CastBlock.Recharging, result);
        }

        [Test]
        public void NotReadyReportedWhenChargeIsFineButTheModuleIsNot()
        {
            CastBlock result = CastGate.ForAbility(CastBlock.None, hasChargeGate: true, hasCharge: true, isReady: false);
            Assert.AreEqual(CastBlock.NotReady, result);
        }

        [Test]
        public void EverythingClearReturnsNone()
        {
            CastBlock result = CastGate.ForAbility(CastBlock.None, hasChargeGate: true, hasCharge: true, isReady: true);
            Assert.AreEqual(CastBlock.None, result);
        }

        [Test]
        public void AModuleWithNoChargeGateNeverReportsRecharging()
        {
            // Sprint has no ChargePool at all - hasCharge being false must not matter when there
            // was never a charge gate to fail in the first place.
            CastBlock result = CastGate.ForAbility(CastBlock.None, hasChargeGate: false, hasCharge: false, isReady: true);
            Assert.AreEqual(CastBlock.None, result);
        }

        [Test]
        public void AModuleWithNoChargeGateStillHonoursIsReady()
        {
            CastBlock result = CastGate.ForAbility(CastBlock.None, hasChargeGate: false, hasCharge: false, isReady: false);
            Assert.AreEqual(CastBlock.NotReady, result);
        }

        // ---- PressBuffer ----

        [Test]
        public void ANewBufferWithNoPressIsNeverPending()
        {
            var buffer = new CastGate.PressBuffer();
            Assert.IsFalse(buffer.IsPending(0f, 1f));
        }

        [Test]
        public void APressIsPendingInsideItsWindow()
        {
            var buffer = new CastGate.PressBuffer();
            buffer.Press(1.00f);

            Assert.IsTrue(buffer.IsPending(1.05f, 0.12f));
        }

        [Test]
        public void APressExpiresOnceTheWindowHasPassed()
        {
            var buffer = new CastGate.PressBuffer();
            buffer.Press(1.00f);

            Assert.IsFalse(buffer.IsPending(1.13f, 0.12f));
        }

        [Test]
        public void TryConsumeSucceedsExactlyOnce()
        {
            var buffer = new CastGate.PressBuffer();
            buffer.Press(1.00f);

            Assert.IsTrue(buffer.TryConsume(1.05f, 0.12f));
            Assert.IsFalse(buffer.TryConsume(1.06f, 0.12f));
        }

        [Test]
        public void ConsumingAPressAlsoClearsItsPendingState()
        {
            var buffer = new CastGate.PressBuffer();
            buffer.Press(1.00f);
            buffer.TryConsume(1.05f, 0.12f);

            Assert.IsFalse(buffer.IsPending(1.06f, 0.12f));
        }

        [Test]
        public void ASecondPressRestartsTheWindowEvenAfterTheFirstExpired()
        {
            var buffer = new CastGate.PressBuffer();
            buffer.Press(1.00f);
            // Let the first press expire unconsumed.
            Assert.IsFalse(buffer.IsPending(1.20f, 0.12f));

            buffer.Press(1.25f);

            Assert.IsTrue(buffer.IsPending(1.30f, 0.12f));
        }

        [Test]
        public void ASecondPressIsConsumableAgainEvenIfTheFirstWasAlreadyConsumed()
        {
            var buffer = new CastGate.PressBuffer();
            buffer.Press(1.00f);
            buffer.TryConsume(1.01f, 0.12f);

            buffer.Press(1.10f);

            Assert.IsTrue(buffer.TryConsume(1.11f, 0.12f));
        }

        [Test]
        public void AZeroWindowIsPendingOnlyOnTheExactPressFrame()
        {
            var buffer = new CastGate.PressBuffer();
            buffer.Press(2.00f);

            Assert.IsTrue(buffer.IsPending(2.00f, 0f));
            Assert.IsFalse(buffer.IsPending(2.0001f, 0f));
        }
    }
}
