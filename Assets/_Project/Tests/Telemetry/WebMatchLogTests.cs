using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>
    /// In the browser the match log is kept in the tab's memory only (the upload reads it back), never in the browser's lasting
    /// storage, where every match would pile up for good; and no zip is made, since the player cannot open a folder from a web page.
    /// Desktop builds keep their files and zips as before.
    /// </summary>
    public class WebMatchLogTests
    {
        [Test]
        public void TheBrowserKeepsTheLogInMemory() => Assert.IsTrue(TelemetryPathRules.KeepsLogsInMemory(webPlayer: true));

        [Test]
        public void ADesktopBuildWritesItsLogNextToTheGame() => Assert.IsFalse(TelemetryPathRules.KeepsLogsInMemory(webPlayer: false));

        [Test]
        public void TheBrowserMakesNoZip() => Assert.IsFalse(MatchLogZipRule.ZipsOnThisPlatform(webPlayer: true));

        [Test]
        public void ADesktopBuildStillZips() => Assert.IsTrue(MatchLogZipRule.ZipsOnThisPlatform(webPlayer: false));

        [Test]
        public void TheLogFolderAsksTheRule() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(TelemetryPaths), nameof(TelemetryPaths.ResolveMatchLogsRoot),
                typeof(TelemetryPathRules).GetMethod(nameof(TelemetryPathRules.KeepsLogsInMemory))));

        [Test]
        public void TheZipAsksTheRule() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(MatchLogZip), "TryZip",
                typeof(MatchLogZipRule).GetMethod(nameof(MatchLogZipRule.ZipsOnThisPlatform))));
    }
}
