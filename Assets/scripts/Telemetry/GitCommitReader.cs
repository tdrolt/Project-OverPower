#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace Overpower.Telemetry
{
    /// <summary>Reads the current commit hash straight out of .git, with no `git` subprocess (slow, and not on PATH on every machine).
    /// Two callers must never fall out of sync: MatchTelemetry's session header (Editor play mode) and BuildInfoWriter's pre-build step
    /// (Assets/scripts/Editor/Telemetry), which bakes the hash into a Resources text asset so a Player build, which has no .git, can
    /// still stamp its header (MatchTelemetry.ReadCommitHash). In the Runtime assembly under #if UNITY_EDITOR because MatchTelemetry
    /// (Runtime) cannot reference Overpower.Editor; the class does not exist in a Player build.</summary>
    public static class GitCommitReader
    {
        /// <summary>The first `length` characters of HEAD's commit hash, or "unknown" if it can't be read. Never throws.</summary>
        public static string ReadShortHash(int length, string projectRoot = null)
        {
            string full = ReadFullHash(projectRoot);
            if (string.IsNullOrEmpty(full)) return "unknown";
            return full.Length <= length ? full : full.Substring(0, length);
        }

        private static string ReadFullHash(string projectRoot)
        {
            try
            {
                string root = projectRoot ?? Directory.GetParent(Application.dataPath).FullName;
                string gitDir = Path.Combine(root, ".git");
                string headPath = Path.Combine(gitDir, "HEAD");
                if (!File.Exists(headPath))
                    return null;

                string head = File.ReadAllText(headPath).Trim();
                if (!head.StartsWith("ref:", StringComparison.Ordinal))
                    return head; // Detached HEAD: the file already holds the hash itself.

                string refName = head.Substring("ref:".Length).Trim();
                string refPath = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refPath))
                    return File.ReadAllText(refPath).Trim();

                // The ref file itself is missing - a repo that has been packed (git gc) keeps ref tips
                // in one flat file instead, one "<hash> <refname>" line each.
                string packedRefsPath = Path.Combine(gitDir, "packed-refs");
                if (!File.Exists(packedRefsPath))
                    return null;

                foreach (string line in File.ReadAllLines(packedRefsPath))
                {
                    if (line.Length == 0 || line[0] == '#' || line[0] == '^')
                        continue;

                    int space = line.IndexOf(' ');
                    if (space <= 0)
                        continue;

                    if (string.Equals(line.Substring(space + 1).Trim(), refName, StringComparison.Ordinal))
                        return line.Substring(0, space).Trim();
                }

                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Telemetry] could not read the git commit hash: {e.Message}");
                return null;
            }
        }
    }
}
#endif
