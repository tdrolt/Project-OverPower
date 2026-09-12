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
    }
}
