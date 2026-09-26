using System;
using System.IO;
using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Playtest extras Task 2 (P4): the Bug reports and per-player Console sections' own
    /// pure aggregation - built from small hand-written temp-directory logs (two players/files),
    /// following TelemetryAggregatorReviewFixesTests' own style (see that class for the pattern).
    /// Covers exactly what the task brief asks to guard: a bug card's own chat/console windows, the
    /// per-player Console grouping, and that console/bug/chat are no longer counted as unknown
    /// events.</summary>
    public class TelemetryBugConsoleReportTests
    {
        private static string NewTempFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TelemetryBugConsoleTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        private static string Session(int actor, string nick, int team, bool master) =>
            "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":" + actor + ",\"nick\":\"" + nick +
            "\",\"tm\":" + team + ",\"master\":" + (master ? "true" : "false") +
            ",\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n";

        [Test]
        public void ConsoleBugAndChatAreNeverCountedAsUnknownEvents()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1, "P1", 0, true) +
                    "{\"e\":\"bug\",\"t\":100,\"a\":1,\"tm\":0,\"x\":10,\"z\":20,\"alive\":true,\"zone\":3,\"w\":1,\"eq\":2,\"mob\":3,\"ult\":4,\"img\":\"bug_1_100.png\"}\n" +
                    "{\"e\":\"chat\",\"t\":110,\"a\":1,\"text\":\"note\"}\n" +
                    "{\"e\":\"console\",\"t\":85,\"state\":\"warning\",\"msg\":\"W1\",\"n\":1}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var log = TelemetryLog.Load(temp);
                Assert.AreEqual(0, log.UnknownEventCount);
            }
            finally { Directory.Delete(temp, true); }
        }

        [Test]
        public void BugCardWindowsTheReportersChatAndEveryClientsConsoleLines()
        {
            string temp = NewTempFolder();
            try
            {
                string file1 = Session(1, "P1", 0, true) +
                    "{\"e\":\"bug\",\"t\":100,\"a\":1,\"tm\":0,\"x\":10,\"z\":20,\"alive\":true,\"zone\":3,\"w\":1,\"eq\":2,\"mob\":3,\"ult\":4,\"img\":\"bug_1_100.png\"}\n" +
                    // Reporter's own chat: one inside [t, t+60], one just past it.
                    "{\"e\":\"chat\",\"t\":110,\"a\":1,\"text\":\"note within window\"}\n" +
                    "{\"e\":\"chat\",\"t\":161,\"a\":1,\"text\":\"note too late\"}\n" +
                    // Own console lines: one inside [t-20, t+5], one too early, one too late.
                    "{\"e\":\"console\",\"t\":85,\"state\":\"warning\",\"msg\":\"W1\",\"n\":1}\n" +
                    "{\"e\":\"console\",\"t\":50,\"state\":\"error\",\"msg\":\"too early\",\"n\":1}\n" +
                    "{\"e\":\"console\",\"t\":110,\"state\":\"log\",\"msg\":\"too late\",\"n\":1}\n";
                string file2 = Session(2, "P2", 1, false) +
                    // Another player's chat in the window - must NOT be treated as the reporter's own note.
                    "{\"e\":\"chat\",\"t\":120,\"a\":2,\"text\":\"not the reporter\"}\n" +
                    // Another player's console line inside the window - included, labelled by THEIR nick.
                    "{\"e\":\"console\",\"t\":90,\"state\":\"error\",\"msg\":\"E1\",\"n\":1}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), file1);
                File.WriteAllText(Path.Combine(temp, "2.jsonl"), file2);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                Assert.AreEqual(1, tables.Header.Bugs.Count);
                var bug = tables.Header.Bugs[0];
                Assert.AreEqual(1, bug.Actor);
                Assert.AreEqual("P1", bug.Nick);
                Assert.AreEqual(3, bug.Zone);
                Assert.AreEqual(1, bug.Weapon);
                Assert.AreEqual(2, bug.Equipment);
                Assert.AreEqual(3, bug.Mobility);
                Assert.AreEqual(4, bug.Ultimate);
                Assert.AreEqual("bug_1_100.png", bug.ScreenshotFile);

                Assert.AreEqual(1, bug.ChatNotes.Count);
                Assert.AreEqual("note within window", bug.ChatNotes[0]);

                Assert.AreEqual(2, bug.ConsoleWindow.Count);
                Assert.AreEqual(85.0, bug.ConsoleWindow[0].T, 1e-9);
                Assert.AreEqual("P1", bug.ConsoleWindow[0].Nick);
                Assert.AreEqual("warning", bug.ConsoleWindow[0].Level);
                Assert.AreEqual(90.0, bug.ConsoleWindow[1].T, 1e-9);
                Assert.AreEqual("P2", bug.ConsoleWindow[1].Nick);
                Assert.AreEqual("error", bug.ConsoleWindow[1].Level);
            }
            finally { Directory.Delete(temp, true); }
        }

        [Test]
        public void ConsoleByPlayerGroupsRepeatsAndLeavesPlainLogLinesOut()
        {
            string temp = NewTempFolder();
            try
            {
                string file1 = Session(1, "P1", 0, true) +
                    "{\"e\":\"console\",\"t\":10,\"state\":\"warning\",\"msg\":\"Recurring\",\"n\":1}\n" +
                    "{\"e\":\"console\",\"t\":15,\"state\":\"log\",\"msg\":\"just info\",\"n\":1}\n" +
                    "{\"e\":\"console\",\"t\":200,\"state\":\"warning\",\"msg\":\"Recurring\",\"n\":3,\"firstT\":195,\"lastT\":200}\n" +
                    "{\"e\":\"console\",\"t\":5,\"state\":\"dropped\",\"dropped\":7}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), file1);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                Assert.AreEqual(1, tables.Header.ConsoleByPlayer.Count); // "just info" (log) and "dropped" both excluded
                var group = tables.Header.ConsoleByPlayer[0];
                Assert.AreEqual(1, group.Actor);
                Assert.AreEqual("P1", group.Nick);
                Assert.AreEqual("warning", group.Level);
                Assert.AreEqual("Recurring", group.Message);
                Assert.AreEqual(4, group.Count);       // 1 (t=10) + 3 (the folded t=200 line's own n)
                Assert.AreEqual(10.0, group.FirstT, 1e-9);
                Assert.AreEqual(200.0, group.LastT, 1e-9);
            }
            finally { Directory.Delete(temp, true); }
        }

        // Step 0 review fix (d), 2026-09-26: console.csv used to write only "error"/"warning" rows -
        // narrower than ConsoleByPlayer (which already excludes "log"/"dropped" - see the test above)
        // and narrower than the HTML's own per-player Console section, which also shows "exception"
        // and "assert". Now console.csv writes every ConsoleByPlayer row with no further filtering.
        [Test]
        public void ConsoleCsvIncludesExceptionAndAssertRowsNotJustErrorAndWarning()
        {
            string temp = NewTempFolder();
            string outFolder = Path.Combine(Path.GetTempPath(), "TelemetryBugConsoleCsvTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                string file1 = Session(1, "P1", 0, true) +
                    "{\"e\":\"console\",\"t\":10,\"state\":\"warning\",\"msg\":\"W\",\"n\":1}\n" +
                    "{\"e\":\"console\",\"t\":11,\"state\":\"error\",\"msg\":\"E\",\"n\":1}\n" +
                    "{\"e\":\"console\",\"t\":12,\"state\":\"exception\",\"msg\":\"X\",\"n\":1}\n" +
                    "{\"e\":\"console\",\"t\":13,\"state\":\"assert\",\"msg\":\"A\",\"n\":1}\n" +
                    "{\"e\":\"console\",\"t\":14,\"state\":\"log\",\"msg\":\"just info\",\"n\":1}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), file1);

                ReportSet set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));
                CsvReportWriter.Write(set, outFolder);

                string csvText = File.ReadAllText(Path.Combine(outFolder, "csv", "whole_match", "console.csv"));
                StringAssert.Contains(",exception,", csvText);
                StringAssert.Contains(",assert,", csvText);
                StringAssert.Contains(",warning,", csvText);
                StringAssert.Contains(",error,", csvText);
                Assert.IsFalse(csvText.Contains("just info"), "the plain log line must stay out, same as ConsoleByPlayer");
            }
            finally
            {
                Directory.Delete(temp, true);
                if (Directory.Exists(outFolder)) Directory.Delete(outFolder, true);
            }
        }
    }
}
