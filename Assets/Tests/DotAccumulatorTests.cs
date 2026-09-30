using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T3 review: DotAccumulator is what replaces one `hit` line per burn tick with one
    /// `dot` line per flush interval (or on a lethal tick). These tests cover the three situations
    /// PlayerTelemetry drives it through: merging several ticks, a lethal-tick flush, and an
    /// interval flush (Reset back to empty, ready for the next bucket).</summary>
    public class DotAccumulatorTests
    {
        [Test]
        public void StartsEmpty()
        {
            var acc = new DotAccumulator();

            Assert.IsFalse(acc.HasData);
            Assert.AreEqual(0, acc.Ticks);
        }

        [Test]
        public void MergeAccumulatesSumsAndTicks()
        {
            var acc = new DotAccumulator();

            acc.Merge(10.0, raw: 5f, armorAbsorbed: 2f, healthLost: 3f);
            acc.Merge(10.5, raw: 4f, armorAbsorbed: 0f, healthLost: 4f);
            acc.Merge(11.0, raw: 6f, armorAbsorbed: 1f, healthLost: 5f);

            Assert.IsTrue(acc.HasData);
            Assert.AreEqual(3, acc.Ticks);
            Assert.AreEqual(15f, acc.RawSum, 1e-6);
            Assert.AreEqual(3f, acc.ArmorSum, 1e-6);
            Assert.AreEqual(12f, acc.HealthSum, 1e-6);
        }

        [Test]
        public void FirstTIsTheFirstMergeAfterAResetAndLastTAlwaysUpdates()
        {
            var acc = new DotAccumulator();

            acc.Merge(10.0, 1f, 0f, 1f);
            acc.Merge(10.2, 1f, 0f, 1f);
            acc.Merge(10.4, 1f, 0f, 1f);

            Assert.AreEqual(10.0, acc.FirstT, 1e-9);
            Assert.AreEqual(10.4, acc.LastT, 1e-9);
        }

        [Test]
        public void ResetClearsEverythingSoTheSameInstanceStartsAFreshBucket()
        {
            var acc = new DotAccumulator();
            acc.Merge(10.0, 5f, 2f, 3f);
            acc.Merge(10.2, 5f, 2f, 3f);

            acc.Reset();

            Assert.IsFalse(acc.HasData);
            Assert.AreEqual(0, acc.Ticks);
            Assert.AreEqual(0f, acc.RawSum);
            Assert.AreEqual(0f, acc.ArmorSum);
            Assert.AreEqual(0f, acc.HealthSum);
            Assert.AreEqual(0.0, acc.FirstT);
            Assert.AreEqual(0.0, acc.LastT);

            // The next bucket's own first tick sets FirstT again, independent of the reset one.
            acc.Merge(20.0, 9f, 0f, 9f);
            Assert.AreEqual(20.0, acc.FirstT, 1e-9);
            Assert.AreEqual(1, acc.Ticks);
        }

        [Test]
        public void ALethalTickIsMergedLikeAnyOther_CallerDecidesToFlushImmediately()
        {
            // DotAccumulator itself has no notion of "lethal" - PlayerTelemetry merges the tick in
            // exactly the same way, then decides to flush right away because the hit was lethal.
            // This test only documents that Merge does not special-case anything.
            var acc = new DotAccumulator();
            acc.Merge(5.0, 100f, 0f, 100f);

            Assert.AreEqual(1, acc.Ticks);
            Assert.AreEqual(100f, acc.HealthSum, 1e-6);
        }
    }
}
