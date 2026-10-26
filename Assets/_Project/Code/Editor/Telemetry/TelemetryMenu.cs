using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Overpower.Data;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>The designer-facing entry point: three menu items, all funnelling through <see cref="BuildReport(string, bool)"/>
    /// so the controller can drive the exact build path a human gets by clicking the menu (via eval).</summary>
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

        // One fixed folder, deleted and recreated on every call, so a repeated "Build Report From Fixture" leaves no pile of temp
        // folders and a stale csv/report.html from an older schema can never linger beside a fresh build.
        private static readonly string FixtureOutputFolder = Path.Combine(Path.GetTempPath(), "OverPowerTelemetryFixtureReport");

        [MenuItem("OverPower/Telemetry/Build Report From Fixture")]
        public static void BuildReportFromFixture()
        {
            string fixtureFolder = Path.Combine(Application.dataPath, FixtureFolder);
            if (Directory.Exists(FixtureOutputFolder)) Directory.Delete(FixtureOutputFolder, true);
            BuildReportInto(fixtureFolder, FixtureOutputFolder, true);
        }

        /// <summary>Load -> Build -> CSV + HTML written INTO <paramref name="folder"/>, then (optionally) opened in the browser. A plain
        /// static method, not just a [MenuItem], because the controller runs it through eval.</summary>
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

            // Bail out with NO deletion at all when there are no .jsonl logs here: cleaning first would delete a stray report.html in
            // a wrongly picked folder, and a folder Load then threw on would be left with no report AND no old one...
            if (Directory.GetFiles(sourceFolder, "*.jsonl").Length == 0)
            {
                Debug.LogError($"[TelemetryMenu] No .jsonl telemetry logs found in '{sourceFolder}' - nothing built, nothing deleted.");
                return null;
            }

            // ...so load + aggregate BEFORE cleaning anything: a folder that fails to parse keeps whatever report it already had.
            TelemetryLog log;
            ReportSet reportSet;
            try
            {
                log = TelemetryLog.Load(sourceFolder);
                // One log, three scopes (whole match, Phase 1, Phase 2 when the match had one; see ReportSet), so the CSV folders and
                // the HTML tabs always match.
                reportSet = TelemetryAggregator.BuildSet(log);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TelemetryMenu] Failed to load/aggregate '{sourceFolder}': {ex.Message} - nothing deleted, folder left as-is.");
                return null;
            }

            // Only NOW, with a successful build in hand, clean the report's OWN known output (report.html, the old flat csv/*.csv
            // layout, the per-scope csv/ subfolders). outputFolder IS sourceFolder for a real match, which also holds the .jsonl logs
            // and possibly the user's own files, so CleanStaleReportOutputs never recurses or deletes anything but its known names.
            CleanStaleReportOutputs(outputFolder);

            Directory.CreateDirectory(outputFolder);
            CsvReportWriter.Write(reportSet, outputFolder);

            BalanceTargetsData targets = LoadBalanceTargetsData();
            ArenaReportRender.Result arena = ArenaReportRender.Render();
            string htmlPath = HtmlReportWriter.Write(reportSet, log, targets, arena, outputFolder);

            Debug.Log($"[TelemetryMenu] Built report from '{sourceFolder}' into '{outputFolder}': {htmlPath}");
            // A hand-built "file://" + path leaves spaces and other reserved characters unescaped (persistentDataPath contains one on
            // this PC). System.Uri percent-encodes the path and normalises the backslashes.
            if (openInBrowser) Application.OpenURL(new Uri(htmlPath).AbsoluteUri);
            return htmlPath;
        }

        // The exact 12 CSV file names written flat inside csv/ before the per-scope folders existed; a report built with an old
        // binary could still have left them lying around.
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

            // Old flat layout: known files directly inside csv/.
            DeleteKnownCsvFilesOnly(csvFolder);

            // Delete only the known file names inside each scope folder, and remove the scope folder ONLY if that leaves it empty:
            // a recursive delete destroyed anything else a user had saved there (their own spreadsheet, say).
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

        // Shares MatchTelemetry's helper, so "Open Telemetry Folder" and "Build Report…"'s default folder point at the SAME place the
        // game just wrote to (the project's "Match logs"; Documents\OverPower\Match logs or the old AppData location only as
        // fallbacks; see TelemetryPaths.ResolveMatchLogsRoot).
        private static string TelemetryRoot()
        {
            return TelemetryPaths.ResolveMatchLogsRoot(TelemetryFolderName());
        }

        // Any TelemetryConfig asset carries the legacy fallback folder name (used only if ResolveMatchLogsRoot falls all the way
        // back to persistentDataPath); falls back to the shipped default so the menu still works if none is found.
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
