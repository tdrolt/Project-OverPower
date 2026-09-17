using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Overpower.Data;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T6 step 4: the designer-facing entry point - three menu items, all funnelling
    /// through <see cref="BuildReport(string, bool)"/> so the controller can drive the exact same
    /// build path via eval that a human gets by clicking the menu.</summary>
    public static class TelemetryMenu
    {
        private const string BalanceTargetsAssetPath = "Assets/Gameplay/Config/BalanceTargets.asset";
        private const string FixtureFolder = "Tests/TelemetryFixtures/match_a"; // relative to Application.dataPath

        [MenuItem("OverPower/Telemetry/Build Report…")]
        public static void BuildReportFromDialog()
        {
            string root = TelemetryRoot();
            string defaultFolder = NewestMatchFolder(root) ?? root;
            string folder = EditorUtility.OpenFolderPanel("Build Telemetry Report", defaultFolder, "");
            if (string.IsNullOrEmpty(folder)) return;

            BuildReport(folder, true);
        }

        [MenuItem("OverPower/Telemetry/Open Telemetry Folder")]
        public static void OpenTelemetryFolder()
        {
            string root = TelemetryRoot();
            Directory.CreateDirectory(root);
            EditorUtility.RevealInFinder(root);
        }

        // Review fix (T6 item 3): one fixed folder, deleted and recreated on every call, instead of
        // a fresh DateTime.Now.Ticks folder each time - a repeated "Build Report From Fixture" no
        // longer leaves an ever-growing pile of temp folders behind, and a stale csv/report.html
        // from a previous schema can never linger alongside a fresh build's output.
        private static readonly string FixtureOutputFolder = Path.Combine(Path.GetTempPath(), "OverPowerTelemetryFixtureReport");

        [MenuItem("OverPower/Telemetry/Build Report From Fixture")]
        public static void BuildReportFromFixture()
        {
            string fixtureFolder = Path.Combine(Application.dataPath, FixtureFolder);
            if (Directory.Exists(FixtureOutputFolder)) Directory.Delete(FixtureOutputFolder, true);
            BuildReportInto(fixtureFolder, FixtureOutputFolder, true);
        }

        /// <summary>Task T6 step 4/5: Load -> Build -> CSV + HTML written INTO <paramref name="folder"/>,
        /// then (optionally) opened in the browser. Exposed as a plain static method - not just a
        /// [MenuItem] - because the controller runs this through eval, never by clicking the menu.</summary>
        public static string BuildReport(string folder, bool openInBrowser)
        {
            return BuildReportInto(folder, folder, openInBrowser);
        }

        private static string BuildReportInto(string sourceFolder, string outputFolder, bool openInBrowser)
        {
            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                Debug.LogError($"[TelemetryMenu] No such folder: {sourceFolder}");
                return null;
            }

            // Round-2 review fix (item D, regression): the OLD order cleaned the folder before
            // even checking it held any logs, and before Load/BuildSet could fail - picking the
            // wrong folder in "Build Report..." deleted a stray report.html that happened to be
            // sitting there, and a folder Load threw on was left with no report AND no old one
            // either. Bail out with NO deletion at all when there are no .jsonl logs here...
            if (Directory.GetFiles(sourceFolder, "*.jsonl").Length == 0)
            {
                Debug.LogError($"[TelemetryMenu] No .jsonl telemetry logs found in '{sourceFolder}' - nothing built, nothing deleted.");
                return null;
            }

            // ...and load + aggregate BEFORE cleaning anything, so a folder that fails to parse
            // keeps whatever report it already had.
            TelemetryLog log;
            ReportSet reportSet;
            try
            {
                log = TelemetryLog.Load(sourceFolder);
                // Task T7: one log, three scopes (whole match, Phase 1, Phase 2 when the match had
                // one) - see ReportSet's own comment. CsvReportWriter and HtmlReportWriter both take
                // the whole set now, so the CSV folders and the HTML tabs always match.
                reportSet = TelemetryAggregator.BuildSet(log);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TelemetryMenu] Failed to load/aggregate '{sourceFolder}': {ex.Message} - nothing deleted, folder left as-is.");
                return null;
            }

            // Only NOW, with a successful build in hand, clean the report's OWN known output
            // (report.html, the pre-T7 flat csv/*.csv layout, and the T7 per-scope csv/
            // subfolders) - outputFolder IS sourceFolder for a real match (BuildReport passes the
            // same folder both ways), which also holds the .jsonl logs and possibly the user's own
            // unrelated files, so CleanStaleReportOutputs never recurses or deletes anything but
            // its own known file names (see that method's own comment).
            CleanStaleReportOutputs(outputFolder);

            Directory.CreateDirectory(outputFolder);
            CsvReportWriter.Write(reportSet, outputFolder);

            BalanceTargetsData targets = LoadBalanceTargetsData();
            ArenaReportRender.Result arena = ArenaReportRender.Render();
            string htmlPath = HtmlReportWriter.Write(reportSet, log, targets, arena, outputFolder);

            Debug.Log($"[TelemetryMenu] Built report from '{sourceFolder}' into '{outputFolder}': {htmlPath}");
            // Review fix (T6 item 4): a hand-built "file://" + path string leaves spaces (and any
            // other reserved character) unescaped - persistentDataPath itself contains one on this
            // PC ("...\Project OP\Telemetry\..."). System.Uri percent-encodes the path and
            // normalises the backslashes properly, the same fix already applied to F1's own
            // "Open telemetry folder" build path (see assumptions-for-tudor.md, Task T2).
            if (openInBrowser) Application.OpenURL(new Uri(htmlPath).AbsoluteUri);
            return htmlPath;
        }

        // Review fix (item 7): the exact 12 pre-T7 CSV file names, written flat inside csv/ before
        // the three-folder layout existed - a report built with an old binary (or a stale cached
        // one) could still have left these lying around.
        private static readonly string[] KnownFlatCsvFileNames =
        {
            "gold_timeline.csv", "economy_by_minute.csv", "zone_income.csv", "ownership.csv",
            "captures.csv", "purchases.csv", "shop_blocked.csv", "hits.csv", "weapons.csv",
            "abilities.csv", "players.csv", "deaths.csv", "log_coverage.csv",
        };

        private static void CleanStaleReportOutputs(string outputFolder)
        {
            string reportHtml = Path.Combine(outputFolder, "report.html");
            if (File.Exists(reportHtml)) File.Delete(reportHtml);

            string csvFolder = Path.Combine(outputFolder, "csv");
            if (!Directory.Exists(csvFolder)) return;

            // Pre-T7 flat layout: known files directly inside csv/.
            DeleteKnownCsvFilesOnly(csvFolder);

            // Round-2 review fix (item D, regression): a recursive delete of a whole scope folder
            // destroyed anything else a user had saved in there too (their own spreadsheet, say).
            // Delete only the known file names inside each scope folder, and remove the scope
            // folder itself ONLY if that leaves it completely empty - never recurse.
            foreach (string scopeFolderName in new[] { "whole_match", "phase1_3teams", "phase2_2teams" })
            {
                string scopePath = Path.Combine(csvFolder, scopeFolderName);
                if (!Directory.Exists(scopePath)) continue;
                DeleteKnownCsvFilesOnly(scopePath);
                if (Directory.GetFileSystemEntries(scopePath).Length == 0) Directory.Delete(scopePath);
            }

            // Only remove csv/ itself if cleaning left it empty - never assume it held nothing else.
            if (Directory.GetFileSystemEntries(csvFolder).Length == 0) Directory.Delete(csvFolder);
        }

        private static void DeleteKnownCsvFilesOnly(string folder)
        {
            foreach (string fileName in KnownFlatCsvFileNames)
            {
                string path = Path.Combine(folder, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static BalanceTargetsData LoadBalanceTargetsData()
        {
            var asset = AssetDatabase.LoadAssetAtPath<BalanceTargets>(BalanceTargetsAssetPath);
            return asset != null ? asset.ToData() : new BalanceTargetsData();
        }

        private static string TelemetryRoot()
        {
            return Path.Combine(Application.persistentDataPath, TelemetryFolderName());
        }

        // Any TelemetryConfig asset in the project carries the same folder name every client writes
        // into (MatchTelemetry reads its own wired config's FolderName) - falls back to the shipped
        // default so the menu still works if none is found.
        private static string TelemetryFolderName()
        {
            string[] guids = AssetDatabase.FindAssets("t:TelemetryConfig");
            if (guids.Length > 0)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<TelemetryConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (cfg != null) return cfg.FolderName;
            }
            return "Telemetry";
        }

        private static string NewestMatchFolder(string root)
        {
            if (!Directory.Exists(root)) return null;
            return Directory.GetDirectories(root)
                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
                .FirstOrDefault();
        }
    }
}
