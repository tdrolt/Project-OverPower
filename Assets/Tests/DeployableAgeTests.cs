using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers DeployableAge.SecondsSince (FAIL #15 fix, Task 2.0): a late joiner (or anyone
    /// else) must compute a deployable's age from the placement timestamp that travelled in
    /// instantiationData, not from Photon's own info.SentServerTime - measured, on a genuinely fresh
    /// late joiner, to read as though the object was placed "now" even ~23s after the real placement
    /// (see two-client-harness.md ss7 for the reflection session that caught this). PhotonNetwork.
    /// ServerTimestamp is a 32-bit ms counter that wraps roughly every 49.7 days, so the subtraction
    /// must be done in unchecked 32-bit arithmetic - the wrap-around case below is what proves that.</summary>
    public class DeployableAgeTests
    {
        [Test]
        public void ReadsTheOrdinaryElapsedSecondsBetweenTwoTimestamps()
        {
            Assert.AreEqual(4.0, DeployableAge.SecondsSince(placedServerTimestampMs: 1000, nowServerTimestampMs: 5000), 1e-9);
        }

        [Test]
        public void SurvivesTheServerTimestampWrappingPastIntMaxValue()
        {
            // PhotonNetwork.ServerTimestamp is ms since the game server started, stored as a 32-bit
            // signed int reinterpreting an unsigned counter - it wraps from a huge positive number to
            // a huge negative one. Placed 500ms before the wrap, read 500ms after: 1001ms really
            // elapsed (int.MinValue - int.MaxValue is itself a 1ms step in unsigned terms), even
            // though the raw ints look like they went backwards by ~4.29 billion.
            int placedMs = int.MaxValue - 500;
            int nowMs = int.MinValue + 500;

            Assert.AreEqual(1.001, DeployableAge.SecondsSince(placedMs, nowMs), 1e-9);
        }

        [Test]
        public void ClampsAFutureTimestampToZeroInsteadOfGoingNegative()
        {
            // "now" earlier than "placed" - clock skew or an out-of-order delivery, not a wrap (the
            // gap here is small, nowhere near the ~24.8-day half-range a real wrap would need) - must
            // never read as a negative age.
            Assert.AreEqual(0.0, DeployableAge.SecondsSince(placedServerTimestampMs: 5000, nowServerTimestampMs: 3000));
        }

        [Test]
        public void ZeroElapsedReadsAsZero()
        {
            Assert.AreEqual(0.0, DeployableAge.SecondsSince(placedServerTimestampMs: 42, nowServerTimestampMs: 42));
        }
    }
}
