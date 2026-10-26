using System;
using System.IO;
using UnityEngine;

namespace Overpower.Telemetry
{
    /// <summary>Shared by the game (MatchTelemetry, MatchLogZip) and the Editor's TelemetryMenu, so both agree on where match logs live.
    /// Root order (TelemetryPathRules.Choose decides between the probed results): the game/project folder's "Match logs" subfolder
    /// (the parent of Application.dataPath: a build's folder, or the Unity project folder in the Editor); then
    /// Documents\OverPower\Match logs (e.g. the game sits in Program Files); then the persistentDataPath location (AppData on
    /// Windows), so telemetry is never lost. Each probe is exception-safe and logs one warning on failure before the next.</summary>
    public static class TelemetryPaths
    {
        public const string MatchLogsFolderName = "Match logs";
        private const string DocumentsAppFolderName = "OverPower";

        /// <summary>legacyFolderName is TelemetryConfig.FolderName, used only by the last-resort fallback to keep old logs and new
        /// ones from mixing under a different name.</summary>
        public static string ResolveMatchLogsRoot(string legacyFolderName)
        {
            string gameFolder = SafeParentOf(Application.dataPath);
            bool gameFolderOk = TryUseFolder(gameFolder, MatchLogsFolderName, out string gameRoot);

            string documentsRoot = null;
            bool documentsOk = false;
            if (!gameFolderOk)
            {
                string documentsBase = SafeDocumentsFolder();
                documentsOk = TryUseFolder(
                    string.IsNullOrEmpty(documentsBase) ? null : Path.Combine(documentsBase, DocumentsAppFolderName),
                    MatchLogsFolderName, out documentsRoot);
            }

            switch (TelemetryPathRules.Choose(gameFolderOk, documentsOk))
            {
                case MatchLogLocation.GameFolder:
                    return gameRoot;
                case MatchLogLocation.Documents:
                    return documentsRoot;
                default:
                    return LegacyRoot(legacyFolderName);
            }
        }

        /// <summary>The persistentDataPath location (AppData on Windows), used only when neither the game folder nor Documents
        /// can be written to.</summary>
        public static string LegacyRoot(string legacyFolderName)
        {
            string root = Path.Combine(Application.persistentDataPath, string.IsNullOrEmpty(legacyFolderName) ? "Telemetry" : legacyFolderName);
            try
            {
                Directory.CreateDirectory(root);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Telemetry] could not create the fallback telemetry folder '{root}': {e.Message}");
            }
            return root;
        }

        private static string SafeParentOf(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? null : Directory.GetParent(path)?.FullName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string SafeDocumentsFolder()
        {
            try
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Creates baseFolder/subFolderName and writes a throwaway probe file into it: Directory.CreateDirectory alone can
        /// succeed on a read-only mount and still fail every later write. Any failure logs one warning and returns false.</summary>
        private static bool TryUseFolder(string baseFolder, string subFolderName, out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(baseFolder))
                return false;

            string candidate = Path.Combine(baseFolder, subFolderName);
            try
            {
                Directory.CreateDirectory(candidate);
                string probe = Path.Combine(candidate, ".write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                result = candidate;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Telemetry] could not use '{candidate}' for match logs ({e.Message}) - trying the next fallback.");
                return false;
            }
        }
    }
}
