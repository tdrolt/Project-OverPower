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

            // Review fix (item 7): outputFolder IS sourceFolder for a real match (BuildReport
            // passes the same folder both ways), which also holds the .jsonl logs themselves -
            // clean only the report's OWN known output (report.html, the pre-T7 flat csv/*.csv
            // layout, and the T7 per-scope csv/ subfolders) before writing, so rebuilding after a
            // schema change never leaves a stale file from an older layout sitting next to a fresh
            // one. Never touches anything else in the folder.
            CleanStaleReportOutputs(outputFolder);

            TelemetryLog log = TelemetryLog.Load(sourceFolder);
            // Task T7: one log, three scopes (whole match, Phase 1, Phase 2 when the match had one) -
            // see ReportSet's own comment. CsvReportWriter and HtmlReportWriter both take the whole
            // set now, so the CSV folders and the HTML tabs are always built from the exact same data.
            ReportSet reportSet = TelemetryAggregator.BuildSet(log);

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

            foreach (string fileName in KnownFlatCsvFileNames)
            {
                string path = Path.Combine(csvFolder, fileName);
                if (File.Exists(path)) File.Delete(path);
            }

            foreach (string scopeFolderName in new[] { "whole_match", "phase1_3teams", "phase2_2teams" })
            {
                string scopePath = Path.Combine(csvFolder, scopeFolderName);
                if (Directory.Exists(scopePath)) Directory.Delete(scopePath, true);
            }

            // Only remove csv/ itself if cleaning left it empty - never assume it held nothing else.
            if (Directory.Exists(csvFolder) && Directory.GetFileSystemEntries(csvFolder).Length == 0)
                Directory.Delete(csvFolder);
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
