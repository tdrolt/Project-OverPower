using System;
using System.IO;
using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T7: PhaseTimeline.From, against small hand-written temp-directory logs (same
    /// pattern as TelemetryAggregatorReviewFixesTests) rather than the shared match_phases fixture -
    /// this class is pure and small enough to test directly without the aggregator at all.</summary>
    public class PhaseTimelineTests
    {
        private static string NewTempFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "PhaseTimelineTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        private static string Session(int actor) =>
            "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":" + actor + ",\"nick\":\"n" + actor +
            "\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n";

        [Test]
        public void NoPhaseEventsMeansPhase1OnlyCoveringTheWholeMatch()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"sample\",\"t\":40,\"bal\":0}\n"; // matchLength = 40, no phase event at all
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsFalse(timeline.HasPhase2);
                Assert.IsNull(timeline.Phase2);
                Assert.IsNull(timeline.TransitionSeconds);
                Assert.AreEqual(40.0, timeline.MatchLength, 1e-9);

                Assert.AreEqual(0.0, timeline.Phase1.Start, 1e-9);
                Assert.AreEqual(40.0, timeline.Phase1.End, 1e-9);
                Assert.IsTrue(timeline.Phase1.EndInclusive); // Phase 1 IS the whole match here.

                Assert.AreEqual(0.0, timeline.WholeMatch.Start, 1e-9);
                Assert.AreEqual(40.0, timeline.WholeMatch.End, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void APhase2EventAt90SplitsIntoTwoWindows()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":1,\"remain\":[]}\n" + // MatchTelemetry's own anchor - not a transition
                    "{\"e\":\"phase\",\"t\":90,\"num\":2,\"remain\":[0,1]}\n" +
                    "{\"e\":\"sample\",\"t\":150,\"bal\":0}\n"; // matchLength = 150
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsTrue(timeline.HasPhase2);
                Assert.AreEqual(90.0, timeline.TransitionSeconds.Value, 1e-9);
                Assert.AreEqual(150.0, timeline.MatchLength, 1e-9);

                Assert.AreEqual(0.0, timeline.Phase1.Start, 1e-9);
                Assert.AreEqual(90.0, timeline.Phase1.End, 1e-9);
                Assert.IsFalse(timeline.Phase1.EndInclusive); // [0, 90) - the instant AT 90 is Phase 2's.

                Assert.AreEqual(90.0, timeline.Phase2.Start, 1e-9);
                Assert.AreEqual(150.0, timeline.Phase2.End, 1e-9);
                Assert.IsTrue(timeline.Phase2.EndInclusive); // [90, end] - Phase 2 is always the last window.
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void APhaseNumberOfOneIsNeverATransitionEvenAtARealTimestamp()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    // A late/duplicate phase-1 anchor at a real timestamp (not just -1) must still never
                    // count as the transition - only phase >= 2 does.
                    "{\"e\":\"phase\",\"t\":5,\"num\":1,\"remain\":[]}\n" +
                    "{\"e\":\"sample\",\"t\":60,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsFalse(timeline.HasPhase2);
                Assert.IsNull(timeline.TransitionSeconds);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
