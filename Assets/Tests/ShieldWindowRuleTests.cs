using NUnit.Framework;
using Overpower.Abilities;

namespace Overpower.Tests
{
    /// <summary>Tudor D20: the Invulnerability shield's armed window on screen. A ring while it waits, a short
    /// "Wasted" if nobody hit in time, nothing extra once a hit sets it off.</summary>
    public class ShieldWindowRuleTests
    {
        private const float Window = 2.5f;
        private const float WastedFor = 1.2f;
        private const float Grace = 0.3f;

        private static ShieldWindowView At(float t, bool triggered = false, float grace = 0f) =>
            ShieldWindowRule.View(t, Window, triggered, WastedFor, grace);

        [Test]
        public void RingShowsFromTheCastUntilTheWindowEnds()
        {
            Assert.IsTrue(At(0f).RingShown, "at the cast");
            Assert.IsTrue(At(1.2f).RingShown, "mid window");
            Assert.IsTrue(At(2.499f).RingShown, "just before the end");
            Assert.IsFalse(At(2.5f).RingShown, "at the end");
            Assert.IsFalse(At(3f).RingShown, "after");
            Assert.IsFalse(At(1f).WastedShown, "no Wasted while armed");
        }

        [Test]
        public void ATriggerInsideTheWindowHidesTheRingAndNeverShowsWasted()
        {
            foreach (float t in new[] { 0.4f, 2.0f, 2.6f, 3.4f, 9f })
            {
                Assert.IsFalse(At(t, true).RingShown, $"ring at {t}");
                Assert.IsFalse(At(t, true).WastedShown, $"wasted at {t}");
            }
        }

        [Test]
        public void NoTriggerByTheEndShowsWastedForItsSecondsThenNothing()
        {
            Assert.IsFalse(At(2.499f).WastedShown, "still armed");
            Assert.IsTrue(At(2.5f).WastedShown, "starts at the end");
            Assert.IsTrue(At(3.6f).WastedShown, "still inside its seconds");
            Assert.IsFalse(At(3.7f).WastedShown, "gone after its seconds");
            Assert.IsFalse(At(3.7f).RingShown);
        }

        [Test]
        public void ARemoteCopyWaitsAGraceBeforeWastedSoALateTriggerNeverFlashesIt()
        {
            Assert.IsFalse(At(2.6f, false, Grace).WastedShown, "inside the grace: not yet");
            Assert.IsFalse(At(2.6f, false, Grace).RingShown, "the ring is still off after the window");
            Assert.IsTrue(At(2.8f, false, Grace).WastedShown, "past window + grace");
            Assert.IsFalse(At(4.0f, false, Grace).WastedShown, "window + grace + its seconds");
        }

        [Test]
        public void ATriggerArrivingAfterWastedShowedStillTakesItAway()
        {
            Assert.IsTrue(At(3.0f, false, Grace).WastedShown, "showing");
            Assert.IsFalse(At(3.0f, true, Grace).WastedShown, "the late trigger arrives");
        }

        [Test]
        public void AZeroWindowOnlyEverShowsWasted()
        {
            var v = ShieldWindowRule.View(0f, 0f, false, WastedFor, 0f);
            Assert.IsFalse(v.RingShown);
            Assert.IsTrue(v.WastedShown);
        }

        [Test]
        public void WhoSeesIt()
        {
            Assert.IsTrue(ShieldWindowRule.CanSee(true, 0, 1, false), "the caster, switch off");
            Assert.IsTrue(ShieldWindowRule.CanSee(false, 1, 1, false), "a teammate, switch off");
            Assert.IsFalse(ShieldWindowRule.CanSee(false, 0, 1, false), "an enemy, switch off");
            Assert.IsFalse(ShieldWindowRule.CanSee(false, -1, 1, false), "team unknown, switch off");
            Assert.IsFalse(ShieldWindowRule.CanSee(false, 1, -1, false), "caster team unknown, switch off");
            Assert.IsTrue(ShieldWindowRule.CanSee(false, 0, 1, true), "an enemy, switch on");
            Assert.IsTrue(ShieldWindowRule.CanSee(false, -1, -1, true), "anyone, switch on");
        }
    }
}
