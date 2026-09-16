using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    public class MatchClockTests
    {
        [Test]
        public void MatchSecondsCountFromTheStartStamp() =>
            Assert.AreEqual(12.5, MatchClock.Seconds(nowMs: 112500, startMs: 100000), 1e-9);

        [Test]
        public void WorksAcrossTheServerClockWrap() =>
            Assert.AreEqual(2.0, MatchClock.Seconds(int.MinValue + 1000, int.MaxValue - 999), 1e-9);

        [Test]
        public void UnknownClockOrStartIsMinusOne()
        {
            Assert.AreEqual(-1.0, MatchClock.Seconds(0, 100000));
            Assert.AreEqual(-1.0, MatchClock.Seconds(100000, 0));
        }
    }
}
