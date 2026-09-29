using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>A freshly placed mine blinks while it cannot go off yet, then shows steadily. The numbers are
    /// passed in, so these guard the rule, not the current tuning.</summary>
    public class MineArmingBlinkRuleTests
    {
        private const float ArmDelay = 1.0f;
        private const float Period = 0.1f;

        [Test]
        public void DuringArmingTheModelIsShownAtSomeTimesAndHiddenAtOthers()
        {
            bool sawShown = false, sawHidden = false;
            for (float t = 0f; t < ArmDelay; t += 0.01f)
            {
                if (MineArmingBlinkRule.IsShown(t, ArmDelay, Period)) sawShown = true;
                else sawHidden = true;
            }

            Assert.IsTrue(sawShown);
            Assert.IsTrue(sawHidden);
        }

        [Test]
        public void AlternatesEveryBlinkPeriod()
        {
            bool first = MineArmingBlinkRule.IsShown(0.05f, ArmDelay, Period);
            bool second = MineArmingBlinkRule.IsShown(0.15f, ArmDelay, Period);
            bool third = MineArmingBlinkRule.IsShown(0.25f, ArmDelay, Period);

            Assert.AreNotEqual(first, second);
            Assert.AreEqual(first, third);
        }

        [Test]
        public void FromTheMomentItIsArmedItIsAlwaysShown()
        {
            for (float t = ArmDelay; t < ArmDelay + 2f; t += 0.01f)
                Assert.IsTrue(MineArmingBlinkRule.IsShown(t, ArmDelay, Period), $"hidden at {t}");
        }

        [Test]
        public void ZeroArmDelayNeverBlinks()
        {
            for (float t = 0f; t < 1f; t += 0.01f)
                Assert.IsTrue(MineArmingBlinkRule.IsShown(t, 0f, Period), $"hidden at {t}");
        }

        [Test]
        public void ZeroBlinkPeriodDoesNotBlinkOrDivideByZero()
        {
            Assert.IsTrue(MineArmingBlinkRule.IsShown(0.3f, ArmDelay, 0f));
        }
    }
}
