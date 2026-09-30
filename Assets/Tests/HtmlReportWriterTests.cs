using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Overpower.Data;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T6: the HTML writer produces a working, self-contained report.html for the T5
    /// fixture (match_a) - non-empty, with the embedded JSON blob and every top-level section id
    /// present - and a player-provided string engineered to look like a script-tag breakout
    /// ("&lt;/script&gt;&lt;b&gt;" as a nickname) comes out of the embedded JSON only as Newtonsoft's
    /// StringEscapeHandling.EscapeHtml \uXXXX form, never as literal markup that could ever close the
    /// surrounding &lt;script&gt; tag.</summary>
    public class HtmlReportWriterTests
    {
        private static string FixturePath => Path.Combine(Application.dataPath, "Tests/TelemetryFixtures/match_a");
        private static string EscapingFixturePath => Path.Combine(Application.dataPath, "Tests/TelemetryFixtures/html_escaping");
        private static string LineSeparatorFixturePath => Path.Combine(Application.dataPath, "Tests/TelemetryFixtures/line_separator");

        [Test]
        public void WritesNonEmptyReportWithEmbeddedDataAndEverySectionId()
        {
            var log = TelemetryLog.Load(FixturePath);
            var reportSet = TelemetryAggregator.BuildSet(log);
            string folder = Path.Combine(Path.GetTempPath(), "OverPowerHtmlReportWriterTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                string path = HtmlReportWriter.Write(reportSet, log, new BalanceTargetsData(), null, folder);
                Assert.IsTrue(File.Exists(path));

                string html = File.ReadAllText(path);
                Assert.IsNotEmpty(html);
                StringAssert.Contains("const DATA = ", html);

                // Task T7: every scope gets its own suffixed copy of every section id (three tabs).
                foreach (string scope in new[] { "whole-match", "phase-1", "phase-2" })
                foreach (string sectionId in new[]
                         {
                             "section-header", "section-economy", "section-territory",
                             "section-combat", "section-players", "section-markers",
                         })
                {
                    StringAssert.Contains("id='" + sectionId + "-" + scope + "'", html);
                }

                // Review fix (item 12): the 'no elimination' note's TEXT lives in every tab's
                // static template (hidden by default) - its mere presence in the HTML source
                // proves nothing about which tab the page's own JS actually shows it in at
                // runtime (this test never executes that JS). What IS verifiable statically:
                // (a) the embedded DATA's own phase2 is null for match_a (the exact signal the
                // page branches on), and (b) the Phase 2 tab's own copy of the note starts
                // hidden by default in the markup, same as every other tab's copy.
                StringAssert.Contains("\"phase2\":null", html);
                StringAssert.Contains("<div id='phase2-empty-note-phase-2' class='warn' style='display:none'>", html);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void PlayerProvidedNicknameIsEscapedInEmbeddedJson()
        {
            var log = TelemetryLog.Load(EscapingFixturePath);
            var reportSet = TelemetryAggregator.BuildSet(log);
            string folder = Path.Combine(Path.GetTempPath(), "OverPowerHtmlReportWriterEscTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                string path = HtmlReportWriter.Write(reportSet, log, new BalanceTargetsData(), null, folder);
                string html = File.ReadAllText(path);

                // The raw payload must never appear literally in the page (it would close the
                // embedding <script> tag)...
                StringAssert.DoesNotContain("</script><b>", html);
                // ...but its \uXXXX-escaped form does, inside the embedded JSON.
                StringAssert.Contains("\\u003c/script\\u003e\\u003cb\\u003e", html);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        /// <summary>Review fix (T6 item 6): U+2028/U+2029 (LINE/PARAGRAPH SEPARATOR) are valid inside
        /// a JSON string but were not valid inside a JS string literal until ES2019 - a raw one in a
        /// nickname could corrupt the embedding `const DATA = {...};` statement in an older/
        /// non-browser JS engine. Newtonsoft's StringEscapeHandling.EscapeHtml doesn't touch them, so
        /// HtmlReportWriter.EscapeLineTerminators does, as a final pass.</summary>
        [Test]
        public void LineAndParagraphSeparatorsAreEscapedInEmbeddedJson()
        {
            var log = TelemetryLog.Load(LineSeparatorFixturePath);
            var reportSet = TelemetryAggregator.BuildSet(log);
            string folder = Path.Combine(Path.GetTempPath(), "OverPowerHtmlReportWriterLsTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                string path = HtmlReportWriter.Write(reportSet, log, new BalanceTargetsData(), null, folder);
                string html = File.ReadAllText(path);

                Assert.IsFalse(html.Contains("\u2028"), "a raw U+2028 must never reach the embedded JSON");
                Assert.IsFalse(html.Contains("\u2029"), "a raw U+2029 must never reach the embedded JSON");
                StringAssert.Contains("Line\\u2028Break", html);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        /// <summary>Review fix (T6 item 6): Json.NET always writes numbers culture-invariantly
        /// regardless of Thread.CurrentCulture, but this proves it end to end for this project's own
        /// payload rather than trusting that library behaviour blind - under de-DE (comma decimal
        /// separator), the embedded JSON must still read "1.5", never "1,5" (which would also break
        /// JSON.parse entirely, since a bare comma there is a second array/object element).</summary>
        [Test]
        public void EmbeddedJsonUsesInvariantNumberFormattingUnderDeDeCulture()
        {
            var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            string folder = Path.Combine(Path.GetTempPath(), "OverPowerHtmlReportWriterDeDeTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

                var tables = new ReportTables();
                tables.GoldTimeline.Add(new GoldTimelineRow { T = 1.5, Actor = 1, Nick = "Test", Team = 0, Balance = 100, EarnedSoFar = 50, SpentSoFar = 25 });
                var reportSet = new ReportSet { WholeMatch = tables, Phase1 = tables };

                string path = HtmlReportWriter.Write(reportSet, null, new BalanceTargetsData(), null, folder);
                string html = File.ReadAllText(path);

                StringAssert.Contains("\"t\":1.5", html);
                StringAssert.DoesNotContain("\"t\":1,5", html);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
