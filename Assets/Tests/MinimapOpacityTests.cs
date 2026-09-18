using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class MinimapOpacityTests
    {
        private const float Corner = 0.8f;
        private const float Large = 1f;
        private const float Drop = 0.3f;

        [Test]
        public void TheCornerMapSitsAtTheCornerOpacity()
        {
            Assert.AreEqual(0.8f, MinimapOpacity.TargetAlpha(false, false, Corner, Large, Drop), 1e-5f);
        }

        [Test]
        public void MovingDoesNotDimTheCornerMap()
        {
            Assert.AreEqual(0.8f, MinimapOpacity.TargetAlpha(false, true, Corner, Large, Drop), 1e-5f);
        }

        [Test]
        public void TheLargeMapIsFullyOpaqueWhenStandingStill()
        {
            Assert.AreEqual(1f, MinimapOpacity.TargetAlpha(true, false, Corner, Large, Drop), 1e-5f);
        }

        [Test]
        public void MovingWithTheLargeMapOpenDropsItByTheDrop()
        {
            Assert.AreEqual(0.7f, MinimapOpacity.TargetAlpha(true, true, Corner, Large, Drop), 1e-5f);
        }

        [Test]
        public void ADropBiggerThanTheOpacityClampsAtInvisibleRatherThanGoingNegative()
        {
            Assert.AreEqual(0f, MinimapOpacity.TargetAlpha(true, true, Corner, 0.2f, 0.9f), 1e-5f);
        }

        [Test]
        public void MovingStartsOnlyAboveTheEnterSpeed()
        {
            Assert.IsTrue(MinimapOpacity.IsMoving(false, 1.2f, 1f, 0.35f));
            Assert.IsFalse(MinimapOpacity.IsMoving(false, 0.9f, 1f, 0.35f));
        }

        [Test]
        public void MovingStopsOnlyBelowTheExitSpeed()
        {
            Assert.IsTrue(MinimapOpacity.IsMoving(true, 0.5f, 1f, 0.35f));
            Assert.IsFalse(MinimapOpacity.IsMoving(true, 0.3f, 1f, 0.35f));
        }

        [Test]
        public void InsideTheDeadzoneTheAnswerNeverChanges()
        {
            // The whole point: a player hovering at 0.6 m/s gets the state they already had, both ways round,
            // so the map cannot strobe between them.
            Assert.IsTrue(MinimapOpacity.IsMoving(true, 0.6f, 1f, 0.35f));
            Assert.IsFalse(MinimapOpacity.IsMoving(false, 0.6f, 1f, 0.35f));
        }

        [Test]
        public void TheSpeedsSwappedRoundStillBehaveTheSameWay()
        {
            Assert.IsTrue(MinimapOpacity.IsMoving(false, 1.2f, 0.35f, 1f));
            Assert.IsFalse(MinimapOpacity.IsMoving(true, 0.3f, 0.35f, 1f));
        }

        [Test]
        public void AFullFadeTakesExactlyFadeSeconds()
        {
            float a = 0f;
            for (int i = 0; i < 25; i++) // 25 x 0.01s = 0.25s
                a = MinimapOpacity.Step(a, 1f, 0.01f, 0.25f);
            Assert.AreEqual(1f, a, 1e-4f);
        }

        [Test]
        public void TheFadeNeverOvershootsItsTarget()
        {
            Assert.AreEqual(0.7f, MinimapOpacity.Step(0.69f, 0.7f, 1f, 0.25f), 1e-5f);
            Assert.AreEqual(0.7f, MinimapOpacity.Step(0.71f, 0.7f, 1f, 0.25f), 1e-5f);
        }

        [Test]
        public void AZeroFadeSnaps()
        {
            Assert.AreEqual(0.7f, MinimapOpacity.Step(0f, 0.7f, 0.016f, 0f), 1e-5f);
        }

        [Test]
        public void SmoothingConvergesOnTheSampleAndZeroSmoothingIsTheRawSample()
        {
            Assert.AreEqual(4f, MinimapOpacity.SmoothSpeed(0f, 4f, 0.016f, 0f), 1e-5f);

            float s = 0f;
            for (int i = 0; i < 60; i++)
                s = MinimapOpacity.SmoothSpeed(s, 4f, 0.016f, 0.15f);
            Assert.AreEqual(4f, s, 0.01f);
        }

        [Test]
        public void SmoothingIsFrameRateIndependent()
        {
            float fast = 0f;
            for (int i = 0; i < 40; i++)
                fast = MinimapOpacity.SmoothSpeed(fast, 5f, 0.005f, 0.15f);

            float slow = 0f;
            for (int i = 0; i < 10; i++)
                slow = MinimapOpacity.SmoothSpeed(slow, 5f, 0.02f, 0.15f);

            Assert.AreEqual(fast, slow, 0.05f, "0.2s of smoothing must land in the same place at either step size");
        }
    }
}
