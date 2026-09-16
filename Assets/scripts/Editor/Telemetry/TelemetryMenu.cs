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

        [MenuItem("OverPower/Telemetry/Build Report From Fixture")]
        public static void BuildReportFromFixture()
        {
            string fixtureFolder = Path.Combine(Application.dataPath, FixtureFolder);
            string outFolder = Path.Combine(Path.GetTempPath(), "OverPowerTelemetryFixtureReport_" + DateTime.Now.Ticks);
            BuildReportInto(fixtureFolder, outFolder, true);
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

            TelemetryLog log = TelemetryLog.Load(sourceFolder);
            ReportTables tables = TelemetryAggregator.Build(log);

            Directory.CreateDirectory(outputFolder);
            CsvReportWriter.Write(tables, outputFolder);

            BalanceTargetsData targets = LoadBalanceTargetsData();
            ArenaReportRender.Result arena = ArenaReportRender.Render();
            string htmlPath = HtmlReportWriter.Write(tables, log, targets, arena, outputFolder);

            Debug.Log($"[TelemetryMenu] Built report from '{sourceFolder}' into '{outputFolder}': {htmlPath}");
            if (openInBrowser) Application.OpenURL("file://" + htmlPath.Replace('\\', '/'));
            return htmlPath;
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
