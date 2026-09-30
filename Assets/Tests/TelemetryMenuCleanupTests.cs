using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T7 review fix (item 7): TelemetryMenu.BuildReport deletes only its OWN known
    /// report outputs (report.html, the pre-T7 flat csv/*.csv layout, the T7 per-scope csv/
    /// subfolders) before writing a fresh report - never the .jsonl logs themselves, which live in
    /// the exact same folder for a real match (BuildReport's own outputFolder == sourceFolder).</summary>
    public class TelemetryMenuCleanupTests
    {
        private static string NewTempFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TelemetryMenuCleanupTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        [Test]
        public void BuildReportRemovesStaleOutputsButNeverTheJsonlLogs()
        {
            string folder = NewTempFolder();
            try
            {
                // The real log - must survive.
                string jsonl =
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"n1\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                    "{\"e\":\"sample\",\"t\":30,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(folder, "1_n1.jsonl"), jsonl);

                // A stale report.html from a previous build.
                File.WriteAllText(Path.Combine(folder, "report.html"), "<html>stale</html>");

                // A stale PRE-T7 flat csv/ layout.
                string csvFolder = Path.Combine(folder, "csv");
                Directory.CreateDirectory(csvFolder);
                File.WriteAllText(Path.Combine(csvFolder, "players.csv"), "actor,name\r\n1,n1\r\n");
                File.WriteAllText(Path.Combine(csvFolder, "hits.csv"), "t,attacker\r\n");

                // A stale T7 per-scope layout, as if from an older schema build.
                string staleWholeMatch = Path.Combine(csvFolder, "whole_match");
                Directory.CreateDirectory(staleWholeMatch);
                File.WriteAllText(Path.Combine(staleWholeMatch, "players.csv"), "STALE\r\n");

                string htmlPath = TelemetryMenu.BuildReport(folder, false);

                Assert.IsTrue(File.Exists(Path.Combine(folder, "1_n1.jsonl")), "the .jsonl log must never be touched");
                Assert.AreEqual(jsonl, File.ReadAllText(Path.Combine(folder, "1_n1.jsonl")), "the .jsonl log's own content must be untouched");

                Assert.IsTrue(File.Exists(htmlPath), "a fresh report.html must be written");
                Assert.AreNotEqual("<html>stale</html>", File.ReadAllText(htmlPath));

                // The stale flat csv/*.csv files are gone.
                Assert.IsFalse(File.Exists(Path.Combine(csvFolder, "hits.csv")));

                // The fresh T7 layout exists, and its players.csv is not the stale placeholder.
                string freshWholeMatchPlayers = Path.Combine(csvFolder, "whole_match", "players.csv");
                Assert.IsTrue(File.Exists(freshWholeMatchPlayers));
                Assert.AreNotEqual("STALE\r\n", File.ReadAllText(freshWholeMatchPlayers));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // ---------------------------------------------------------------- round-2 review fix (item D)

        [Test]
        public void AnUnknownFileInsideAScopeFolderSurvivesRebuilding()
        {
            string folder = NewTempFolder();
            try
            {
                string jsonl = "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"n1\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                               "{\"e\":\"sample\",\"t\":30,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(folder, "1_n1.jsonl"), jsonl);

                // A stale T7 layout, PLUS a user's own file saved directly inside the whole_match
                // scope folder (e.g. a spreadsheet they were working from) - recurse-deleting the
                // scope folder would destroy it even though it is not one of the known CSV names.
                string wholeMatchFolder = Path.Combine(folder, "csv", "whole_match");
                Directory.CreateDirectory(wholeMatchFolder);
                File.WriteAllText(Path.Combine(wholeMatchFolder, "players.csv"), "STALE\r\n");
                File.WriteAllText(Path.Combine(wholeMatchFolder, "my_notes.xlsx"), "not a report file");

                TelemetryMenu.BuildReport(folder, false);

                Assert.IsTrue(File.Exists(Path.Combine(wholeMatchFolder, "my_notes.xlsx")),
                    "an unknown file saved inside a scope folder must survive rebuilding the report");
                Assert.AreEqual("not a report file", File.ReadAllText(Path.Combine(wholeMatchFolder, "my_notes.xlsx")));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void AFolderWithNoJsonlLogsIsLeftCompletelyUntouched()
        {
            string folder = NewTempFolder();
            try
            {
                // No .jsonl anywhere - someone picked the wrong folder in "Build Report...". It
                // still has an unrelated report.html and csv/ folder (e.g. from a DIFFERENT tool,
                // or simply not a telemetry folder at all) that must never be deleted.
                File.WriteAllText(Path.Combine(folder, "report.html"), "<html>not ours</html>");
                string csvFolder = Path.Combine(folder, "csv");
                Directory.CreateDirectory(csvFolder);
                File.WriteAllText(Path.Combine(csvFolder, "players.csv"), "unrelated,data\r\n");

                LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("No \\.jsonl telemetry logs found"));
                string result = TelemetryMenu.BuildReport(folder, false);

                Assert.IsNull(result, "a folder with no .jsonl logs must build nothing");
                Assert.IsTrue(File.Exists(Path.Combine(folder, "report.html")));
                Assert.AreEqual("<html>not ours</html>", File.ReadAllText(Path.Combine(folder, "report.html")));
                Assert.IsTrue(File.Exists(Path.Combine(csvFolder, "players.csv")));
                Assert.AreEqual("unrelated,data\r\n", File.ReadAllText(Path.Combine(csvFolder, "players.csv")));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
