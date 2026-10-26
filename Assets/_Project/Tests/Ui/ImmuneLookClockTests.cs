using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Pure timing for the yellow "shield immunity" look (Mark plan step 1, Decision 15): the
    /// look follows the Invulnerability shield's own phase message, which every client receives
    /// (What exists D), so this clock only ever needs "now" and "how long", never IsMine - the same
    /// class is driven identically on the owner and on every remote copy.</summary>
    public class ImmuneLookClockTests
    {
        [Test]
        public void TheLookStaysOnForExactlyItsSeconds()
        {
            var clock = new ImmuneLookClock();
            clock.Show(10f, 4f);

            Assert.IsTrue(clock.IsOn(10f));
            Assert.IsTrue(clock.IsOn(13.99f));
            Assert.IsFalse(clock.IsOn(14f));
        }

        [Test]
        public void ClearEndsItAtOnce()
        {
            var clock = new ImmuneLookClock();
            clock.Show(10f, 4f);
            clock.Clear();

            Assert.IsFalse(clock.IsOn(10.1f));
        }

        [Test]
        public void ANewShowRestartsFromTheLatestTrigger()
        {
            var clock = new ImmuneLookClock();
            clock.Show(10f, 4f);
            clock.Show(12f, 4f);

            Assert.IsTrue(clock.IsOn(15.9f));
            Assert.IsFalse(clock.IsOn(16f));
        }

        [Test]
        public void ZeroOrNegativeSecondsShowsNothing()
        {
            var clock = new ImmuneLookClock();
            clock.Show(10f, 0f);
            Assert.IsFalse(clock.IsOn(10f));

            clock.Show(10f, -1f);
            Assert.IsFalse(clock.IsOn(10f));
        }
    }
}
