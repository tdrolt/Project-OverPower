using System.IO;
using System.Linq;
using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Round-2 review fix (item B): the item-4 fix's own regression - using a plain
    /// window.Clip for a life's own span [deathT - timeAlive, deathT) treats the window's literal
    /// Start/End (0 / matchLength) as hard walls, which is wrong at the very edges of the timeline:
    /// a life that started before the match clock was known (timeAlive > deathT) got truncated at 0
    /// instead of counting in full, and a death logged at t == -1 was dropped outright (it used to
    /// count via Contains(-1) before this whole feature existed). Isolated temp fixtures, one per
    /// scenario, since each is testing one specific edge of the alive-time formula.</summary>
    public class AliveTimeUnboundedEdgesTests
    {
        private static string NewTempFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "AliveTimeUnboundedEdgesTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        private static string Session() =>
            "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"n1\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n";

        private static string Death(double t, float timeAlive) =>
            "{\"e\":\"death\",\"t\":" + t.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"a\":-1,\"at\":-1,\"w\":-1,\"ab\":-1,\"assists\":[],\"x\":0,\"z\":0,\"timeAlive\":" +
            timeAlive.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"gold\":0,\"lw\":-1,\"leq\":-1,\"lmob\":-1,\"lult\":-1,\"abl\":0,\"rcl\":0}\n";

        [Test]
        public void ALifeThatStartedBeforeTheMatchClockCountsInFullNotClippedAtZero()
        {
            string temp = NewTempFolder();
            try
            {
                // Death at t=5 with timeAlive=45 - this life started at t=-40 (before the match
                // clock even reached 0), which is legitimate (the respawn/spawn that began this
                // life happened before mStart was known). The whole-match total must still be the
                // full 45, not cut short at t=0.
                string lines = Session() + "{\"e\":\"phase\",\"t\":90,\"num\":2,\"remain\":[0]}\n" + Death(5, 45f) + "{\"e\":\"sample\",\"t\":120,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));
                double whole = set.WholeMatch.Players.Single(p => p.Actor == 1).TimeAlive;
                double p1 = set.Phase1.Players.Single(p => p.Actor == 1).TimeAlive;
                double p2 = set.Phase2.Players.Single(p => p.Actor == 1).TimeAlive;

                Assert.AreEqual(45.0, whole, 1e-6, "the whole-match total must be the life's own full timeAlive, not clipped at t=0");
                Assert.AreEqual(45.0, p1, 1e-6, "the death is in Phase 1 (t=5 < 90) - Phase 1 gets the whole life");
                Assert.AreEqual(0.0, p2, 1e-6);
                Assert.AreEqual(whole, p1 + p2, 0.01);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void ADeathLoggedAtTMinusOneStillCountsItsFullTimeAlive()
        {
            string temp = NewTempFolder();
            try
            {
                // t == -1 is MatchClock's own "clock not known yet" sentinel - before this whole
                // life-span-clipping feature existed, a death here counted via Contains(-1) (true
                // for the whole-match window); it must still count in full, not be dropped.
                string lines = Session() + "{\"e\":\"phase\",\"t\":90,\"num\":2,\"remain\":[0]}\n" + Death(-1, 30f) + "{\"e\":\"sample\",\"t\":120,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));
                double whole = set.WholeMatch.Players.Single(p => p.Actor == 1).TimeAlive;
                double p1 = set.Phase1.Players.Single(p => p.Actor == 1).TimeAlive;
                double p2 = set.Phase2.Players.Single(p => p.Actor == 1).TimeAlive;

                Assert.AreEqual(30.0, whole, 1e-6, "a death at t=-1 must still count its full timeAlive, not be dropped");
                Assert.AreEqual(30.0, p1, 1e-6, "t=-1 maps to the very start of the match - Phase 1 owns it");
                Assert.AreEqual(0.0, p2, 1e-6);
                Assert.AreEqual(whole, p1 + p2, 0.01);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void ADeathExactlyAtTheTransitionCreditsTheWholeLifeToPhaseOne()
        {
            string temp = NewTempFolder();
            try
            {
                // Death at t=90 (exactly the transition) with timeAlive=25 - the life span
                // [65, 90) lies entirely BEFORE the transition (it's exclusive at 90), so all of
                // it belongs to Phase 1, even though the death EVENT itself (for kill-counting
                // purposes elsewhere) is attributed to Phase 2 by the usual half-open convention.
                string lines = Session() + "{\"e\":\"phase\",\"t\":90,\"num\":2,\"remain\":[0]}\n" + Death(90, 25f) + "{\"e\":\"sample\",\"t\":120,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));
                double whole = set.WholeMatch.Players.Single(p => p.Actor == 1).TimeAlive;
                double p1 = set.Phase1.Players.Single(p => p.Actor == 1).TimeAlive;
                double p2 = set.Phase2.Players.Single(p => p.Actor == 1).TimeAlive;

                Assert.AreEqual(25.0, whole, 1e-6);
                Assert.AreEqual(25.0, p1, 1e-6, "the life span [65, 90) is entirely before the transition");
                Assert.AreEqual(0.0, p2, 1e-6);
                Assert.AreEqual(whole, p1 + p2, 0.01);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
