using System;
using System.Collections.Generic;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Playtest extras P5 (2026-09-26): the pure rules behind MatchLogZip. Plain C#, no Unity/Photon
    /// dependency - same reasoning as ConsoleLineRule/BugMarkRule.
    /// </summary>
    public static class MatchLogZipRule
    {
        /// <summary>Which of a match folder's file names are THIS actor's own: its own session file
        /// (MatchTelemetry.TryOpenFile writes "{actor}_{Sanitize(nick)}.jsonl") and its own Ctrl+B
        /// screenshots (BugMarkerKey writes "bug_{actor}_{t}.png"). Never another actor's files, even
        /// though several local clients on one PC can share this same folder (survey, "Screenshot,
        /// zip") - the trailing underscore in each prefix matters: actor 1 must never also match
        /// actor 10's "10_...jsonl" or "bug_10_...png".</summary>
        public static List<string> SelectOwnFiles(IEnumerable<string> fileNames, int actor)
        {
            var result = new List<string>();
            if (fileNames == null)
                return result;

            string jsonlPrefix = actor + "_";
            string pngPrefix = "bug_" + actor + "_";

            foreach (string name in fileNames)
            {
                if (string.IsNullOrEmpty(name))
                    continue;

                if (name.StartsWith(jsonlPrefix, StringComparison.Ordinal) && name.EndsWith(".jsonl", StringComparison.Ordinal))
                    result.Add(name);
                else if (name.StartsWith(pngPrefix, StringComparison.Ordinal) && name.EndsWith(".png", StringComparison.Ordinal))
                    result.Add(name);
            }

            return result;
        }

        /// <summary>OverPower-log_&lt;dateStamp&gt;_&lt;sanitizedNick&gt;.zip. dateStamp is a plain
        /// parameter (never computed in here) so MatchLogZip can compute it once per match and reuse
        /// it on every later call - the brief's own "overwriting its own earlier zip of the same
        /// match" only holds if the SAME name comes out every time this match is zipped again (once
        /// when the result panel shows, maybe again on quit).</summary>
        public static string ZipFileName(string dateStamp, string sanitizedNick) =>
            $"OverPower-log_{dateStamp}_{sanitizedNick}.zip";
    }
}
