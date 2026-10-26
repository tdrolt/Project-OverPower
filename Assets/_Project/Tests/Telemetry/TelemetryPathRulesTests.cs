using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Pure-rule test for the 2026-09-27 match-log location fallback order (D1's own logic,
    /// TelemetryPathRules.Choose). No filesystem involved - TelemetryPaths is the impure half that
    /// probes each candidate and feeds the pass/fail result in here.</summary>
    public class TelemetryPathRulesTests
    {
        [Test]
        public void GameFolderWritable_WinsOverEverything()
        {
            Assert.AreEqual(MatchLogLocation.GameFolder, TelemetryPathRules.Choose(true, true));
            Assert.AreEqual(MatchLogLocation.GameFolder, TelemetryPathRules.Choose(true, false));
        }

        [Test]
        public void GameFolderNotWritable_DocumentsGiven_UsesDocuments()
        {
            Assert.AreEqual(MatchLogLocation.Documents, TelemetryPathRules.Choose(false, true));
        }

        [Test]
        public void NeitherWritable_FallsBackToLegacy()
        {
            Assert.AreEqual(MatchLogLocation.Legacy, TelemetryPathRules.Choose(false, false));
        }
    }
}
