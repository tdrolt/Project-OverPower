using System.Reflection;
using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.Net;
using Overpower.Telemetry;
using Overpower.Vision;

namespace Overpower.Tests
{
    /// <summary>
    /// The game asks the tested rules where the match log's new lines are written: the mode and map on the session line, the warm-up anchor of a
    /// mode's own map, the Dominion points race, the respawn shield, the Conquest map shrink and centre scan, and the writer's open file.
    /// </summary>
    public class MatchLogWiringTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static MethodInfo Method(System.Type type, string name) => type.GetMethod(name, Any);

        // ---------------------------------------------------------------- writing

        [Test] public void TheWriterAsksWhetherItKeepsTheFileOpen() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(TelemetryWriter), "Flush", Method(typeof(TelemetryWriteRules), nameof(TelemetryWriteRules.KeepsFileOpen))));

        [Test] public void TheZipsFlushLetsGoOfEveryFile()
        {
            MethodInfo release = Method(typeof(TelemetryWriter), nameof(TelemetryWriter.Release));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), nameof(MatchTelemetry.FlushNow), release), "this client's own file");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "FlushBotLogs", release), "and the bots'");
            Assert.IsFalse(IlWiring.Uses(typeof(MatchTelemetry), "Update", release), "the timer's flush keeps the files open");
        }

        [Test] public void ABotsBodyStartsItsSamplesWithItsOwnHeadStart() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerTelemetry), "Start", Method(typeof(TelemetryWriteRules), nameof(TelemetryWriteRules.FirstSampleHeadStart))));

        // ---------------------------------------------------------------- the session line

        [Test] public void TheSessionLineCarriesTheModeAndTheDominionSettings()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "WriteSessionLine", Method(typeof(MatchTelemetry), "ModeOfRoom")));
            MethodInfo withDominion = typeof(TuningSnapshot).GetMethod(nameof(TuningSnapshot.Json), new[]
            {
                typeof(Overpower.Data.TerritoryConfig), typeof(Overpower.Data.GameplayConfig), typeof(Overpower.Data.ArmorConfig), typeof(Overpower.Data.TelemetryConfig),
                typeof(Overpower.Data.WeaponCatalogue), typeof(Overpower.Data.AbilityCatalogue), typeof(Overpower.Data.DominionConfig),
            });
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "WriteSessionLine", withDominion));
        }

        // ---------------------------------------------------------------- the warm-up anchor

        [Test] public void AModesOwnMapLogsTheWarmUpAnchorByTheTestedRule()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(MatchTelemetry), "OnJoinedRoom", Method(typeof(WarmupAnchorLine), nameof(WarmupAnchorLine.LogsOnArrival))));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnJoinedRoom", Method(typeof(MatchTelemetry), "LogWarmupAnchor")));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "LogWarmupAnchor", Method(typeof(WarmupAnchorLine), nameof(WarmupAnchorLine.OnLogged))));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "TryOpenFile", Method(typeof(WarmupAnchorLine), nameof(WarmupAnchorLine.OnFileOpened))));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnLeftRoom", Method(typeof(WarmupAnchorLine), nameof(WarmupAnchorLine.Reset))));
        }

        [Test] public void TheClaimLogsItsAnchorThroughTheSameBookkeeping() =>
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "ClaimMatchIdentityWhenClockIsReady", Method(typeof(MatchTelemetry), "LogWarmupAnchor")));

        // ---------------------------------------------------------------- Dominion's points race

        [Test] public void TheMasterLogsThePointsWhenTheRoomsPointsChange()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "OnRoomPropertiesUpdate", Method(typeof(DominionDirector), "LogPointsIfChanged")));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionDirector), "LogPointsIfChanged", Method(typeof(TelemetryWriteRules), nameof(TelemetryWriteRules.PointsDiffer))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "LogPointsIfChanged", Method(typeof(MatchTelemetry), nameof(MatchTelemetry.LogTeamPoints))));
        }

        // ---------------------------------------------------------------- the respawn shield

        [Test] public void TheShieldLogsGoingUpAndEachWayItGoesDown()
        {
            System.Type shield = typeof(RespawnShield);
            MethodInfo down = Method(shield, "LogShieldDown");
            Assert.IsTrue(IlWiring.Uses(shield, "StartShield", Method(typeof(MatchTelemetry), nameof(MatchTelemetry.LogShieldUp))));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(shield, "LogShieldDown", Method(typeof(ShieldLogState), nameof(ShieldLogState.Ended))));
            Assert.IsTrue(IlWiring.Uses(shield, "HandleAliveChanged", down), "died");
            Assert.IsTrue(IlWiring.Uses(shield, "HandleEnemyAffected", down), "attacked");
            Assert.IsTrue(IlWiring.Uses(shield, "Awake", down), "left the spawn");
            Assert.IsTrue(IlWiring.Uses(shield, "ClearForFreshStart", down), "a round or break started");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(shield, "Update", Method(typeof(ShieldLogState), nameof(ShieldLogState.RanOut))), "ran out");
        }

        [Test] public void TheShieldCountsWhatItStops()
        {
            MethodInfo blocked = Method(typeof(ShieldLogState), nameof(ShieldLogState.Blocked));
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), nameof(RespawnShield.BlocksHit), blocked));
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), nameof(RespawnShield.StopsEnemyEffectFrom), blocked));
        }

        [Test] public void ABotsShieldLinesGoToTheBotsFile()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "LogForBody", Method(typeof(BodyRole), nameof(BodyRole.IsBot))));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "LogForBody", Method(typeof(MatchTelemetry), nameof(MatchTelemetry.LogBot))));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), nameof(MatchTelemetry.LogShieldUp), Method(typeof(MatchTelemetry), "LogForBody")));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), nameof(MatchTelemetry.LogShieldDown), Method(typeof(MatchTelemetry), "LogForBody")));
        }

        // ---------------------------------------------------------------- Conquest: the shrink and the centre scan

        [Test] public void TheShrinkIsLoggedAtTheFirstKnockoutAndAtATwoTeamStart()
        {
            MethodInfo logShrink = Method(typeof(MatchDirector), "LogShrink");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchDirector), "MasterRecompute", logShrink));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchDirector), "GoLive", logShrink));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchDirector), "LogShrink", Method(typeof(PhaseTwoCutRules), nameof(PhaseTwoCutRules.CutZones))), "the zones are the cut's own");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchDirector), "LogShrink", Method(typeof(MatchTelemetry), nameof(MatchTelemetry.LogShrink))));
        }

        [Test] public void EveryWaveOfTheCentreScanIsLoggedByTheTestedRule()
        {
            MethodInfo logWave = Method(typeof(CentreScan), "LogWaveIfFinished");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(CentreScan), "LogWaveIfFinished", Method(typeof(ScanWaveLog), nameof(ScanWaveLog.Step))));
            Assert.IsTrue(IlWiring.Uses(typeof(CentreScan), "LogWaveIfFinished", Method(typeof(MatchTelemetry), nameof(MatchTelemetry.LogScan))));
            Assert.IsTrue(IlWiring.Uses(typeof(CentreScan), "Update", logWave));
            Assert.IsTrue(IlWiring.Uses(typeof(CentreScan), "Stop", logWave), "a scan that stops mid-wave still logs it");
            Assert.IsTrue(IlWiring.Uses(typeof(CentreScan), "CatchEnemies", Method(typeof(ScanWaveLog), nameof(ScanWaveLog.Caught))));
        }
    }
}
