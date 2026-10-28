using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Overpower.Telemetry
{
    /// <summary>
    /// The pure half of sending a client's own match log to the collector (TelemetryConfig > Upload; MatchLogZip.TryUpload sends):
    /// when to send, what the remote file is called, the anonymous copy that is sent, and how a reply is read. The send is plain
    /// text with no extra headers, so a browser makes it without a permission check (no CORS preflight).
    /// </summary>
    public static class MatchLogUploadRules
    {
        public const string ContentType = "text/plain;charset=utf-8";

        /// <summary>What the collector receives (JSON): the shared key, the file name, the anonymous log.</summary>
        [Serializable]
        public struct UploadMessage
        {
            public string key;
            public string file;
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

        public static string Payload(string key, string file, string body) =>
            JsonUtility.ToJson(new UploadMessage { key = key ?? "", file = file ?? "", body = body ?? "" });

        private static readonly Regex ActorField = new Regex("\"a\":(-?\\d+)");
        private static readonly Regex NickField = new Regex("\"nick\":\"((?:[^\"\\\\]|\\\\.)*)\"");
        private static readonly Regex ChatText = new Regex("(\"text\":\")(?:[^\"\\\\]|\\\\.)*(\")");
        private static readonly Regex WindowsUser = new Regex("([A-Za-z]:(?:\\\\\\\\|/)Users(?:\\\\\\\\|/))[^\\\\/\"]+");

        /// <summary>The copy that leaves the PC: every player name the log knows (its own session line and the join lines) becomes
        /// "Player &lt;actor&gt;" wherever it appears, chat lines keep their time but lose their text, and a Windows user folder name in a
        /// path becomes "user". The local file is not changed.</summary>
        public static string Anonymize(string log)
        {
            if (string.IsNullOrEmpty(log)) return log ?? "";
            string[] lines = log.Split('\n');

            var alias = new Dictionary<string, string>();
            foreach (string line in lines)
            {
                var nick = NickField.Match(line);
                var actor = ActorField.Match(line);
                if (nick.Success && actor.Success && nick.Groups[1].Value.Length > 0 && !alias.ContainsKey(nick.Groups[1].Value))
                    alias[nick.Groups[1].Value] = "Player " + actor.Groups[1].Value;
            }
            Regex names = alias.Count == 0 ? null : new Regex(
                "(?<![A-Za-z0-9])(" + string.Join("|", alias.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + ")(?![A-Za-z0-9])");

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Contains("\"e\":\"" + TelemetryKeys.Chat + "\""))
                    line = ChatText.Replace(line, "$1$2");
                if (names != null)
                    line = names.Replace(line, m => alias[m.Value]);
                lines[i] = WindowsUser.Replace(line, "$1user");
            }
            return string.Join("\n", lines);
        }
    }
}
