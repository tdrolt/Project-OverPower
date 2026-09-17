using System.IO;
using System.Linq;
using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Round-2 review fix (item A): the item-8 fix's own regression - a REAL (non-zero-
    /// length) stint/capture ending or starting exactly at the phase transition must appear in
    /// exactly ONE phase, never a spurious zero-length duplicate in the other. Realistic trigger:
    /// the elimination that starts Phase 2 happens on the exact same tick as an ownership change or
    /// a capture completing. Isolated temp fixture (not the shared match_phases one), since this is
    /// specifically about boundary-touching spans, not the general phase split.</summary>
    public class TimeWindowBoundaryTouchTests
    {
        private static string NewTempFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TimeWindowBoundaryTouchTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        [Test]
        public void SpansTouchingTheTransitionEachAppearInExactlyOnePhaseAndSumsMatchWholeMatch()
        {
            string temp = NewTempFolder();
            try
            {
                string lines =
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"n1\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                    // Zone 0: team 0 owns it from t=10, LOSES it to team 1 at EXACTLY t=90 (the
                    // elimination tick) - a real stint [10, 90) that only touches Phase 2's own start.
                    "{\"e\":\"ownership\",\"t\":10,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1000}\n" +
                    // Team 1's capture attempt on zone 0, started at t=85, closed by the same t=90
                    // ownership change - a real capture [85, 90) that also only touches Phase 2's start.
                    "{\"e\":\"capture\",\"t\":85,\"zone\":0,\"tm\":1,\"state\":\"started\",\"progress\":0.5,\"players\":1}\n" +
                    "{\"e\":\"phase\",\"t\":90,\"num\":2,\"remain\":[0,1]}\n" +
                    "{\"e\":\"ownership\",\"t\":90,\"zone\":0,\"tier\":2,\"old\":0,\"new\":1,\"since\":2000}\n" +
                    // Zone 1: team 1 starts owning it at EXACTLY t=90 - a real stint [90, end] that
                    // only touches Phase 1's own end.
                    "{\"e\":\"ownership\",\"t\":90,\"zone\":1,\"tier\":2,\"old\":-1,\"new\":1,\"since\":3000}\n" +
                    // Zone 2: team 2 captures it and loses it again on the SAME tick, t=90 - a
                    // genuinely zero-length ORIGINAL stint sitting exactly at the transition.
                    "{\"e\":\"ownership\",\"t\":90,\"zone\":2,\"tier\":2,\"old\":-1,\"new\":2,\"since\":4000}\n" +
                    "{\"e\":\"ownership\",\"t\":90,\"zone\":2,\"tier\":2,\"old\":2,\"new\":-1,\"since\":5000}\n" +
                    "{\"e\":\"sample\",\"t\":120,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));

                // ---- zone 0 / team 0: real stint [10, 90) - Phase 1 only, never Phase 2 ----
                var wholeZone0 = set.WholeMatch.Ownership.Where(o => o.Zone == 0 && o.Team == 0).ToList();
                Assert.AreEqual(1, wholeZone0.Count, "zone 0's team-0 stint must appear exactly once on the whole-match build");
                Assert.AreEqual(80.0, wholeZone0[0].Duration, 1e-9);
                Assert.AreEqual(1, wholeZone0[0].Phase);
                Assert.AreEqual("captured", wholeZone0[0].HowEnded);

                Assert.AreEqual(1, set.Phase1.Ownership.Count(o => o.Zone == 0 && o.Team == 0));
                Assert.AreEqual(0, set.Phase2.Ownership.Count(o => o.Zone == 0 && o.Team == 0),
                    "the stint ending exactly at the transition must NOT also produce a spurious zero-length row in Phase 2");

                // ---- zone 0 capture attempt [85, 90) - Phase 1 only, never Phase 2 ----
                var wholeCaptures = set.WholeMatch.Captures.Where(c => c.Zone == 0).ToList();
                Assert.AreEqual(1, wholeCaptures.Count);
                Assert.AreEqual("completed", wholeCaptures[0].Outcome);
                Assert.AreEqual(5.0, wholeCaptures[0].Duration, 1e-9);
                Assert.AreEqual(1, wholeCaptures[0].Phase);

                Assert.AreEqual(1, set.Phase1.Captures.Count(c => c.Zone == 0));
                Assert.AreEqual(0, set.Phase2.Captures.Count(c => c.Zone == 0),
                    "a capture ending exactly at the transition must not ALSO count as 'completed' in Phase 2");

                // ---- zone 1 / team 1: real stint [90, end] - Phase 2 only, never Phase 1 ----
                Assert.AreEqual(0, set.Phase1.Ownership.Count(o => o.Zone == 1),
                    "the stint starting exactly at the transition must not produce a spurious zero-length row in Phase 1");
                var wholeZone1 = set.WholeMatch.Ownership.Where(o => o.Zone == 1).ToList();
                Assert.AreEqual(1, wholeZone1.Count);
                Assert.AreEqual(2, wholeZone1[0].Phase);
                Assert.AreEqual(1, set.Phase2.Ownership.Count(o => o.Zone == 1));

                // ---- zone 2 / team 2: a genuinely zero-length stint AT the transition - Phase 2 only ----
                var wholeZone2 = set.WholeMatch.Ownership.Where(o => o.Zone == 2).ToList();
                Assert.AreEqual(1, wholeZone2.Count, "the zero-length stint at the transition must still appear exactly once");
                Assert.AreEqual(0.0, wholeZone2[0].Duration, 1e-9);
                Assert.AreEqual(2, wholeZone2[0].Phase);
                Assert.AreEqual(0, set.Phase1.Ownership.Count(o => o.Zone == 2));
                Assert.AreEqual(1, set.Phase2.Ownership.Count(o => o.Zone == 2));

                // ---- P1 + P2 ownership seconds still sum to the whole match ----
                double whole = set.WholeMatch.Ownership.Sum(o => o.Duration);
                double p1 = set.Phase1.Ownership.Sum(o => o.Duration);
                double p2 = set.Phase2.Ownership.Sum(o => o.Duration);
                Assert.AreEqual(whole, p1 + p2, 0.01, "ownership seconds must still sum P1 + P2 == whole match");
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
