using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T5, opus review (second pass): one focused fixture per fix, each built from a
    /// small hand-written temp-directory log (not the shared match_a fixture - see
    /// TelemetryAggregatorTests for that one), so a change to one scenario can't perturb another's
    /// numbers. See TelemetryAggregator's own class comment for the numbered list these correspond to.</summary>
    public class TelemetryAggregatorReviewFixesTests
    {
        private static string NewTempFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TelemetryReviewFixTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        private static string Session(int actor, int team, bool master, string tuningJson) =>
            "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":" + actor + ",\"nick\":\"n" + actor +
            "\",\"tm\":" + team + ",\"master\":" + (master ? "true" : "false") +
            ",\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":" + tuningJson + "}\n";

        // ---------------------------------------------------------------- item 1 (HIGH): captures rewrite

        [Test]
        public void CapturesHandleMultipleCycles_TeamACapturesThenTeamBDrainsThenTeamBCaptures()
        {
            string temp = NewTempFolder();
            try
            {
                string lines =
                    Session(1, 0, true, "{}") +
                    // Team A (0) captures zone 0: started 10 -> owns it at 30.
                    "{\"e\":\"capture\",\"t\":10,\"zone\":0,\"tm\":0,\"state\":\"started\",\"progress\":0.01,\"players\":1}\n" +
                    "{\"e\":\"ownership\",\"t\":30,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1000}\n" +
                    // Team B (1) drains it: drainStarted, reaching neutral at 220.
                    "{\"e\":\"capture\",\"t\":150,\"zone\":0,\"tm\":1,\"state\":\"drainStarted\",\"progress\":-0.01,\"players\":1}\n" +
                    "{\"e\":\"ownership\",\"t\":220,\"zone\":0,\"tier\":2,\"old\":0,\"new\":-1,\"since\":2000}\n" +
                    // Team B captures the now-neutral zone fresh: started 225 -> owns it at 245.
                    "{\"e\":\"capture\",\"t\":225,\"zone\":0,\"tm\":1,\"state\":\"started\",\"progress\":0.01,\"players\":1}\n" +
                    "{\"e\":\"ownership\",\"t\":245,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":1,\"since\":3000}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                Assert.AreEqual(3, tables.Captures.Count);

                var row1 = tables.Captures.Single(c => Math.Abs(c.Start - 10.0) < 1e-9);
                Assert.AreEqual(0, row1.Team);
                Assert.AreEqual(30.0, row1.End, 1e-9);
                Assert.AreEqual("completed", row1.Outcome);
                Assert.AreEqual(20.0, row1.Duration, 1e-9);

                var row2 = tables.Captures.Single(c => Math.Abs(c.Start - 150.0) < 1e-9);
                Assert.AreEqual(1, row2.Team);
                Assert.AreEqual(220.0, row2.End, 1e-9);
                Assert.AreEqual("neutralised", row2.Outcome);

                var row3 = tables.Captures.Single(c => Math.Abs(c.Start - 225.0) < 1e-9);
                Assert.AreEqual(1, row3.Team);
                Assert.AreEqual(245.0, row3.End, 1e-9);
                Assert.AreEqual("completed", row3.Outcome);
                Assert.AreEqual(20.0, row3.Duration, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 3: dead samples excluded

        [Test]
        public void DeadSamplesDoNotAccrueEquippedOrZoneTime()
        {
            string temp = NewTempFolder();
            try
            {
                string tuning = "{\"telemetry\":{\"sampleIntervalSeconds\":10},\"weapons\":[{\"id\":1}]}";
                string lines = Session(1, 0, true, tuning) +
                    "{\"e\":\"sample\",\"t\":0,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    // Died sometime during [0,10) - this sample is DEAD, so [10,20) must not count.
                    "{\"e\":\"sample\",\"t\":10,\"bal\":0,\"x\":0,\"z\":0,\"alive\":false,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":0,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"sample\",\"t\":20,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"sample\",\"t\":30,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var w1 = tables.Weapons.Single(w => w.WeaponId == 1);
                // [0,10) alive -> 10s; [10,20) starts DEAD -> skipped; [20,30) alive -> 10s.
                // Trailing interval after t=30 (coverage ends at 30 too) adds 0.
                Assert.AreEqual(20.0, w1.TimeEquippedSeconds, 1e-6);

                var p1 = tables.Players.Single(p => p.Actor == 1);
                Assert.AreEqual(20.0, p1.TimeNeutralZone, 1e-6); // same intervals, zone -1 throughout
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 5: tier bounds don't crash

        [Test]
        public void InvalidTierValuesAreSkippedNotCrashed()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"ownership\",\"t\":5,\"zone\":0,\"tier\":0,\"old\":-1,\"new\":0,\"since\":1}\n" + // tier 0: out of range
                    "{\"e\":\"ownership\",\"t\":5,\"zone\":1,\"tier\":5,\"old\":-1,\"new\":0,\"since\":2}\n" + // tier 5: out of range
                    "{\"e\":\"sample\",\"t\":10,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":0,\"tm\":0,\"w\":-1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"goldEarned\",\"t\":10,\"terr\":10,\"zones\":[10,0],\"bounty\":0,\"refund\":0,\"debug\":0,\"other\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                ReportTables tables = null;
                Assert.DoesNotThrow(() => tables = TelemetryAggregator.Build(TelemetryLog.Load(temp)));

                // 2 invalid zones x 1 minute (the "zones held" sweep) + 2 invalid zones in the one
                // goldEarned line's own zones[] (the income accumulation) = 4.
                Assert.AreEqual(4, tables.Header.InvalidTierCount);
                Assert.AreEqual(1, tables.EconomyByMinute.Count); // still builds a normal row otherwise
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 4: zone income rescaling

        [Test]
        public void GoldEarnedZonesAreRescaledToSumToTerr()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"ownership\",\"t\":2,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1}\n" +
                    "{\"e\":\"sample\",\"t\":1,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":0,\"tm\":0,\"w\":-1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    // zones[0]=9 but terr=10 - a real rounding mismatch (each zone rounded to whole gold at the source).
                    "{\"e\":\"goldEarned\",\"t\":5,\"terr\":10,\"zones\":[9,0],\"bounty\":0,\"refund\":0,\"debug\":0,\"other\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var zoneRow = tables.ZoneIncome.Single(z => z.Zone == 0 && z.Team == 0);
                // Rescaled: 9 * (10/9) = 10, not the raw unscaled 9.
                Assert.AreEqual(10.0, zoneRow.GoldGenerated, 1e-6);

                var econRow = tables.EconomyByMinute.Single(r => r.Minute == 0 && r.Team == 0);
                Assert.AreEqual(10.0, econRow.IncomeByTier[1], 1e-6); // tier 2 -> index 1, same rescale applied
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 6: splash hits separated from accuracy

        [Test]
        public void SplashHitsAreCountedSeparatelyFromAccuracy()
        {
            string temp = NewTempFolder();
            try
            {
                string tuning = "{\"weapons\":[{\"id\":5}]}";
                string lines = Session(1, 0, true, tuning) +
                    "{\"e\":\"shots\",\"t\":1,\"w\":5,\"pulls\":1,\"proj\":1}\n" +
                    "{\"e\":\"hit\",\"t\":1,\"a\":1,\"at\":0,\"v\":2,\"vt\":1,\"w\":5,\"ab\":-1,\"src\":\"Projectile\",\"raw\":10,\"arm\":0,\"hpLost\":10,\"lethal\":false,\"d\":5,\"vul\":0,\"op\":false}\n" +
                    "{\"e\":\"hit\",\"t\":1,\"a\":1,\"at\":0,\"v\":3,\"vt\":1,\"w\":5,\"ab\":-1,\"src\":\"Splash\",\"raw\":4,\"arm\":0,\"hpLost\":4,\"lethal\":false,\"d\":null,\"vul\":0,\"op\":false}\n" +
                    "{\"e\":\"hit\",\"t\":1,\"a\":1,\"at\":0,\"v\":4,\"vt\":1,\"w\":5,\"ab\":-1,\"src\":\"Splash\",\"raw\":4,\"arm\":0,\"hpLost\":4,\"lethal\":false,\"d\":null,\"vul\":0,\"op\":false}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                var w5 = tables.Weapons.Single(w => w.WeaponId == 5);

                Assert.AreEqual(1, w5.Hits);           // Projectile only
                Assert.AreEqual(2, w5.SplashHits);     // Splash, counted separately
                Assert.AreEqual(1.0, w5.Accuracy, 1e-9); // 1 hit / 1 projectile - NOT 3/1 = 300%
                Assert.AreEqual(18f, w5.DamageRaw, 1e-6); // damage still includes all 3 (10+4+4)
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 12: culture invariance

        [Test]
        public void AggregateAndCsvWriteAreInvariantUnderDutchCulture()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            string temp = NewTempFolder();
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("nl-NL"); // comma decimals, semicolon lists

                string tuning = "{\"weapons\":[{\"id\":1}]}";
                string lines = Session(1, 0, true, tuning) +
                    "{\"e\":\"sample\",\"t\":0,\"bal\":0,\"x\":1.5,\"z\":2.5,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100.25,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"hit\",\"t\":1,\"a\":1,\"at\":0,\"v\":2,\"vt\":1,\"w\":1,\"ab\":-1,\"src\":\"Projectile\",\"raw\":11.25,\"arm\":0,\"hpLost\":11.25,\"lethal\":false,\"d\":3.5,\"vul\":0,\"op\":false}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                CsvReportWriter.Write(tables, temp);

                string hitsCsv = File.ReadAllText(Path.Combine(temp, "csv", "hits.csv"));
                string[] hitsLines = hitsCsv.Replace("﻿", "").Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual(2, hitsLines.Length); // header + 1 row
                int headerColumns = hitsLines[0].Split(',').Length;
                int rowColumns = hitsLines[1].Split(',').Length;
                // If a decimal used a comma under nl-NL, the row would split into MORE fields than
                // the header - invariant culture keeps every number a plain "11.25", never "11,25".
                Assert.AreEqual(headerColumns, rowColumns);
                StringAssert.Contains("11.25", hitsLines[1]);
                StringAssert.DoesNotContain("11,25", hitsLines[1]);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Directory.Delete(temp, true);
            }
        }
    }
}
