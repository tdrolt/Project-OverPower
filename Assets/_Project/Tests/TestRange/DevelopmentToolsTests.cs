using NUnit.Framework;
using Overpower.Data;
using Overpower.TestRange;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 29 Oct: the scroll-wheel zoom and the training dummies were for looking at the game while building it. They stay in the
    /// Editor and the development builds (the Dev test clients); a player's build (itch.io, the playtest build) has neither. A spectator
    /// seat keeps the wheel, since its key bar offers "Wheel: Zoom".
    /// </summary>
    public class DevelopmentToolsTests
    {
        [TestCase(true, true, true, TestName = "switch on, development run")]
        [TestCase(true, false, false, TestName = "switch on, a player's build")]
        [TestCase(false, true, false, TestName = "switch off, development run")]
        [TestCase(false, false, false, TestName = "switch off, a player's build")]
        public void TheDummiesNeedTheSwitchAndADevelopmentRun(bool switchOn, bool developmentRun, bool expected) =>
            Assert.AreEqual(expected, DevelopmentTools.TestRangeOn(switchOn, developmentRun));

        [TestCase(true, false, true, TestName = "development run, playing")]
        [TestCase(false, false, false, TestName = "a player's build, playing")]
        [TestCase(false, true, true, TestName = "a player's build, spectator seat")]
        [TestCase(true, true, true, TestName = "development run, spectator seat")]
        public void TheWheelZoomsOnlyInDevelopmentRunsAndForSpectators(bool developmentRun, bool spectatorSeat, bool expected) =>
            Assert.AreEqual(expected, DevelopmentTools.ScrollZoomOn(developmentRun, spectatorSeat));

        // Tudor, 30 Oct: the "F1: debug log" hint is not shown in a player's build; F1 still opens the log there.
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void TheDebugLogHintShowsOnlyInDevelopmentRuns(bool developmentRun, bool expected) =>
            Assert.AreEqual(expected, DevelopmentTools.ShowsDebugLogHint(developmentRun));

        // ---- the game asks the rules

        [Test]
        public void TheDebugLogAsksBeforeDrawingTheHint() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DebugOverlay), "OnGUI", typeof(DevelopmentTools).GetMethod(nameof(DevelopmentTools.ShowsDebugLogHint))));

        [Test]
        public void TheSpawnerAsksBeforePlacingDummies() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(TestRangeSpawner), "Start", typeof(DevelopmentTools).GetMethod(nameof(DevelopmentTools.TestRangeOn))));

        [Test]
        public void ThePanelAsksBeforeBuildingItself() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(TestRangePanel), "Awake", typeof(DevelopmentTools).GetMethod(nameof(DevelopmentTools.TestRangeOn))));

        [Test]
        public void TheCameraAsksBeforeReadingTheWheel() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(CameraTracking), "LateUpdate", typeof(DevelopmentTools).GetMethod(nameof(DevelopmentTools.ScrollZoomOn))));
    }
}
