using NUnit.Framework;
using Overpower.Abilities;

namespace Overpower.Tests
{
    /// <summary>
    /// Scope step 2 (Tudor, 2026-09-18). ScopeAbility eases its own applied factor toward 1 (unscoped) or
    /// 1 + extraZoomOutPercent/100 (fully scoped) - CameraTracking's multiplier stack itself has no notion of
    /// time (CameraZoomStack, scope step 1). Pure and static so ScopeAbility.OwnerTick can stay a thin caller.
    /// </summary>
    public class ScopeEaseTests
    {
        private const float ExtraZoomOutPercent = 20f;
        private const float EaseSeconds = 0.2f;

        [Test]
        public void DoesNotReachTheTargetBeforeTheEaseTimeElapses()
        {
            float value = ScopeEase.Advance(1f, 1.2f, ExtraZoomOutPercent, EaseSeconds, EaseSeconds * 0.9f);

            Assert.Less(value, 1.2f);
        }

        [Test]
        public void ReachesTheTargetAtExactlyTheEaseTime()
        {
            float value = ScopeEase.Advance(1f, 1.2f, ExtraZoomOutPercent, EaseSeconds, EaseSeconds);

            Assert.AreEqual(1.2f, value, 1e-5f);
        }

        [Test]
        public void NeverOvershootsPastTheTargetOnALagSpike()
        {
            float value = ScopeEase.Advance(1f, 1.2f, ExtraZoomOutPercent, EaseSeconds, 5f);

            Assert.AreEqual(1.2f, value, 1e-5f);
        }

        [Test]
        public void EasesBackTowardOneWhenReleasedPartwayThrough()
        {
            float value = ScopeEase.Advance(1.2f, 1f, ExtraZoomOutPercent, EaseSeconds, EaseSeconds * 0.5f);

            Assert.Greater(value, 1f);
            Assert.Less(value, 1.2f);
        }

        [Test]
        public void AnEaseTimeOfZeroSnapsStraightToTheTarget()
        {
            // 0 means "instant" everywhere else numeric tuning in this project does (AbilityModule.cooldownSeconds
            // among them) - a designer who sets this to 0 gets a snap, not a divide-by-zero.
            float value = ScopeEase.Advance(1f, 1.2f, ExtraZoomOutPercent, 0f, 0.001f);

            Assert.AreEqual(1.2f, value, 1e-5f);
        }
    }
}
