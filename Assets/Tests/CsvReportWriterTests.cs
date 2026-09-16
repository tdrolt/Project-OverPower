using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T5: CsvReportWriter writes all 12 CSVs, RFC-4180-quoted, invariant culture. Uses a
    /// small hand-built ReportTables (not the fixture log) so this test is only about the CSV format,
    /// not about aggregation correctness (see TelemetryAggregatorTests for that).</summary>
    public class CsvReportWriterTests
    {
        private static readonly string[] ExpectedFiles =
        {
            "gold_timeline.csv", "economy_by_minute.csv", "zone_income.csv", "ownership.csv",
            "captures.csv", "purchases.csv", "shop_blocked.csv", "hits.csv", "weapons.csv",
            "abilities.csv", "players.csv", "deaths.csv",
        };

        private static ReportTables MinimalTables()
        {
            var tables = new ReportTables();
            tables.Players.Add(new PlayerRow { Actor = 1, Nick = "Smith, \"Ace\" John", Team = 0 });
            tables.Purchases.Add(new PurchaseRow { T = 1.5, Actor = 1, Nick = "Editor", Team = 0, Kind = "purchase", Category = "weapon", ItemId = 2, Amount = 1200, BalanceAfter = 300, Zone = 0, Free = false });
            return tables;
        }

        [Test]
        public void WritesAllTwelveCsvsWithHeaderRows()
        {
            string folder = Path.Combine(Path.GetTempPath(), "CsvReportWriterTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                CsvReportWriter.Write(MinimalTables(), folder);

                string csvDir = Path.Combine(folder, "csv");
                Assert.IsTrue(Directory.Exists(csvDir));

                foreach (string expected in ExpectedFiles)
                    Assert.IsTrue(File.Exists(Path.Combine(csvDir, expected)), $"missing {expected}");

                Assert.AreEqual(ExpectedFiles.Length, Directory.GetFiles(csvDir, "*.csv").Length);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void EscapesACommaAndQuoteContainingName()
        {
            string folder = Path.Combine(Path.GetTempPath(), "CsvReportWriterTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                CsvReportWriter.Write(MinimalTables(), folder);

                string text = File.ReadAllText(Path.Combine(folder, "csv", "players.csv"));
                // RFC-4180: a field containing a comma or a quote is wrapped in quotes, with inner quotes doubled.
                Assert.IsTrue(text.Contains("\"Smith, \"\"Ace\"\" John\""), text);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void PurchasesHeaderRowNamesItsColumns()
        {
            string folder = Path.Combine(Path.GetTempPath(), "CsvReportWriterTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                CsvReportWriter.Write(MinimalTables(), folder);

                string[] lines = File.ReadAllLines(Path.Combine(folder, "csv", "purchases.csv"));
                Assert.GreaterOrEqual(lines.Length, 2); // header + the one row
                StringAssert.Contains("category", lines[0]);
                StringAssert.Contains("price", lines[0]);
                Assert.IsTrue(lines[1].Contains("1200"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
