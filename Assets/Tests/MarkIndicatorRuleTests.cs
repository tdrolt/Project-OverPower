using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Mark plan step 5: the diamond's own fade/pulse, pure and tested without touching a
    /// Graphic - see MarkIndicatorRule's own class comment. Tudor's answer 1 adds TrackedTotal, the one
    /// decision the marked player's own diamond needs beyond "seconds left > 0 -> show with this alpha"
    /// (the shooter's diamond already knows its own "total" from the report that started it; the
    /// victim's diamond has no such report to read one from - see that method's own comment).</summary>
    public class MarkIndicatorRuleTests
    {
        [Test]
        public void FullAtTheStartFadingTowardTheFloorAsTheMarkRunsOut()
        {
            const float total = 2f;
            const float minAlpha = 0.35f;
            const float pulseSpeed = 0f; // "0 = steady" - no pulsing, so only the ramp is under test here.

            Assert.AreEqual(1f, MarkIndicatorRule.Alpha(total, total, minAlpha, pulseSpeed, 5f), 1e-4f,
                "freshly marked: full alpha");
            Assert.AreEqual(minAlpha, MarkIndicatorRule.Alpha(0.001f, total, minAlpha, pulseSpeed, 5f), 1e-3f,
                "about to run out: down at the floor");

            float halfway = MarkIndicatorRule.Alpha(total * 0.5f, total, minAlpha, pulseSpeed, 5f);
            Assert.Greater(halfway, minAlpha, "halfway through, still above the floor");
            Assert.Less(halfway, 1f, "halfway through, already below full");
        }

        [Test]
        public void HiddenOnceNoTimeIsLeft()
        {
            Assert.AreEqual(0f, MarkIndicatorRule.Alpha(0f, 2f, 0.35f, 2.5f, 10f));
            Assert.AreEqual(0f, MarkIndicatorRule.Alpha(-0.5f, 2f, 0.35f, 2.5f, 10f), "an overshoot past zero is still hidden");
        }

        [Test]
        public void ThePulseNeverDropsBelowTheFloorOrAboveOne()
        {
            const float total = 2f;
            const float minAlpha = 0.35f;
            const float pulseSpeed = 2.5f;

            for (int i = 0; i < 50; i++)
            {
                float time = i * 0.137f; // An irrational-ish step so 50 samples don't land on a tidy cycle.
                // Sweeps secondsLeft from just-marked (total) down toward just-about-to-expire (>0),
                // never reaching or going below 0 - Alpha's hard cut to 0 there is HiddenOnceNoTimeIsLeft's
                // own job, not this test's.
                float secondsLeft = total * (1f - i / 50f);
                float alpha = MarkIndicatorRule.Alpha(secondsLeft, total, minAlpha, pulseSpeed, time);
                Assert.GreaterOrEqual(alpha, minAlpha, $"sample {i}: below the floor");
                Assert.LessOrEqual(alpha, 1f, $"sample {i}: above one");
            }
        }

        [Test]
        public void TrackedTotalBumpsUpWhenTheFreshValueIsHigher()
        {
            Assert.AreEqual(2f, MarkIndicatorRule.TrackedTotal(0f, 2f), "a fresh mark, nothing tracked yet");
            Assert.AreEqual(2f, MarkIndicatorRule.TrackedTotal(1.2f, 2f), "a second attacker's longer-lived mark becomes the longest");
        }

        [Test]
        public void TrackedTotalHoldsSteadyAsTheObservedValueCountsDown()
        {
            Assert.AreEqual(2f, MarkIndicatorRule.TrackedTotal(2f, 1.2f));
            Assert.AreEqual(2f, MarkIndicatorRule.TrackedTotal(2f, 0f), "even at zero - the view itself decides whether to hide, not this");
        }
    }
}
