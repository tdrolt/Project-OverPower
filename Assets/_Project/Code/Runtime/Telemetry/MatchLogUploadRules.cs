using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Overpower.Telemetry
{
    /// <summary>
    /// The pure half of sending a client's own match log to the collector (TelemetryConfig > Upload; MatchLogZip.TryUpload sends):
    /// when to send, what the remote file is called, the copy that is sent (no chat), and how a reply is read. The send is plain
    /// text with no extra headers, so a browser makes it without a permission check (no CORS preflight).
    /// </summary>
    public static class MatchLogUploadRules
    {
        public const string ContentType = "text/plain;charset=utf-8";

        /// <summary>What the collector receives (JSON): the shared key, the file name, the match's folder name and id (every client
        /// of one match sends the same id, so the collector files them together), the log without chat.</summary>
        [Serializable]
        public struct UploadMessage
        {
            public string key;
            public string file;
            public string match;
            public string matchId;
            public string body;
        }

        /// <summary>Development runs (the Editor, the Dev1-4 test clients) send nothing unless allowed, so checks do not fill the
        /// collector's folder.</summary>
        public static bool ShouldUpload(bool telemetryEnabled, bool uploadEnabled, string url, bool developmentRun, bool allowDevelopmentRuns, bool hasLog) =>
            hasLog && ShowsNotice(telemetryEnabled, uploadEnabled, url, developmentRun, allowDevelopmentRuns);

        /// <summary>The name screen says match data is collected exactly when this client would send it.</summary>
        public static bool ShowsNotice(bool telemetryEnabled, bool uploadEnabled, string url, bool developmentRun, bool allowDevelopmentRuns) =>
            telemetryEnabled && uploadEnabled && IsUsableUrl(url) && (!developmentRun || allowDevelopmentRuns);

        public static bool IsUsableUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host);

        /// <summary>The log is sent at the result screen and again once the leave is complete (its last lines); the same unchanged
        /// file is not sent twice.</summary>
        public static bool SendsAgain(string lastSentName, long lastSentLength, string name, long length) =>
            name != lastSentName || length > lastSentLength;

        /// <summary>One file per player per match; a later send of the same match replaces it on the collector's side.</summary>
        public static string RemoteFileName(string matchFolderName, int actor) =>
            Regex.Replace(matchFolderName ?? "match", "[^A-Za-z0-9._-]", "_") + "_" + actor + ".jsonl";

        /// <summary>Google answers a web-app POST with a redirect after the script has run, so 3xx counts as delivered.</summary>
        public static bool IsDelivered(long responseCode) => responseCode >= 200 && responseCode < 400;

        public static string Payload(string key, string file, string match, string matchId, string body) =>
            JsonUtility.ToJson(new UploadMessage { key = key ?? "", file = file ?? "", match = match ?? "", matchId = matchId ?? "", body = body ?? "" });

        private static readonly Regex SessionMatchId = new Regex(
            "\"e\":\"" + TelemetryKeys.Session + "\".*?\"" + TelemetryKeys.MatchId + "\":\"((?:[^\"\\\\]|\\\\.)*)\"");
        private static readonly Regex ChatText = new Regex("(\"text\":\")(?:[^\"\\\\]|\\\\.)*(\")");
        private static readonly Regex WindowsUser = new Regex("([A-Za-z]:(?:\\\\\\\\|/)Users(?:\\\\\\\\|/))[^\\\\/\"]+");

        /// <summary>The match id written on the log's session line ("" when there is none). Every client of one match has the same id.</summary>
        public static string MatchIdOf(string log)
        {
            if (string.IsNullOrEmpty(log)) return "";
            var found = SessionMatchId.Match(log);
            return found.Success ? found.Groups[1].Value : "";
        }

        /// <summary>How long after a Ctrl+B mark the reporter's chat counts as the bug's note (the report reads the same window).</summary>
        public const double BugNoteSeconds = 60.0;

        private static readonly Regex LineTime = new Regex("\"t\":(-?[0-9.eE+-]+)");

        /// <summary>The copy that leaves the PC: player names stay (Tudor, 29 Oct), chat lines keep their time but lose their text
        /// unless they are the player's note on their own Ctrl+B (within BugNoteSeconds of a bug line in this log, which holds only
        /// this player's chat), and a Windows user folder name in a path becomes "user". The local file is not changed.</summary>
        public static string SentCopy(string log)
        {
            if (string.IsNullOrEmpty(log)) return log ?? "";
            string[] lines = log.Split('\n');
            string chat = "\"e\":\"" + TelemetryKeys.Chat + "\"";
            string bug = "\"e\":\"" + TelemetryKeys.Bug + "\"";

            var marks = new List<double>();
            foreach (string line in lines)
                if (line.Contains(bug) && TimeOf(line, out double t)) marks.Add(t);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Contains(chat) && !(TimeOf(line, out double t) && marks.Exists(m => t >= m && t <= m + BugNoteSeconds)))
                    line = ChatText.Replace(line, "$1$2");
                lines[i] = WindowsUser.Replace(line, "$1user");
            }
            return string.Join("\n", lines);
        }

        private static bool TimeOf(string line, out double t)
        {
            t = 0;
            var found = LineTime.Match(line);
            return found.Success && double.TryParse(found.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out t);
        }
    }
}
