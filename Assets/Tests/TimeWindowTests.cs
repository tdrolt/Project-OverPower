using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T7 review fix (item 8): TimeWindow.Clip's own edge cases, direct - no aggregator
    /// or log needed. Before this fix, Clip rejected `from == to` (a zero-length span), which made a
    /// zero-length ownership stint/capture attempt that the pre-T7, unwindowed tables always kept
    /// silently disappear from a T7-windowed build instead.</summary>
    public class TimeWindowTests
    {
        [Test]
        public void ClipKeepsAZeroLengthSpanThatLiesInsideTheWindow()
        {
            var window = new TimeWindow(0, 180, true);
            bool ok = window.Clip(60, 60, out double from, out double to);
            Assert.IsTrue(ok, "a zero-length span at t=60, inside [0,180], must survive");
            Assert.AreEqual(60.0, from, 1e-9);
            Assert.AreEqual(60.0, to, 1e-9);
        }

        [Test]
        public void ClipRejectsAZeroLengthSpanOutsideTheWindow()
        {
            var window = new TimeWindow(90, 180, true);
            bool ok = window.Clip(60, 60, out _, out _);
            Assert.IsFalse(ok, "a zero-length span at t=60 has no overlap with a window starting at 90");
        }

        [Test]
        public void ClipRejectsWhenFromIsStrictlyAfterTo()
        {
            var window = new TimeWindow(0, 180, true);
            bool ok = window.Clip(100, 50, out _, out _);
            Assert.IsFalse(ok, "an inverted span (from > to) is never valid, zero-length or not");
        }

        [Test]
        public void ClipStillClampsAndShortensANormalOverlappingSpan()
        {
            var window = new TimeWindow(0, 90, false);
            bool ok = window.Clip(60, 120, out double from, out double to);
            Assert.IsTrue(ok);
            Assert.AreEqual(60.0, from, 1e-9);
            Assert.AreEqual(90.0, to, 1e-9); // clamped to the window's own End.
        }

        [Test]
        public void ClipClampsANegativeStartToTheWindowsOwnStart()
        {
            var window = new TimeWindow(0, 180, true);
            bool ok = window.Clip(-1, 10, out double from, out double to);
            Assert.IsTrue(ok);
            Assert.AreEqual(0.0, from, 1e-9);
            Assert.AreEqual(10.0, to, 1e-9);
        }
    }
}
