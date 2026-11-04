using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Overpower.Dominion;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>
    /// A match log read into its mode's report: the mode and map from the logs or the folder, the players with their bot tags, the tabs and what
    /// each shows, and Dominion's rounds, points race, zones, respawn shields and builds, or Conquest's closed corner and centre scan. The logs
    /// are written here by hand, with made-up numbers.
    /// </summary>
    public class ModeReportBuilderTests
    {
        private string root;

        [SetUp]
        public void MakeRoot()
        {
            root = Path.Combine(Path.GetTempPath(), "ModeReportBuilderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void RemoveRoot()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        // Lines are written with single quotes for readability; they become JSON's double quotes.
        private string Folder(string name, params (string File, string[] Lines)[] files)
        {
            string folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            foreach (var (file, lines) in files)
                File.WriteAllLines(Path.Combine(folder, file), lines.Select(l => l.Replace('\'', '"')));
            return folder;
        }

        private static string Marker(double t, string note) =>
            "{'e':'marker','t':" + t.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",'a':1,'note':'" + note + "'}";

        private static string Sample(double t, int zone, bool alive, int team, int weapon) =>
            "{'e':'sample','t':" + t.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",'bal':0,'x':4,'z':6,'alive':" + (alive ? "true" : "false") +
            ",'zone':" + zone + ",'tm':" + team + ",'w':" + weapon + ",'eq':12,'mob':13,'ult':14,'abl':1,'rcl':0}";

        private const string DominionTuning = "'tuning':{'telemetry':{'sampleIntervalSeconds':5},'dominion':{'roundsToWin':2,'maxRounds':3,'roundSeconds':100,'breakSeconds':10," +
                                              "'overtimeSeconds':20,'overtimeLeadPoints':50,'pointsPerZonePerSecond':[0,2,3,0],'centrePayoutPoints':70,'centrePayoutIntervalSeconds':25," +
                                              "'centreFirstPayoutSeconds':25,'bountyHoldSeconds':40,'bountyPoints':30,'respawnSeconds2v2':4,'respawnSeconds3v3v3':5,'shieldSeconds':3," +
                                              "'suddenDeathShrinkSeconds':40,'suddenDeathStartRadius2v2':20,'suddenDeathStartRadius3v3v3':40,'finalRadius2v2':3,'finalRadius3v3v3':6," +
                                              "'damagePerSecondOutside':7,'weaponDepthByRound':[0,1,2],'armorUpgradesByRound':[0,1,2]}}";

        private string DominionMatch()
        {
            int[] teams = { 0, 1 };
            return Folder("2026-01-02_0304_Dominion-2v2_Test-lobby",
                ("1_Kim.jsonl", new[]
                {
                    "{'e':'session','t':1,'schema':1,'m':'M1','a':1,'nick':'Kim','tm':0,'master':true,'mode':'Dominion 2v2','map':'Dominion 2v2','teamSize':2,'teams':[0,1]," + DominionTuning + "}",
                    "{'e':'phase','t':2,'num':0,'remain':[]}",
                    "{'e':'phase','t':10,'num':2,'remain':[0,1]}",
                    Marker(10, DominionMarkerNotes.BreakStart(1)),
                    Marker(20, DominionMarkerNotes.RoundStart(1)),
                    "{'e':'points','t':30,'round':1,'stage':2,'pts':[5,10]}",
                    "{'e':'ownership','t':35,'zone':1,'tier':3,'old':-1,'new':1,'since':3501}",
                    "{'e':'ownership','t':40,'zone':0,'tier':3,'old':-1,'new':0,'since':4001}",
                    Sample(40, 0, true, 0, 1),
                    Sample(45, 0, true, 0, 1),
                    Sample(50, -1, true, 0, 1),
                    "{'e':'ownership','t':50,'zone':0,'tier':3,'old':0,'new':1,'since':5001}",
                    Marker(50, DominionMarkerNotes.Bounty(0, 1, 30)),
                    "{'e':'points','t':60,'round':1,'stage':2,'pts':[20,60]}",
                    "{'e':'death','t':60,'a':1001,'at':1,'w':5,'ab':-1,'assists':[],'x':1,'z':1,'timeAlive':40,'gold':0}",
                    "{'e':'respawn','t':66,'x':1,'z':1,'deadSec':6,'uaSpawn':false}",
                    "{'e':'shield','t':66,'state':'up','dur':3}",
                    "{'e':'shield','t':68,'state':'down','reason':'left spawn','blocked':2}",
                    Sample(70, 2, true, 0, 1),
                    Marker(80, DominionMarkerNotes.RoundEnd(1, 1, teams, new[] { 40, 90 })),
                    Marker(80, DominionMarkerNotes.BreakStart(2)),
                    Marker(90, DominionMarkerNotes.RoundStart(2)),
                    Marker(150, DominionMarkerNotes.RoundEnd(2, 1, teams, new[] { 10, 70 })),
                    Marker(150, DominionMarkerNotes.MatchOver(1, teams, new[] { 0, 2 })),
                }),
                ("1001_Rex.jsonl", new[]
                {
                    "{'e':'session','t':1,'schema':1,'m':'M1','a':1001,'nick':'Rex','tm':1,'master':false,'bot':true,'bbuild':2,'mode':'Dominion 2v2','map':'Dominion 2v2','teamSize':2,'teams':[0,1]}",
                    Sample(25, 1, true, 1, 5),
                    Sample(30, 1, true, 1, 5),
                    Sample(35, 1, true, 1, 7),
                    Sample(95, 1, true, 1, 7),
                }));
        }

        private ModeReport BuildDominion(out TelemetryLog log)
        {
            log = TelemetryLog.Load(DominionMatch());
            return ModeReportBuilder.Build(log, ModeReportBuilder.ModeOf(log), TelemetryAggregator.BuildSet(log));
        }

        // ---------------------------------------------------------------- Dominion

        [Test] public void TheModeIsReadFromTheSessionLine()
        {
            TelemetryLog log = TelemetryLog.Load(DominionMatch());
            Assert.AreEqual(ReportMode.Dominion2v2, ModeReportBuilder.ModeOf(log));
            Assert.AreEqual("Dominion 2v2", log.Sessions[0].Mode);
            Assert.AreEqual(2, log.Sessions[0].TeamSize);
        }

        [Test] public void TheTitleNamesTheModeTheWinnerAndTheScore()
        {
            ModeReport report = BuildDominion(out _);
            Assert.AreEqual("Dominion 2v2, " + ArenaNames.TeamName(1) + " wins 2-0", report.Title);
            Assert.AreEqual("the lane", report.MapName);
            Assert.AreEqual(10.0, report.LiveSeconds, 1e-6, "the warm-up anchor makes the phase line at 10 s the moment it went live");
        }

        [Test] public void ABotIsTaggedWithItsBuildAndCounted()
        {
            ModeReport report = BuildDominion(out _);
            RosterEntry rex = report.Roster.Single(r => r.Actor == 1001);
            Assert.IsTrue(rex.Bot);
            Assert.AreEqual("Laser", rex.Build);
            Assert.IsFalse(report.Roster.Single(r => r.Actor == 1).Bot);
            Assert.AreEqual("2 players: 1 human, 1 bot", report.PlayerCount);
        }

        [Test] public void EveryRoundPlayedHasATab()
        {
            ModeReport report = BuildDominion(out _);
            CollectionAssert.AreEqual(new[] { "whole-match", "round-1", "round-2" }, report.Tabs.Select(t => t.Key).ToArray());
            ReportTab round1 = report.Tabs[1];
            Assert.AreEqual(20.0, round1.Start);
            Assert.AreEqual(80.0, round1.End);
            Assert.IsTrue(round1.TimesFromStart);
            Assert.IsTrue(report.Scopes.ContainsKey("round-1"));
            CollectionAssert.DoesNotContain(report.Tabs[0].Sections, "economy", "Dominion picks its build for free: no gold");
            CollectionAssert.Contains(report.Tabs[0].Sections, "result");
        }

        [Test] public void TheResultHasEveryRoundWithItsWinnerPointsAndMargin()
        {
            DominionData d = BuildDominion(out _).DominionReport;
            Assert.AreEqual(1, d.Winner);
            Assert.AreEqual("two round wins", d.HowEnded);
            Assert.AreEqual(2, d.Rounds.Count);
            Assert.AreEqual(50, d.Rounds[0].Margin);
            Assert.AreEqual(60.0, d.Rounds[0].LengthSeconds, 1e-6);
            CollectionAssert.AreEqual(new[] { 1 }, d.Rounds[0].Winners);
            Assert.IsFalse(d.Rounds[0].Overtime);
        }

        [Test] public void ThePointsRaceFollowsTheLoggedPointsAndEndsAtTheRoundsTotal()
        {
            PointsRace race = BuildDominion(out _).DominionReport.Races.Single(r => r.Round == 1);
            Assert.IsTrue(race.Logged);
            PointsSeries purple = race.Series.Single(s => s.Team == 1);
            CollectionAssert.AreEqual(new[] { 0.0, 0.0 }, purple.Points[0]);
            CollectionAssert.AreEqual(new[] { 10.0, 10.0 }, purple.Points[1], "30 s is 10 s into the round");
            CollectionAssert.AreEqual(new[] { 60.0, 90.0 }, purple.Points.Last(), "the round's end and its total");
            RaceEvent bounty = race.Events.Single();
            Assert.AreEqual("bounty", bounty.Kind);
            Assert.AreEqual(30.0, bounty.At, 1e-6);
        }

        [Test] public void ThePointsAreSplitIntoZonesBountiesAndTheCentre()
        {
            PointsSource purple = BuildDominion(out _).DominionReport.Races.Single(r => r.Round == 1).Sources.Single(s => s.Team == 1);
            Assert.AreEqual(90, purple.Total);
            Assert.AreEqual(30, purple.Bounties);
            Assert.AreEqual(0, purple.Centre);
            Assert.AreEqual(60, purple.Zones);
        }

        [Test] public void ARoundWithoutLoggedPointsRunsFromZeroToItsTotal()
        {
            PointsRace race = BuildDominion(out _).DominionReport.Races.Single(r => r.Round == 2);
            Assert.IsFalse(race.Logged);
            CollectionAssert.AreEqual(new[] { 60.0, 70.0 }, race.Series.Single(s => s.Team == 1).Points.Last());
        }

        [Test] public void TheZonesShowWhoHeldThemAndWhenTheyWereFirstTaken()
        {
            DominionData d = BuildDominion(out _).DominionReport;
            Assert.AreEqual(45.0, d.ZoneHolds.Single(h => h.Round == 1 && h.Zone == 1 && h.Team == 1).Seconds, 1e-6, "35 s to the round's end at 80 s");
            Assert.AreEqual(10.0, d.ZoneHolds.Single(h => h.Round == 1 && h.Zone == 0 && h.Team == 0).Seconds, 1e-6);
            ZoneRoundRow top = d.ZoneRounds.Single(z => z.Round == 1 && z.Zone == 0);
            Assert.AreEqual(2, top.Captures);
            Assert.AreEqual(20.0, top.FirstCaptureAfter.Value, 1e-6);
            Assert.IsFalse(d.ZoneRounds.Any(z => ReportMaps.IsSpawn(ReportMap.Lane, z.Zone)), "a spawn is never fought over");
        }

        [Test] public void EachShieldsEndIsRead()
        {
            ShieldEnd shield = BuildDominion(out _).DominionReport.Shields.Single();
            Assert.AreEqual("left spawn", shield.Reason);
            Assert.AreEqual(2, shield.Blocked);
            Assert.AreEqual(1, shield.Actor);
            Assert.AreEqual(0, shield.Team);
        }

        [Test] public void ABuildIsWhatAPlayerHeldForMostOfTheRound()
        {
            DominionData d = BuildDominion(out _).DominionReport;
            BuildRow rex = d.Builds.Single(b => b.Round == 1 && b.Actor == 1001);
            Assert.AreEqual(5, rex.Weapon, "two samples with gun 5 against one with gun 7");
            Assert.AreEqual(12, rex.Attachment);
            Assert.AreEqual(1, rex.Absorb);
            Assert.AreEqual(7, d.Builds.Single(b => b.Round == 2 && b.Actor == 1001).Weapon);
        }

        [Test] public void TimeOnAScoringZoneAndTimeDeadAreCountedPerTab()
        {
            DominionData d = BuildDominion(out _).DominionReport;
            PlayerExtra kim = d.PlayerExtras.Single(x => x.Scope == "round-1" && x.Actor == 1);
            Assert.AreEqual(10.0, kim.ScoringZoneSeconds, 1e-6, "two samples on the top zone; the spawn does not count");
            Assert.AreEqual(6.0, kim.DeadSeconds, 1e-6);
            Assert.AreEqual(0.0, d.PlayerExtras.Single(x => x.Scope == "round-2" && x.Actor == 1).DeadSeconds, 1e-6);
        }

        [Test] public void TheSettingsThatMatterAreTheDominionOnes()
        {
            ModeReport report = BuildDominion(out _);
            string[] labels = report.Settings.Select(s => s[0]).ToArray();
            CollectionAssert.Contains(labels, "Respawn shield");
            CollectionAssert.Contains(labels, "Shop by round");
            CollectionAssert.DoesNotContain(labels, "Centre payout", "a 2v2 has no centre");
            Assert.AreEqual("2 per team", report.Settings.Single(s => s[0] == "Team size")[1]);
        }

        [Test] public void ALogWithoutTheDominionSettingsSaysSo()
        {
            string folder = Folder("2026-01-02_0304_Dominion-2v2_Old",
                ("1_Kim.jsonl", new[] { "{'e':'session','t':1,'schema':1,'m':'M2','a':1,'nick':'Kim','tm':0,'master':true,'tuning':{}}", Marker(5, DominionMarkerNotes.RoundStart(1)) }));
            TelemetryLog log = TelemetryLog.Load(folder);
            ModeReport report = ModeReportBuilder.Build(log, ModeReportBuilder.ModeOf(log), null);
            Assert.AreEqual(ReportMode.Dominion2v2, report.Mode, "the folder's name");
            StringAssert.StartsWith("not in this log", report.Settings.Single(s => s[0] == "Dominion settings")[1]);
            Assert.AreEqual("Dominion 2v2, no winner recorded", report.Title);
        }

        // ---------------------------------------------------------------- Conquest

        [Test] public void ATwoTeamConquestHidesItsClosedCornerAndHasNoPhaseTabs()
        {
            string folder = Folder("2026-01-02_0304_Conquest-3v3_Test",
                ("1_Kim.jsonl", new[]
                {
                    "{'e':'session','t':1,'schema':1,'m':'M3','a':1,'nick':'Kim','tm':0,'master':true,'tuning':{}}",
                    "{'e':'shrink','t':5,'tm':2,'zones':[2,4,5,8]}",
                    "{'e':'scan','t':40,'startT':30,'tm':0,'caught':3}",
                }),
                ("2_Lou.jsonl", new[]
                {
                    "{'e':'session','t':1,'schema':1,'m':'M3','a':2,'nick':'Lou','tm':1,'master':false,'tuning':{}}",
                    "{'e':'scan','t':40,'startT':30.02,'tm':0,'caught':-1}",
                }));
            TelemetryLog log = TelemetryLog.Load(folder);
            ModeReport report = ModeReportBuilder.Build(log, ModeReportBuilder.ModeOf(log), null);
            Assert.AreEqual(ReportMode.Conquest3v3, report.Mode);
            CollectionAssert.AreEqual(new[] { "whole-match" }, report.Tabs.Select(t => t.Key).ToArray());
            CollectionAssert.AreEquivalent(new[] { 2, 4, 5, 8 }, report.HiddenZones);
            Assert.IsFalse(report.ShowsOverPower);
            ScanWave wave = report.ConquestReport.ScanWaves.Single();
            Assert.AreEqual(0, wave.Holder);
            Assert.AreEqual(3, wave.Caught, "every client logs the wave; the holder's team knows what it spotted");
            Assert.AreEqual(2, report.ConquestReport.Shrink.Team);
        }

        [Test] public void AnOldTwoTeamConquestStillHidesTheThirdCorner()
        {
            string folder = Folder("2026-01-02_0304_Conquest-3v3_Old",
                ("1_Kim.jsonl", new[] { "{'e':'session','t':1,'schema':1,'m':'M4','a':1,'nick':'Kim','tm':0,'master':true,'tuning':{}}" }));
            TelemetryLog log = TelemetryLog.Load(folder);
            CollectionAssert.AreEquivalent(ReportMaps.ClosedZones(2), ModeReportBuilder.Build(log, ModeReportBuilder.ModeOf(log), null).HiddenZones);
        }

        [Test] public void AMatchThatNamesNoModeKeepsThePhaseTabs()
        {
            TelemetryLog log = TelemetryLog.Load(Path.Combine(Application.dataPath, "_Project/Tests/Telemetry/Fixtures/match_a"));
            ModeReport report = ModeReportBuilder.Build(log, ModeReportBuilder.ModeOf(log), TelemetryAggregator.BuildSet(log));
            Assert.AreEqual(ReportMode.Conquest3v3v3, report.Mode);
            CollectionAssert.AreEqual(new[] { "whole-match", "phase-1", "phase-2" }, report.Tabs.Select(t => t.Key).ToArray());
            CollectionAssert.Contains(report.Tabs[0].Sections, "economy");
        }

        [Test] public void ALogOfNothingStillBuildsAReport()
        {
            ModeReport report = ModeReportBuilder.Build(null, ModeReportBuilder.ModeOf(null), null);
            Assert.AreEqual(1, report.Tabs.Count);
            Assert.IsNotEmpty(report.Title);
        }
    }
}
