using System;
using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class AimConeStateTests
    {
        // The real baseline weapon's numbers: a tight 1.5 degree resting cone, blooming fast
        // to a 7 degree ceiling, recovering at 6 degrees/second, with a 1.5x accuracy bonus for
        // standing still.
        private static AimConeState NewState() => new AimConeState(
            minAngle: 1.5f, maxAngle: 7f, bloomPerShot: 0.8f,
            recoveryPerSecond: 6f, standingStillMultiplier: 1.5f);

        [Test]
        public void FreshConeSitsAtMinAngle()
        {
            var c = NewState();
            Assert.AreEqual(1.5f, c.CurrentAngle, 0.001f);
        }

        [Test]
        public void OneShotAddsExactlyBloomPerShot()
        {
            var c = NewState();
            c.RegisterShot();
            Assert.AreEqual(1.5f + 0.8f, c.CurrentAngle, 0.001f);
        }

        [Test]
        public void RepeatedShotsClampAtMaxAngle()
        {
            var c = NewState();
            for (int i = 0; i < 50; i++)
                c.RegisterShot();

            Assert.AreEqual(7f, c.CurrentAngle, 0.001f);
        }

        [Test]
        public void TickRecoversAtRecoveryPerSecond()
        {
            // recoveryPerSecond (6) exceeds maxAngle - minAngle (5.5), so a full 1s tick would
            // always hit the minAngle floor regardless of bloom. Use a short tick that recovers
            // less than the available headroom, so the clamp in test 5 isn't what's measured here.
            var c = NewState();
            c.RegisterShot(); // 1.5 + 0.8 = 2.3

            float before = c.CurrentAngle;
            c.Tick(0.1f, isMoving: false); // recovers 6 * 0.1 = 0.6, well above minAngle

            Assert.AreEqual(before - 0.6f, c.CurrentAngle, 0.001f);
        }

        [Test]
        public void RecoveryNeverGoesBelowMinAngleEvenAfterALongTick()
        {
            var c = NewState();
            c.RegisterShot();
            c.Tick(1000f, isMoving: false);

            Assert.AreEqual(1.5f, c.CurrentAngle, 0.001f);
        }

        [Test]
        public void EffectiveAngleIsCurrentAngleDividedByMultiplierWhenNotMoving()
        {
            var c = NewState();
            c.RegisterShot();
            c.Tick(0f, isMoving: false);

            Assert.AreEqual(c.CurrentAngle / 1.5f, c.EffectiveAngle, 0.001f);
        }

        [Test]
        public void EffectiveAngleEqualsCurrentAngleWhenMoving()
        {
            var c = NewState();
            c.RegisterShot();
            c.Tick(0f, isMoving: true);

            Assert.AreEqual(c.CurrentAngle, c.EffectiveAngle, 0.001f);
        }

        [Test]
        public void RecoveryHappensWhetherOrNotThePlayerIsMoving()
        {
            // isMoving only affects EffectiveAngle, never whether CurrentAngle recovers.
            var stationary = NewState();
            var moving = NewState();
            stationary.RegisterShot();
            moving.RegisterShot();

            stationary.Tick(1f, isMoving: false);
            moving.Tick(1f, isMoving: true);

            Assert.AreEqual(stationary.CurrentAngle, moving.CurrentAngle, 0.001f);
        }

        [Test]
        public void SampleOffsetStaysWithinHalfTheEffectiveAngle()
        {
            var c = NewState();
            c.RegisterShot();
            c.RegisterShot();
            c.Tick(0f, isMoving: false); // establishes EffectiveAngle for this frame, no recovery
            float half = c.EffectiveAngle / 2f;

            var rng = new Random(12345);
            for (int i = 0; i < 1000; i++)
            {
                float offset = c.SampleOffsetDegrees(rng);
                Assert.LessOrEqual(offset, half);
                Assert.GreaterOrEqual(offset, -half);
            }
        }

        [Test]
        public void SameSeedProducesTheSameSequence()
        {
            var c = NewState();
            c.RegisterShot();

            var rngA = new Random(999);
            var rngB = new Random(999);

            for (int i = 0; i < 20; i++)
                Assert.AreEqual(c.SampleOffsetDegrees(rngA), c.SampleOffsetDegrees(rngB), 0.0001f);
        }

        [Test]
        public void ResetReturnsCurrentAngleToMinAngle()
        {
            var c = NewState();
            for (int i = 0; i < 10; i++)
                c.RegisterShot();

            c.Reset();

            Assert.AreEqual(1.5f, c.CurrentAngle, 0.001f);
        }

        // Movement terms. Values chosen so every expected number is exact:
        // min 2, max 10, bloom 1, recovery 1, standing-still 1.5, moving spread 4, moving bloom 3.
        private static AimConeState MovingCone() =>
            new AimConeState(2f, 10f, 1f, 1f, 1.5f, 4f, 3f);

        [Test]
        public void MovingAddsTheFlatSpreadTheInstantMovementStarts()
        {
            var cone = MovingCone();
            cone.Tick(0f, true);
            Assert.AreEqual(6f, cone.EffectiveAngle, 1e-4f); // 2 current + 4 moving spread
        }

        [Test]
        public void StoppingRemovesTheFlatSpreadTheInstantMovementEnds()
        {
            var cone = MovingCone();
            cone.Tick(0f, true);
            cone.Tick(0f, false);
            Assert.AreEqual(2f / 1.5f, cone.EffectiveAngle, 1e-4f);
        }

        [Test]
        public void MovingBloomsAtItsRateMinusRecovery()
        {
            var cone = MovingCone();
            cone.Tick(1f, true);
            Assert.AreEqual(4f, cone.CurrentAngle, 1e-4f); // 2 + (3 - 1) * 1
        }

        [Test]
        public void FiringWhileMovingStacksShotBloomWithMovingBloomAndSpread()
        {
            var cone = MovingCone();
            cone.Tick(0f, true);
            cone.RegisterShot();
            cone.Tick(1f, true);
            Assert.AreEqual(5f, cone.CurrentAngle, 1e-4f); // 2 + 1 (shot) + (3 - 1) * 1 (moving bloom)
            Assert.AreEqual(9f, cone.EffectiveAngle, 1e-4f); // 5 current + 4 moving spread
        }

        [Test]
        public void MovingBloomNeverPassesMaxAngle()
        {
            var cone = MovingCone();
            cone.Tick(10f, true);
            Assert.AreEqual(10f, cone.CurrentAngle, 1e-4f);
            Assert.AreEqual(14f, cone.EffectiveAngle, 1e-4f); // max + moving spread
        }

        [Test]
        public void StandingStillRecoversBloomGainedWhileMoving()
        {
            var cone = MovingCone();
            cone.Tick(1f, true);   // 4
            cone.Tick(1f, false);  // 4 - 1 = 3
            Assert.AreEqual(3f, cone.CurrentAngle, 1e-4f);
        }

        [Test]
        public void FiveArgumentConeHasNoMovementPenalty()
        {
            var cone = new AimConeState(2f, 10f, 1f, 1f, 1.5f);
            cone.Tick(1f, true);
            Assert.AreEqual(2f, cone.CurrentAngle, 1e-4f);
            Assert.AreEqual(2f, cone.EffectiveAngle, 1e-4f);
        }
    }
}
