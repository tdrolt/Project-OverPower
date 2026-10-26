using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>A freshly placed mine blinks while it cannot go off yet, then shows steadily. The numbers are
    /// passed in, so these guard the rule, not the current tuning.</summary>
    public class MineArmingBlinkRuleTests
    {
        private const float ArmDelay = 1.0f;

        private static bool Shown(float t, float period = Period) =>
            MineArmingBlinkRule.IsShown(t, new MineDetonationState(ArmDelay).IsArmed(t), period);
        private const float Period = 0.1f;

        [Test]
        public void DuringArmingTheModelIsShownAtSomeTimesAndHiddenAtOthers()
        {
            bool sawShown = false, sawHidden = false;
            for (float t = 0f; t < ArmDelay; t += 0.01f)
            {
                if (Shown(t)) sawShown = true;
                else sawHidden = true;
            }

            Assert.IsTrue(sawShown);
            Assert.IsTrue(sawHidden);
        }

        [Test]
        public void AlternatesEveryBlinkPeriod()
        {
            bool first = Shown(0.05f);
            bool second = Shown(0.15f);
            bool third = Shown(0.25f);

            Assert.AreNotEqual(first, second);
            Assert.AreEqual(first, third);
        }

        [Test]
        public void FromTheMomentItIsArmedItIsAlwaysShown()
        {
            for (float t = ArmDelay; t < ArmDelay + 2f; t += 0.01f)
                Assert.IsTrue(Shown(t), $"hidden at {t}");
        }

        [Test]
        public void ZeroArmDelayNeverBlinks()
        {
            for (float t = 0f; t < 1f; t += 0.01f)
                Assert.IsTrue(MineArmingBlinkRule.IsShown(t, new MineDetonationState(0f).IsArmed(t), Period), $"hidden at {t}");
        }

        [Test]
        public void ZeroBlinkPeriodNeverBlinksWhileArming()
        {
            foreach (float t in new[] { 0.1f, 0.3f, 0.7f, 0.95f })
                Assert.IsTrue(Shown(t, 0f), $"hidden at {t}");
        }

        [Test]
        public void ANegativeAgeReadsAsShown()
        {
            Assert.IsTrue(Shown(-0.5f));
        }
    }
}
