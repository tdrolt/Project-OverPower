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

        [Test]
        public void WritesNonEmptyReportWithEmbeddedDataAndEverySectionId()
        {
            var log = TelemetryLog.Load(FixturePath);
            var tables = TelemetryAggregator.Build(log);
            string folder = Path.Combine(Path.GetTempPath(), "OverPowerHtmlReportWriterTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                string path = HtmlReportWriter.Write(tables, log, new BalanceTargetsData(), null, folder);
                Assert.IsTrue(File.Exists(path));

                string html = File.ReadAllText(path);
                Assert.IsNotEmpty(html);
                StringAssert.Contains("const DATA = ", html);

                foreach (string id in new[]
                         {
                             "section-header", "section-economy", "section-territory",
                             "section-combat", "section-players", "section-markers",
                         })
                {
                    StringAssert.Contains("id='" + id + "'", html);
                }
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
            var tables = TelemetryAggregator.Build(log);
            string folder = Path.Combine(Path.GetTempPath(), "OverPowerHtmlReportWriterEscTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                string path = HtmlReportWriter.Write(tables, log, new BalanceTargetsData(), null, folder);
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
    }
}
