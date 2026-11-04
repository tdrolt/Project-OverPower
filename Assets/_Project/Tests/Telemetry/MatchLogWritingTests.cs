using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>
    /// The match log's new lines and how it writes: the file kept open outside a browser build, the host's bots sampling in different frames,
    /// a points line only when the points changed, the warm-up anchor a mode's own map logs again, the respawn shield's up-and-down story and
    /// one line per centre-scan wave.
    /// </summary>
    public class MatchLogWritingTests
    {
        // ---------------------------------------------------------------- the file

        [Test] public void TheFileStaysOpenOutsideABrowserBuild() => Assert.IsTrue(TelemetryWriteRules.KeepsFileOpen(webGlPlayer: false));

        [Test] public void ABrowserBuildAppendsAndClosesEveryFlush() => Assert.IsFalse(TelemetryWriteRules.KeepsFileOpen(webGlPlayer: true));

        // ---------------------------------------------------------------- the bots' samples

        [Test] public void ABotsFirstSampleComesEarlyByLessThanOneInterval()
        {
            foreach (int actor in new[] { 0, 1, 7, 1001, 1005, 1009, 25000, -3 })
            {
                float headStart = TelemetryWriteRules.FirstSampleHeadStart(actor, 4f);
                Assert.GreaterOrEqual(headStart, 0f, "actor " + actor);
                Assert.Less(headStart, 4f, "actor " + actor);
            }
        }

        [Test] public void NineBotsOfOneHostNeverSampleInTheSameFrame()
        {
            List<float> headStarts = Enumerable.Range(1001, 9).Select(actor => TelemetryWriteRules.FirstSampleHeadStart(actor, 4f)).OrderBy(s => s).ToList();
            for (int i = 1; i < headStarts.Count; i++)
                Assert.Greater(headStarts[i] - headStarts[i - 1], 1f / 30f, "two bots would sample within one frame at 30 fps");
        }

        [Test] public void TheSameBotAlwaysGetsTheSameHeadStart() =>
            Assert.AreEqual(TelemetryWriteRules.FirstSampleHeadStart(1003, 6f), TelemetryWriteRules.FirstSampleHeadStart(1003, 6f));

        [Test] public void NoIntervalMeansNoHeadStart()
        {
            Assert.AreEqual(0f, TelemetryWriteRules.FirstSampleHeadStart(1002, 0f));
            Assert.AreEqual(0f, TelemetryWriteRules.FirstSampleHeadStart(1002, -2f));
        }

        // ---------------------------------------------------------------- the points race

        [Test] public void PointsAreLoggedOnlyWhenTheyChanged()
        {
            Assert.IsTrue(TelemetryWriteRules.PointsDiffer(null, new[] { 0, 0 }), "nothing logged yet");
            Assert.IsTrue(TelemetryWriteRules.PointsDiffer(new[] { 10, 20 }, new[] { 15, 20 }));
            Assert.IsTrue(TelemetryWriteRules.PointsDiffer(new[] { 10, 20 }, new[] { 10, 20, 0 }), "a different number of teams");
            Assert.IsFalse(TelemetryWriteRules.PointsDiffer(new[] { 10, 20 }, new[] { 10, 20 }), "a stage write carrying the same points again");
            Assert.IsFalse(TelemetryWriteRules.PointsDiffer(new[] { 10, 20 }, null), "nothing read, nothing to log");
        }

        // ---------------------------------------------------------------- the warm-up anchor

        [Test] public void TheMasterLogsTheAnchorWhenItsMapArrivesInAStartedLobby()
        {
            var memory = new TelemetryWrittenMemory();
            var anchor = new WarmupAnchorLine();
            Assert.IsTrue(anchor.LogsOnArrival(memory, "room a", isMaster: true, lobbyStage: 1));
            Assert.IsTrue(anchor.LogsOnArrival(memory, "room a", isMaster: true, lobbyStage: 2));
        }

        [Test] public void NoAnchorFromAnotherClientOrFromTheLobbyItself()
        {
            var memory = new TelemetryWrittenMemory();
            var anchor = new WarmupAnchorLine();
            Assert.IsFalse(anchor.LogsOnArrival(memory, "room a", isMaster: false, lobbyStage: 1), "phase lines are the master's");
            Assert.IsFalse(anchor.LogsOnArrival(memory, "room a", isMaster: true, lobbyStage: 0), "in the lobby the claim logs it");
        }

        [Test] public void AnAnchorWrittenIntoTheFileIsNotLoggedAgain()
        {
            var memory = new TelemetryWrittenMemory();
            var anchor = new WarmupAnchorLine();
            anchor.OnLogged(memory, "room a", fileIsOpen: true);
            Assert.IsFalse(new WarmupAnchorLine().LogsOnArrival(memory, "room a", true, 1), "the next scene's copy reads the same process memory");
        }

        [Test] public void AQueuedAnchorCountsOnlyOnceTheFileHasOpened()
        {
            var memory = new TelemetryWrittenMemory();
            var anchor = new WarmupAnchorLine();
            anchor.OnLogged(memory, "room a", fileIsOpen: false);
            Assert.IsTrue(anchor.LogsOnArrival(memory, "room a", true, 1), "a queued line dies with a scene that never opened the file");
            anchor.OnFileOpened(memory, "room a");
            Assert.IsFalse(anchor.LogsOnArrival(memory, "room a", true, 1));
        }

        [Test] public void AFileThatOpensWithoutAQueuedAnchorMarksNothing()
        {
            var memory = new TelemetryWrittenMemory();
            new WarmupAnchorLine().OnFileOpened(memory, "room a");
            Assert.IsFalse(memory.WasWritten(WarmupAnchorLine.Kind, "room a", 0));
        }

        [Test] public void LeavingTheRoomDropsAQueuedAnchor()
        {
            var memory = new TelemetryWrittenMemory();
            var anchor = new WarmupAnchorLine();
            anchor.OnLogged(memory, "room a", fileIsOpen: false);
            anchor.Reset();
            anchor.OnFileOpened(memory, "room a");
            Assert.IsFalse(memory.WasWritten(WarmupAnchorLine.Kind, "room a", 0));
        }

        // ---------------------------------------------------------------- the respawn shield

        [Test] public void AShieldGoesDownOnceWithWhatItStopped()
        {
            var shield = new ShieldLogState();
            shield.Started();
            shield.Blocked();
            shield.Blocked();
            shield.Blocked();
            Assert.IsTrue(shield.Ended(out int blocked));
            Assert.AreEqual(3, blocked);
            Assert.IsFalse(shield.Ended(out _), "the clear that follows the log line writes no second one");
        }

        [Test] public void AShieldTheLogNeverSawGoUpNeverGoesDown()
        {
            var shield = new ShieldLogState();
            shield.Blocked();
            Assert.IsFalse(shield.Ended(out _));
        }

        [Test] public void ANewShieldCountsItsOwnBlocks()
        {
            var shield = new ShieldLogState();
            shield.Started();
            shield.Blocked();
            shield.Ended(out _);
            shield.Blocked();
            shield.Started();
            shield.Blocked();
            shield.Ended(out int blocked);
            Assert.AreEqual(1, blocked);
        }

        [Test] public void AShieldRunsOutWhenItReadsDownWithoutAClear()
        {
            var shield = new ShieldLogState();
            Assert.IsFalse(shield.RanOut(upNow: false, clearSent: false), "never up");
            shield.Started();
            Assert.IsFalse(shield.RanOut(upNow: true, clearSent: false), "still up");
            Assert.IsFalse(shield.RanOut(upNow: false, clearSent: true), "a clear ended it, with its own reason");
            Assert.IsTrue(shield.RanOut(upNow: false, clearSent: false));
        }

        [Test] public void EveryShieldEndHasItsOwnReason()
        {
            var reasons = new[] { ShieldLogState.RanOutReason, ShieldLogState.AttackedReason, ShieldLogState.LeftSpawnReason, ShieldLogState.DiedReason, ShieldLogState.FreshStartReason };
            Assert.AreEqual(reasons.Length, reasons.Distinct().Count());
        }

        // ---------------------------------------------------------------- the centre scan

        [Test] public void AWaveIsLoggedOnceWhenItsFrontLeavesTheArena()
        {
            var log = new ScanWaveLog();
            Assert.IsFalse(log.Step(true, 5000, 1, travelling: true, seenByMyTeam: true, out _));
            log.Caught();
            log.Caught();
            Assert.IsFalse(log.Step(true, 5000, 1, travelling: true, seenByMyTeam: true, out _));
            Assert.IsTrue(log.Step(true, 5000, 1, travelling: false, seenByMyTeam: true, out ScanWaveLog.Wave wave));
            Assert.AreEqual(5000, wave.StartMs);
            Assert.AreEqual(1, wave.Holder);
            Assert.AreEqual(2, wave.Caught);
            Assert.IsFalse(log.Step(true, 5000, 1, travelling: false, seenByMyTeam: true, out _), "the finished wave stays on the clock until the next");
        }

        [Test] public void AnotherTeamsWaveHasNoCatchCount()
        {
            var log = new ScanWaveLog();
            log.Step(true, 7000, 2, travelling: true, seenByMyTeam: false, out _);
            log.Caught();
            Assert.IsTrue(log.Step(true, 7000, 2, travelling: false, seenByMyTeam: false, out ScanWaveLog.Wave wave));
            Assert.AreEqual(-1, wave.Caught);
        }

        [Test] public void TheNextWaveCutsTheLastOneOff()
        {
            var log = new ScanWaveLog();
            log.Step(true, 1000, 0, travelling: true, seenByMyTeam: false, out _);
            Assert.IsTrue(log.Step(true, 2000, -1, travelling: true, seenByMyTeam: false, out ScanWaveLog.Wave cut));
            Assert.AreEqual(1000, cut.StartMs);
            Assert.AreEqual(0, cut.Holder);
            Assert.IsTrue(log.Step(true, 2000, -1, travelling: false, seenByMyTeam: false, out ScanWaveLog.Wave next));
            Assert.AreEqual(2000, next.StartMs);
            Assert.AreEqual(-1, next.Holder);
        }

        [Test] public void AScanThatStopsMidWaveLogsTheWave()
        {
            var log = new ScanWaveLog();
            log.Step(true, 3000, 1, travelling: true, seenByMyTeam: true, out _);
            Assert.IsTrue(log.Step(false, 0, -1, travelling: false, seenByMyTeam: false, out ScanWaveLog.Wave wave), "the map shrank or the match ended");
            Assert.AreEqual(3000, wave.StartMs);
            Assert.IsFalse(log.Step(false, 0, -1, travelling: false, seenByMyTeam: false, out _));
        }

        [Test] public void NoWaveIsLoggedWhileNoneIsOnTheClock()
        {
            var log = new ScanWaveLog();
            log.Caught();
            Assert.IsFalse(log.Step(false, 4000, 1, travelling: true, seenByMyTeam: true, out _));
            Assert.IsFalse(log.Step(false, 4000, 1, travelling: false, seenByMyTeam: true, out _));
        }

        [Test] public void TheHolderIsTheOneTheWaveEndsWith()
        {
            var log = new ScanWaveLog();
            log.Step(true, 9000, -1, travelling: true, seenByMyTeam: false, out _);
            Assert.IsTrue(log.Step(true, 9000, 2, travelling: false, seenByMyTeam: false, out ScanWaveLog.Wave wave), "a capture stamped before the start is read a frame late");
            Assert.AreEqual(2, wave.Holder);
        }
    }
}
