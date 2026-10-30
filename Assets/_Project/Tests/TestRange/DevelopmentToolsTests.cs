using NUnit.Framework;
using Overpower.Data;
using Overpower.TestRange;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 29-30 Oct: the training dummies and zooming in closer than normal were for looking at the game while building it. They
    /// stay in the Editor and the development builds (the Dev test clients); a player's build (itch.io, the playtest build) has neither,
    /// but still zooms out. A spectator seat keeps the whole wheel range, since its key bar offers "Wheel: Zoom".
    /// </summary>
    public class DevelopmentToolsTests
    {
        [TestCase(true, true, true, TestName = "switch on, development run")]
        [TestCase(true, false, false, TestName = "switch on, a player's build")]
        [TestCase(false, true, false, TestName = "switch off, development run")]
        [TestCase(false, false, false, TestName = "switch off, a player's build")]
        public void TheDummiesNeedTheSwitchAndADevelopmentRun(bool switchOn, bool developmentRun, bool expected) =>
            Assert.AreEqual(expected, DevelopmentTools.TestRangeOn(switchOn, developmentRun));

        // Tudor, 30 Oct: a player's build still zooms OUT with the wheel (up to the camera's farthest); only zooming in closer than
        // the normal distance, which was for inspecting visuals, is kept to development runs and spectator seats.
        [TestCase(true, false, 0.5f, TestName = "development run, playing: as close as the camera allows")]
        [TestCase(false, false, 1f, TestName = "a player's build, playing: never closer than normal")]
        [TestCase(false, true, 0.5f, TestName = "a player's build, spectator seat: as close as the camera allows")]
        [TestCase(true, true, 0.5f, TestName = "development run, spectator seat")]
        public void TheWheelNeverZoomsInPastNormalInAPlayersBuild(bool developmentRun, bool spectatorSeat, float expected) =>
            Assert.AreEqual(expected, DevelopmentTools.ClosestScrollZoom(developmentRun, spectatorSeat, 0.5f));

        [Test]
        public void ACameraWhoseClosestIsAlreadyFartherKeepsIt() =>
            Assert.AreEqual(1.2f, DevelopmentTools.ClosestScrollZoom(false, false, 1.2f));

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
        public void TheCameraClampsTheWheelWithTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(CameraTracking), "LateUpdate", typeof(DevelopmentTools).GetMethod(nameof(DevelopmentTools.ClosestScrollZoom))));
    }
}
