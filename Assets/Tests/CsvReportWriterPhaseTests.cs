using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T7: CsvReportWriter.Write(ReportSet, folder) - the three-folder layout
    /// (csv/whole_match, csv/phase1_3teams, csv/phase2_2teams), each with the same 12 files as the
    /// original single-scope Write(ReportTables, folder) (see CsvReportWriterTests for that one),
    /// plus log_coverage.csv as a 13th file in whole_match only.</summary>
    public class CsvReportWriterPhaseTests
    {
        private static readonly string[] TwelveFiles =
        {
            "gold_timeline.csv", "economy_by_minute.csv", "zone_income.csv", "ownership.csv",
            "captures.csv", "purchases.csv", "shop_blocked.csv", "hits.csv", "weapons.csv",
            "abilities.csv", "players.csv", "deaths.csv",
        };

        private static string PhasesFixturePath => Path.Combine(Application.dataPath, "Tests/TelemetryFixtures/match_phases");
        private static string NoPhaseFixturePath => Path.Combine(Application.dataPath, "Tests/TelemetryFixtures/match_a");

        private static string NewTempFolder() =>
            Path.Combine(Path.GetTempPath(), "CsvReportWriterPhaseTests_" + System.Guid.NewGuid().ToString("N"));

        [Test]
        public void AMatchWithAPhase2WritesAllThreeFolders()
        {
            string folder = NewTempFolder();
            Directory.CreateDirectory(folder);
            try
            {
                ReportSet set = TelemetryAggregator.BuildSet(TelemetryLog.Load(PhasesFixturePath));
                Assert.IsNotNull(set.Phase2);

                CsvReportWriter.Write(set, folder);

                string wholeMatch = Path.Combine(folder, "csv", "whole_match");
                string phase1 = Path.Combine(folder, "csv", "phase1_3teams");
                string phase2 = Path.Combine(folder, "csv", "phase2_2teams");

                foreach (string expected in TwelveFiles)
                {
                    Assert.IsTrue(File.Exists(Path.Combine(wholeMatch, expected)), $"whole_match missing {expected}");
                    Assert.IsTrue(File.Exists(Path.Combine(phase1, expected)), $"phase1_3teams missing {expected}");
                    Assert.IsTrue(File.Exists(Path.Combine(phase2, expected)), $"phase2_2teams missing {expected}");
                }

                // The 13th file - whole_match only.
                Assert.IsTrue(File.Exists(Path.Combine(wholeMatch, "log_coverage.csv")));
                Assert.IsFalse(File.Exists(Path.Combine(phase1, "log_coverage.csv")));
                Assert.IsFalse(File.Exists(Path.Combine(phase2, "log_coverage.csv")));

                Assert.AreEqual(13, Directory.GetFiles(wholeMatch, "*.csv").Length);
                Assert.AreEqual(12, Directory.GetFiles(phase1, "*.csv").Length);
                Assert.AreEqual(12, Directory.GetFiles(phase2, "*.csv").Length);

                // log_coverage.csv itself lists actor 3 (no file) as present:false.
                string logCoverageText = File.ReadAllText(Path.Combine(wholeMatch, "log_coverage.csv"));
                string[] rows = logCoverageText.Split('\n').Where(l => l.Trim().Length > 0).ToArray();
                StringAssert.Contains("filePresent", rows[0]);
                Assert.IsTrue(rows.Any(r => r.StartsWith("3,") && r.Contains("false")));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        // Review fix (item 5): the whole-match ownership.csv must carry a "phase" column so a
        // split stint's two halves (zone 5, 60->90 Phase 1 / 90->120 Phase 2) can be told apart
        // in a spreadsheet without cross-referencing the phase1_3teams/phase2_2teams folders.
        [Test]
        public void WholeMatchOwnershipCsvCarriesAPhaseColumnThatTellsASplitStintsHalvesApart()
        {
            string folder = NewTempFolder();
            Directory.CreateDirectory(folder);
            try
            {
                ReportSet set = TelemetryAggregator.BuildSet(TelemetryLog.Load(PhasesFixturePath));
                CsvReportWriter.Write(set, folder);

                string ownershipCsv = File.ReadAllText(Path.Combine(folder, "csv", "whole_match", "ownership.csv"));
                string[] rows = ownershipCsv.Split('\n').Where(l => l.Trim().Length > 0).ToArray();
                StringAssert.Contains("phase", rows[0]);

                string[] header = rows[0].TrimEnd('\r').Split(',');
                int zoneIdx = System.Array.IndexOf(header, "zone");
                int teamIdx = System.Array.IndexOf(header, "team");
                int fromIdx = System.Array.IndexOf(header, "from");
                int phaseIdx = System.Array.IndexOf(header, "phase");
                Assert.AreNotEqual(-1, phaseIdx);

                // Zone 5, team 0: one continuous 60->120 stint in the raw log, split into two CSV
                // rows by the transition at t=90 (see TelemetryPhaseSplitTests for the full derivation).
                var zone5Team0Rows = rows.Skip(1)
                    .Select(r => r.TrimEnd('\r').Split(','))
                    .Where(f => f[zoneIdx] == "5" && f[teamIdx] == "0")
                    .OrderBy(f => double.Parse(f[fromIdx], System.Globalization.CultureInfo.InvariantCulture))
                    .ToList();
                Assert.AreEqual(2, zone5Team0Rows.Count, "zone 5's team-0 stint must appear as two CSV rows");
                Assert.AreEqual("1", zone5Team0Rows[0][phaseIdx]);
                Assert.AreEqual("2", zone5Team0Rows[1][phaseIdx]);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void AMatchWithNoPhase2WritesOnlyTwoFolders()
        {
            string folder = NewTempFolder();
            Directory.CreateDirectory(folder);
            try
            {
                ReportSet set = TelemetryAggregator.BuildSet(TelemetryLog.Load(NoPhaseFixturePath));
                Assert.IsNull(set.Phase2);

                CsvReportWriter.Write(set, folder);

                Assert.IsTrue(Directory.Exists(Path.Combine(folder, "csv", "whole_match")));
                Assert.IsTrue(Directory.Exists(Path.Combine(folder, "csv", "phase1_3teams")));
                Assert.IsFalse(Directory.Exists(Path.Combine(folder, "csv", "phase2_2teams")));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
