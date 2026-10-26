using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Mark plan step 2, Tudor's override (top-of-plan table, answer 4): "one for each enemy
    /// that you are hitting" - one live number per enemy that ADDS each hit, re-pops and restarts its
    /// life, rather than the plan's original merge-window/side-alternation design. That replaces
    /// `damageNumberMergeSeconds`/`damageNumberSpread` and `ShouldMerge`/`Side` with a single
    /// `damageNumberHoldSeconds`: how long the number stays solid (no rise, no fade) after the last hit
    /// before it starts rising and fading, over `damageNumberLifetimeSeconds`. Pop/rounding/marked-scale
    /// are otherwise unchanged from the plan's original design.</summary>
    public class DamageNumberMotionTests
    {
        private const float Hold = 0.6f;
        private const float Lifetime = 0.8f;
        private const float PopSeconds = 0.12f;
        private const float PopScale = 1.5f;
        private const float Rise = 60f;
        private const float FadeStart = 0.55f;
        private const float MarkedScale = 1.4f;

        private static DamageNumberPose Eval(float ageSinceLastHit, float popAge, bool marked = false) =>
            DamageNumberMotion.Evaluate(ageSinceLastHit, popAge, Hold, Lifetime, PopSeconds, PopScale, Rise, FadeStart, MarkedScale, marked);

        [Test]
        public void ThePopStartsBigAndSettlesToNormalSizeByPopSeconds()
        {
            Assert.AreEqual(PopScale, Eval(0f, 0f).Scale, 0.001f);
            Assert.AreEqual(1f, Eval(0f, PopSeconds).Scale, 0.001f);
            Assert.AreEqual(1f, Eval(0f, PopSeconds + 1f).Scale, 0.001f);
        }

        [Test]
        public void WhileWithinHoldSecondsTheNumberStaysSolidAtZeroRise()
        {
            foreach (float age in new[] { 0f, 0.1f, 0.3f, Hold })
            {
                DamageNumberPose pose = Eval(age, 999f); // popAge irrelevant to Rise/Alpha
                Assert.AreEqual(0f, pose.Rise, 0.001f, "age=" + age);
                Assert.AreEqual(1f, pose.Alpha, 0.001f, "age=" + age);
            }
        }

        [Test]
        public void AfterHoldItRisesWithoutEverGoingBackDownAndReachesTheFullRiseAtTheEnd()
        {
            float previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float age = Hold + Lifetime * (i / 20f);
                float rise = Eval(age, 999f).Rise;
                Assert.GreaterOrEqual(rise + 0.0001f, previous, "age=" + age);
                previous = rise;
            }

            Assert.AreEqual(0f, Eval(Hold, 999f).Rise, 0.001f);
            Assert.AreEqual(Rise, Eval(Hold + Lifetime, 999f).Rise, 0.001f);
        }

        [Test]
        public void AfterHoldItStaysOpaqueUntilTheFadeStartsAndIsGoneAtTheEnd()
        {
            Assert.AreEqual(1f, Eval(Hold + FadeStart * Lifetime, 999f).Alpha, 0.001f);

            float midFadeAlpha = Eval(Hold + Lifetime * ((FadeStart + 1f) / 2f), 999f).Alpha;
            Assert.Greater(midFadeAlpha, 0f);
            Assert.Less(midFadeAlpha, 1f);

            Assert.AreEqual(0f, Eval(Hold + Lifetime, 999f).Alpha, 0.001f);
        }

        [Test]
        public void AMarkedNumberIsDrawnBiggerThroughoutItsLife()
        {
            foreach (float popAge in new[] { 0f, 0.05f, PopSeconds, PopSeconds + 0.5f, 5f })
            {
                float unmarked = Eval(0f, popAge, marked: false).Scale;
                float marked = Eval(0f, popAge, marked: true).Scale;
                Assert.AreEqual(unmarked * MarkedScale, marked, 0.001f, "popAge=" + popAge);
            }
        }

        [Test]
        public void TheNumberShownIsRoundedButALandedHitNeverShowsZero()
        {
            Assert.AreEqual(1, DamageNumberMotion.Shown(0.3f));
            Assert.AreEqual(21, DamageNumberMotion.Shown(21.4f));
            Assert.AreEqual(32, DamageNumberMotion.Shown(31.5f));
            Assert.AreEqual(33, DamageNumberMotion.Shown(32.5f));
            Assert.AreEqual(0, DamageNumberMotion.Shown(0f));
        }
    }
}
