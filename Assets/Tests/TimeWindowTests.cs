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

        // ---------------------------------------------------------------- round-2 review fix (item A):
        // a span that only TOUCHES the window from outside (from != to) must be rejected, not kept as
        // a spurious zero-length row - the item 8 fix's own `>` (instead of `>=`) let a genuinely
        // non-overlapping-but-touching span slip through, because Max/Min clamping doesn't know the
        // span's own `to` is exclusive. Only a span that was ALREADY zero-length in the raw log (from
        // == to) may still be kept at a boundary, and only by whichever window's Contains says owns
        // that exact instant.

        [Test]
        public void ClipRejectsARealSpanThatOnlyTouchesTheWindowsStartFromOutside()
        {
            // Stint [10, 90) ending exactly where a Phase 2 window [90, 180] begins - the stint
            // itself never reaches into Phase 2 at all (it's exclusive at 90).
            var phase2 = new TimeWindow(90, 180, true);
            bool ok = phase2.Clip(10, 90, out _, out _);
            Assert.IsFalse(ok, "a real (non-zero-length) span ending exactly at this window's Start must not produce a touching zero-length row");
        }

        [Test]
        public void ClipRejectsARealSpanThatOnlyTouchesTheWindowsEndFromOutside()
        {
            // Stint [90, 150) starting exactly where a Phase 1 window [0, 90) ends - it belongs
            // entirely to Phase 2, not a phantom zero-length row in Phase 1.
            var phase1 = new TimeWindow(0, 90, false);
            bool ok = phase1.Clip(90, 150, out _, out _);
            Assert.IsFalse(ok, "a real (non-zero-length) span starting exactly at this window's End must not produce a touching zero-length row");
        }

        [Test]
        public void ClipKeepsAZeroLengthSpanExactlyAtATransitionInTheWindowThatOwnsIt()
        {
            // A genuinely zero-length ORIGINAL span (from == to == 90, e.g. a capture completing
            // and being lost on the same tick) sitting exactly at the transition - Phase 2 [90,
            // 180] owns instant 90 (its own Start, inclusive-as-last-window via Contains).
            var phase2 = new TimeWindow(90, 180, true);
            bool ok = phase2.Clip(90, 90, out double from, out double to);
            Assert.IsTrue(ok, "the zero-length instant at 90 belongs to Phase 2 (Contains(90) is true there)");
            Assert.AreEqual(90.0, from, 1e-9);
            Assert.AreEqual(90.0, to, 1e-9);
        }

        [Test]
        public void ClipRejectsAZeroLengthSpanExactlyAtATransitionInTheWindowThatDoesNotOwnIt()
        {
            // Same instant (90), but Phase 1 [0, 90) is exclusive at its own End - it must NOT
            // also claim this zero-length row (exactly one window owns any given instant).
            var phase1 = new TimeWindow(0, 90, false);
            bool ok = phase1.Clip(90, 90, out _, out _);
            Assert.IsFalse(ok, "Phase 1 ends exclusive at 90 - it must not also claim the zero-length instant there");
        }
    }
}
