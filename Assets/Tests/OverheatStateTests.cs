using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class OverheatStateTests
    {
        // Real weapon values: max 100, decay starts 1.5s after the last Add, then falls at
        // 25/s, and the HUD starts warning at 80. Every test uses these so the numbers in the
        // assertions match the numbers a playtester would actually see.
        private static OverheatState NewState() => new OverheatState(
            max: 100f, decayDelay: 1.5f, decayPerSecond: 25f, warningThreshold: 80f);

        [Test]
        public void AddingBelowMaxDoesNotSilence()
        {
            var s = NewState();
            s.Add(99f);

            Assert.IsFalse(s.IsSilenced);
            Assert.IsTrue(s.CanAct);
        }

        [Test]
        public void AddingExactlyToMaxSilences()
        {
            var s = NewState();
            s.Add(100f);

            Assert.IsTrue(s.IsSilenced);
            Assert.IsFalse(s.CanAct);
        }

        [Test]
        public void DecayingJustBelowMaxIsStillSilenced()
        {
            // The rule is "silenced until the bar reaches 0", not "until it drops below max" -
            // reaching 99 by decaying down from a silenced 100 must not clear the silence.
            var s = NewState();
            s.Add(100f);
            s.Tick(1.5f);   // decayDelay elapsed, decay starts
            s.Tick(0.04f);  // 0.04 * 25 = 1 heat lost -> 99

            Assert.AreEqual(99f, s.Heat, 0.01f);
            Assert.IsTrue(s.IsSilenced);
        }

        [Test]
        public void OnceSilencedDecayingToHalfIsStillSilenced()
        {
            var s = NewState();
            s.Add(100f);
            s.Tick(1.5f);  // decayDelay elapsed
            s.Tick(2f);    // 2 * 25 = 50 heat lost -> 50

            Assert.AreEqual(50f, s.Heat, 0.01f);
            Assert.IsTrue(s.IsSilenced);
            Assert.IsFalse(s.CanAct);
        }

        [Test]
        public void ReachingZeroClearsTheSilence()
        {
            var s = NewState();
            s.Add(100f);
            s.Tick(1.5f);  // decayDelay elapsed
            s.Tick(4f);    // 4 * 25 = 100 heat lost -> exactly 0

            Assert.AreEqual(0f, s.Heat, 0.01f);
            Assert.IsFalse(s.IsSilenced);
            Assert.IsTrue(s.CanAct);
        }

        [Test]
        public void DecayDoesNotStartBeforeTheDelayHasElapsed()
        {
            var s = NewState();
            s.Add(50f);
            s.Tick(1.0f);   // less than decayDelay (1.5s)

            Assert.AreEqual(50f, s.Heat, 0.01f);
        }

        [Test]
        public void AddingHeatResetsTheDecayDelay()
        {
            var s = NewState();
            s.Add(50f);
            s.Tick(1.4f);   // still short of decayDelay - no decay yet
            s.Add(10f);     // this restarts the delay countdown
            s.Tick(1.4f);   // still short of decayDelay again - still no decay

            Assert.AreEqual(60f, s.Heat, 0.01f);
        }

        [Test]
        public void RefundCannotPushHeatBelowZero()
        {
            var s = NewState();
            s.Add(10f);
            s.Refund(50f);

            Assert.AreEqual(0f, s.Heat, 0.01f);
        }

        [Test]
        public void IsWarningIsTrueAtExactly80AndFalseAt79()
        {
            var below = NewState();
            below.Add(79f);
            Assert.IsFalse(below.IsWarning);

            var at = NewState();
            at.Add(80f);
            Assert.IsTrue(at.IsWarning);
        }

        [Test]
        public void IsWarningIsFalseWhileSilenced()
        {
            // The warning's job is to precede the silence, not to accompany it - showing a
            // warning during the punishment it was meant to warn about reads as a HUD bug.
            var s = NewState();
            s.Add(100f);

            Assert.IsTrue(s.IsSilenced);
            Assert.IsFalse(s.IsWarning);
        }

        [Test]
        public void ClearZeroesHeatAndUnSilences()
        {
            var s = NewState();
            s.Add(100f);
            s.Clear();

            Assert.AreEqual(0f, s.Heat, 0.01f);
            Assert.IsFalse(s.IsSilenced);
            Assert.IsTrue(s.CanAct);
        }

        [Test]
        public void NormalisedReflectsHeatAsAFractionOfMax()
        {
            var s = NewState();
            Assert.AreEqual(0f, s.Normalised, 0.001f);

            s.Add(50f);
            Assert.AreEqual(0.5f, s.Normalised, 0.001f);

            s.Add(50f);
            Assert.AreEqual(1f, s.Normalised, 0.001f);
        }
    }
}
