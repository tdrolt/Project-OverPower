using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>
    /// The page and the CSV files a match's mode gets: a Dominion match has a tab per round on the lane's own map and names, no GDD targets and
    /// no lobby notes; the stats the page leaves out stay in the CSV files; a Conquest 3v3v3 match keeps its phase tabs and targets.
    /// </summary>
    public class HtmlModeReportTests
    {
        private string folder;

        [SetUp]
        public void MakeMatch()
        {
            folder = Path.Combine(Path.GetTempPath(), "HtmlModeReportTests_" + Guid.NewGuid().ToString("N"), "2026-01-02_0304_Dominion-2v2_Test-lobby");
            Directory.CreateDirectory(folder);
            int[] teams = { 0, 1 };
            string M(double t, string note) => "{'e':'marker','t':" + t.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",'a':1,'note':'" + note + "'}";
            File.WriteAllLines(Path.Combine(folder, "1_Kim.jsonl"), new[]
            {
                "{'e':'session','t':1,'schema':1,'m':'M1','a':1,'nick':'Kim','tm':0,'master':true,'mode':'Dominion 2v2','map':'Dominion 2v2','teamSize':2,'tuning':{}}",
                M(1.5, "seat taken: sS1"),
                "{'e':'phase','t':2,'num':0,'remain':[]}",
                "{'e':'phase','t':10,'num':2,'remain':[0,1]}",
                M(20, DominionMarkerNotes.RoundStart(1)),
                "{'e':'sample','t':30,'bal':0,'x':70,'z':55,'alive':true,'zone':0,'tm':0,'w':1,'eq':-1,'mob':-1,'ult':-1,'abl':0,'rcl':0}",
                M(80, DominionMarkerNotes.RoundEnd(1, 0, teams, new[] { 50, 20 })),
                M(80, DominionMarkerNotes.MatchOver(0, teams, new[] { 2, 0 })),
            }.Select(l => l.Replace('\'', '"')));
        }

        [TearDown]
        public void RemoveMatch()
        {
            string root = Directory.GetParent(folder).FullName;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test] public void ADominionMatchGetsATabPerRoundOnTheLane()
        {
            string html = File.ReadAllText(TelemetryMenu.BuildReport(folder, false));
            StringAssert.Contains("id='tab-round-1'", html);
            StringAssert.Contains("id='section-result-round-1'", html);
            StringAssert.Contains("\"Top zone\"", html);
            StringAssert.DoesNotContain("base (T1)", html, "the triangle arena's names on the lane");
            StringAssert.Contains("\"targets\":null", html, "the GDD's targets are Conquest 3v3v3's");
        }

        [Test] public void TheLobbysNotesAreNotOnThePage()
        {
            string html = File.ReadAllText(TelemetryMenu.BuildReport(folder, false));
            StringAssert.DoesNotContain("seat taken", html);
            StringAssert.Contains("\"hiddenStats\":[", html);
        }

        [Test] public void WhatThePageLeavesOutStaysInTheCsvFiles()
        {
            TelemetryMenu.BuildReport(folder, false);
            string csv = Path.Combine(folder, "csv");
            StringAssert.Contains("pulls", File.ReadAllText(Path.Combine(csv, "whole_match", "weapons.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(csv, "whole_match", "roster.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(csv, "whole_match", "rounds.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(csv, "round_1", "players.csv")), "each round has its own tables");
        }

        [Test] public void TheLanesZoneNamesAreInTheCsvFiles()
        {
            var tables = new ReportTables();
            tables.Ownership.Add(new OwnershipRow { Zone = 0, Tier = 3, Team = 1, From = 1, To = 2, Duration = 1, HowEnded = "matchEnd" });
            var set = new ReportSet { WholeMatch = tables, Phase1 = tables };
            string output = Path.Combine(Directory.GetParent(folder).FullName, "csv-only");
            CsvReportWriter.Write(set, output, ReportMap.Lane);
            StringAssert.Contains("Top zone", File.ReadAllText(Path.Combine(output, "csv", "whole_match", "ownership.csv")));
        }

        [Test] public void AConquestThreeTeamsReportKeepsItsPhaseTabsAndTargets()
        {
            string fixture = Path.Combine(Application.dataPath, "_Project/Tests/Telemetry/Fixtures/match_a");
            TelemetryLog log = TelemetryLog.Load(fixture);
            string output = Path.Combine(Directory.GetParent(folder).FullName, "conquest");
            string html = File.ReadAllText(HtmlReportWriter.Write(TelemetryAggregator.BuildSet(log), log, new BalanceTargetsData(), null, output));
            StringAssert.Contains("id='tab-phase-1'", html);
            StringAssert.Contains("id='tab-phase-2'", html);
            StringAssert.DoesNotContain("\"targets\":null", html);
            StringAssert.Contains("Orange base (T1)", html);
        }
    }
}
