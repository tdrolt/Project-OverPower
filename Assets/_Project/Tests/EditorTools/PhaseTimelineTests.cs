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

        // ---------------------------------------------------------------- review fix (item 9): elimination fallback

        [Test]
        public void AnEliminationWithNoPhaseEventBecomesTheTransitionWithTheFallbackFlagSet()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":1,\"remain\":[]}\n" + // the harmless anchor - never a transition
                    "{\"e\":\"elimination\",\"t\":75,\"tm\":2,\"remain\":[0,1]}\n" + // 2.7's MatchDirector never got to log the matching `phase` 2
                    "{\"e\":\"sample\",\"t\":150,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsTrue(timeline.HasPhase2);
                Assert.IsTrue(timeline.UsedEliminationFallback);
                Assert.AreEqual(75.0, timeline.TransitionSeconds.Value, 1e-9);
                Assert.AreEqual(75.0, timeline.Phase1.End, 1e-9);
                Assert.AreEqual(75.0, timeline.Phase2.Start, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void ARealPhaseEventWinsOverAnEliminationNoFallbackFlag()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"elimination\",\"t\":75,\"tm\":2,\"remain\":[0,1]}\n" +
                    "{\"e\":\"phase\",\"t\":80,\"num\":2,\"remain\":[0,1]}\n" + // 2.7 DID log the real phase event, slightly later
                    "{\"e\":\"sample\",\"t\":150,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsFalse(timeline.UsedEliminationFallback, "a real phase event exists - the elimination is not a fallback source");
                Assert.AreEqual(80.0, timeline.TransitionSeconds.Value, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void TheEarliestOfSeveralEliminationsIsUsedAsTheFallback()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"elimination\",\"t\":95,\"tm\":1,\"remain\":[0,2]}\n" +
                    "{\"e\":\"elimination\",\"t\":75,\"tm\":2,\"remain\":[0,1]}\n" + // earlier, out of file order
                    "{\"e\":\"sample\",\"t\":150,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsTrue(timeline.UsedEliminationFallback);
                Assert.AreEqual(75.0, timeline.TransitionSeconds.Value, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void ANegativeEliminationTimestampMapsToZero()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"elimination\",\"t\":-1,\"tm\":2,\"remain\":[0,1]}\n" +
                    "{\"e\":\"sample\",\"t\":150,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsTrue(timeline.UsedEliminationFallback);
                Assert.AreEqual(0.0, timeline.TransitionSeconds.Value, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void ATransitionAtExactlyZeroDoesNotDoubleCountATMinusOneEventIntoBothPhases()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"join\",\"t\":-1,\"a\":1,\"tm\":0}\n" +
                    "{\"e\":\"elimination\",\"t\":0,\"tm\":2,\"remain\":[0,1]}\n" +
                    "{\"e\":\"sample\",\"t\":150,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.AreEqual(0.0, timeline.TransitionSeconds.Value, 1e-9);
                // Phase 1 is now the empty window [0, 0) - it correctly claims nothing, including
                // the t == -1 join. Phase 2 (the only window with positive length) claims it instead.
                Assert.IsFalse(timeline.Phase1.Contains(-1), "the empty Phase 1 window must not claim the t=-1 join");
                Assert.IsTrue(timeline.Phase2.Contains(-1), "Phase 2 is the only window with real length - it owns the t=-1 join instead");
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- review fix (item 12): two masters racing

        [Test]
        public void TwoPhase2EventsFromTwoMastersUseTheEarliestWithNoDoubleSplit()
        {
            string temp = NewTempFolder();
            try
            {
                // A master-migration race could see two different clients each log their own
                // `phase` 2 line, milliseconds apart - the earliest must win, and there must be
                // exactly ONE transition (not two windows chained together).
                string lines =
                    Session(1) +
                    "{\"e\":\"phase\",\"t\":90.05,\"num\":2,\"remain\":[0,1]}\n" +
                    "{\"e\":\"sample\",\"t\":150,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);
                string lines2 =
                    Session(2) +
                    "{\"e\":\"phase\",\"t\":90.0,\"num\":2,\"remain\":[0,1]}\n" +
                    "{\"e\":\"sample\",\"t\":150,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "2.jsonl"), lines2);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsTrue(timeline.HasPhase2);
                Assert.AreEqual(90.0, timeline.TransitionSeconds.Value, 1e-9);
                Assert.IsFalse(timeline.UsedEliminationFallback);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- 2.7b step 9: the warm-up (new-style logs)

        [Test]
        public void AWarmupThenAThreeTeamStartOpensPhase1AtLive()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":0,\"remain\":[]}\n" + // the warm-up anchor
                    "{\"e\":\"phase\",\"t\":60,\"num\":1,\"remain\":[0,1,2]}\n" + // live: three teams
                    "{\"e\":\"phase\",\"t\":200,\"num\":2,\"remain\":[0,1]}\n" + // the first knockout
                    "{\"e\":\"sample\",\"t\":300,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsTrue(timeline.WentLive);
                Assert.AreEqual(60.0, timeline.LiveSeconds, 1e-9);
                Assert.IsTrue(timeline.HasWarmup);

                Assert.AreEqual(0.0, timeline.Warmup.Start, 1e-9);
                Assert.AreEqual(60.0, timeline.Warmup.End, 1e-9);
                Assert.IsFalse(timeline.Warmup.EndInclusive);

                Assert.AreEqual(60.0, timeline.WholeMatch.Start, 1e-9);
                Assert.AreEqual(300.0, timeline.WholeMatch.End, 1e-9);
                Assert.IsTrue(timeline.WholeMatch.EndInclusive);

                Assert.AreEqual(60.0, timeline.Phase1.Start, 1e-9);
                Assert.AreEqual(200.0, timeline.Phase1.End, 1e-9);
                Assert.IsFalse(timeline.Phase1.EndInclusive);

                Assert.AreEqual(200.0, timeline.Phase2.Start, 1e-9);
                Assert.AreEqual(300.0, timeline.Phase2.End, 1e-9);
                Assert.IsTrue(timeline.Phase2.EndInclusive);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void AHostStartedTwoTeamMatchIsAllPhase2()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":0,\"remain\":[]}\n" +
                    "{\"e\":\"phase\",\"t\":45,\"num\":2,\"remain\":[0,1]}\n" + // live: a host start begins TwoTeams
                    "{\"e\":\"sample\",\"t\":200,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.AreEqual(45.0, timeline.LiveSeconds, 1e-9);
                Assert.AreEqual(45.0, timeline.TransitionSeconds.Value, 1e-9);
                Assert.IsFalse(timeline.Phase1.Contains(100));

                Assert.AreEqual(45.0, timeline.Phase2.Start, 1e-9);
                Assert.AreEqual(200.0, timeline.Phase2.End, 1e-9);
                Assert.IsTrue(timeline.Phase2.EndInclusive);

                Assert.AreEqual(45.0, timeline.WholeMatch.Start, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void ASessionThatNeverWentLiveIsAllWarmup()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":0,\"remain\":[]}\n" +
                    "{\"e\":\"sample\",\"t\":80,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsFalse(timeline.WentLive);
                Assert.AreEqual(0.0, timeline.Warmup.Start, 1e-9);
                Assert.AreEqual(80.0, timeline.Warmup.End, 1e-9);
                Assert.IsTrue(timeline.Warmup.EndInclusive);
                Assert.IsFalse(timeline.HasPhase2);
                Assert.IsFalse(timeline.Phase1.Contains(10));
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void AWarmupEliminationIsNeverTheTransition()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":0,\"remain\":[]}\n" +
                    "{\"e\":\"elimination\",\"t\":20,\"tm\":2,\"remain\":[0,1]}\n" + // before live - never a transition
                    "{\"e\":\"phase\",\"t\":60,\"num\":1,\"remain\":[0,1]}\n" +
                    "{\"e\":\"sample\",\"t\":100,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.IsNull(timeline.TransitionSeconds);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void ALateDuplicateWarmupAnchorDoesNotMoveLive()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1) +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":0,\"remain\":[]}\n" +
                    "{\"e\":\"phase\",\"t\":30,\"num\":1,\"remain\":[0,1]}\n" +
                    "{\"e\":\"phase\",\"t\":50,\"num\":0,\"remain\":[]}\n" + // a stray late/duplicate anchor
                    "{\"e\":\"sample\",\"t\":90,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var timeline = PhaseTimeline.From(TelemetryLog.Load(temp));

                Assert.AreEqual(30.0, timeline.LiveSeconds, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
