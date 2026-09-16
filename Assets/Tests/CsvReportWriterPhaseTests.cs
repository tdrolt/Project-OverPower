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
