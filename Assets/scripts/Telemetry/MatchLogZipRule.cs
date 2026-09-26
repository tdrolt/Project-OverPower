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

        /// <summary>OverPower-log_&lt;matchFolderName&gt;_&lt;sanitizedNick&gt;.zip. matchFolderName is
        /// a plain parameter (never computed in here) - MatchLogZip passes the match folder's own name
        /// (MatchTelemetry.ResolveMatchFolder's "&lt;dateStamp&gt;_&lt;matchId8&gt;", Path.GetFileName of
        /// MatchTelemetry.CurrentFolder). 2026-09-26 fix: this used to be a wall-clock read
        /// (DateTime.Now) taken the first time either caller zipped, cached for the rest of the match
        /// so a same-minute repeat overwrote itself - but a real two-client check found a genuine quit
        /// could still land in a different clock MINUTE than the result panel's own zip, minting a
        /// second file. The match folder's name never changes for the life of a match, so passing IT
        /// through here instead makes "the SAME name every time this match is zipped again" (the
        /// brief's own "overwriting its own earlier zip of the same match") true unconditionally, not
        /// just within one clock minute.</summary>
        public static string ZipFileName(string matchFolderName, string sanitizedNick) =>
            $"OverPower-log_{matchFolderName}_{sanitizedNick}.zip";

        /// <summary>2026-09-26 fix (the zip-name-fix brief): which folder MatchLogZip.TryZip should
        /// zip into - the live one when MatchTelemetry still has it, otherwise the last one this client
        /// actually knows about. Exists because a real Player quit showed MatchTelemetry.OnLeftRoom
        /// (raised by GameQuit.Quit's own PhotonNetwork.Disconnect, sometimes before this client's own
        /// OnApplicationQuit runs) clearing CurrentFolder between the quit's own zip and MatchLogZip's
        /// second, OnApplicationQuit-driven attempt - that attempt used to find nothing and skip,
        /// harmless on its own, but is worth restoring rather than silently losing a late-arriving line
        /// (a bug mark, a final chat message) logged in the gap between the two. lastKnownFolder is
        /// MatchLogZip's own remembered value (set in HandleBeforeClose, which runs BEFORE OnLeftRoom
        /// clears CurrentFolder - see that method's own comment - and after every zip).</summary>
        public static string ResolveZipFolder(string currentFolder, string lastKnownFolder) =>
            !string.IsNullOrEmpty(currentFolder) ? currentFolder : lastKnownFolder;

        /// <summary>Playtest extras P6 follow-up (item 3): whether MatchLogZip.TryZip should even
        /// attempt to read the match folder and zip - true whenever this client's file has EVER opened.
        /// MatchTelemetry.CurrentFolder is exactly that: set once, the first time TryOpenFile succeeds,
        /// and left alone by every path that can end the match (OnApplicationQuit, OnDestroy) - only
        /// OnLeftRoom clears it, for the next match.
        ///
        /// Deliberately NOT keyed on MatchTelemetry.IsRecording (writer.IsOpen): that reads false the
        /// moment the writer closes on quit, even though the file it leaves behind is already complete
        /// (TelemetryWriter.Close flushes before closing) - keying on IsRecording used to make a
        /// quit-time zip silently do nothing whenever MatchTelemetry.OnApplicationQuit happened to run
        /// first (the two components' OnApplicationQuit order is not guaranteed - see MatchLogZip's own
        /// OnApplicationQuit comment on "handle both orders").</summary>
        public static bool ShouldAttemptZip(string currentFolder) => !string.IsNullOrEmpty(currentFolder);
    }
}
