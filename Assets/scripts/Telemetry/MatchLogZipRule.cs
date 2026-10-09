using System;
using System.Collections.Generic;

namespace Overpower.Telemetry
{
    /// <summary>
    /// The pure rules behind MatchLogZip. Plain C#, no Unity/Photon dependency.
    /// </summary>
    public enum OpenFolderKind { None, Explorer, Url }

    /// <summary>The Open folder button's action: run explorer.exe with Argument, or open the address Argument.</summary>
    public readonly struct OpenFolderCommand
    {
        public readonly OpenFolderKind Kind;
        public readonly string Argument;
        public OpenFolderCommand(OpenFolderKind kind, string argument) { Kind = kind; Argument = argument; }
    }

    public static class MatchLogZipRule
    {
        /// <summary>After a deliberate leave Photon reports the local actor number as -1 (or 0): the zip that runs after it uses the number
        /// remembered while still in the room, or it would look for "-1_..." files and miss the whole log.</summary>
        public static int ResolveActor(int live, int remembered) => live > 0 ? live : remembered;

        /// <summary>The nick counterpart of <see cref="ResolveActor"/>: the live one while in a room, else the remembered one.</summary>
        public static string ResolveNick(string live, string remembered, int liveActor)
        {
            // After a leave the actor is gone (<= 0) but Photon's NickName is back to the typed name ("Tudor", not the "Tudor 2" worn in the
            // room): the remembered one keeps the zip under its one name (one zip per player per match).
            if (liveActor <= 0 && !string.IsNullOrEmpty(remembered)) return remembered;
            return string.IsNullOrEmpty(live) ? remembered : live;
        }

        /// <summary>The command line for explorer.exe to open a folder on Windows: the path in quotes with backslashes. Unlike a file:// address
        /// it survives '#', '%' and non-ASCII letters in the path. Null for no folder.</summary>
        public static string ExplorerArguments(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            // A trailing separator before the closing quote would escape it (a path ending in a backslash then a quote reads as an
            // unterminated quote), so it goes; a drive root keeps its one ("C:\", not "C:", which is the drive's current folder).
            string path = folder.Replace('/', '\\').TrimEnd('\\');
            if (path.Length == 0 || path.EndsWith(":", StringComparison.Ordinal)) path += "\\";
            return "\"" + path + "\"";
        }

        /// <summary>What the Open folder button does for a platform: Explorer with the quoted path on Windows, the file:// address elsewhere,
        /// nothing for no folder. The button runs exactly this (MatchLogZip.OnOpenFolderClicked), so the command line is testable
        /// without ever opening a window.</summary>
        public static OpenFolderCommand OpenFolder(bool windows, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return new OpenFolderCommand(OpenFolderKind.None, null);
            return windows
                ? new OpenFolderCommand(OpenFolderKind.Explorer, ExplorerArguments(folder))
                : new OpenFolderCommand(OpenFolderKind.Url, FolderUrl(folder));
        }

        /// <summary>Which of a match folder's file names are THIS actor's own: its session file ("{actor}_{Sanitize(nick)}.jsonl")
        /// and its Ctrl+B screenshots ("bug_{actor}_{t}.png"), never another actor's although several local clients on one PC can
        /// share the folder. The trailing underscore in each prefix matters: actor 1 must not match actor 10's files.</summary>
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

        /// <summary>OverPower-log_&lt;matchFolderName&gt;_&lt;actor&gt;_&lt;sanitizedNick&gt;.zip. matchFolderName is a plain parameter
        /// (MatchLogZip passes the match folder's own name, Path.GetFileName of MatchTelemetry.CurrentFolder), never a wall-clock
        /// read: a clock-minute name minted a second file when a quit landed in a different minute than the result panel's zip. The
        /// folder name never changes for a match, so re-zipping always overwrites the same file.</summary>
        public static string ZipFileName(string matchFolderName, string sanitizedNick, int actor) =>
            $"OverPower-log_{matchFolderName}_{actor}_{sanitizedNick}.zip";

        /// <summary>The file:// address Application.OpenURL needs to open a folder in the OS file browser: System.Uri percent-encodes the spaces
        /// ("Match logs") and the brackets of " (2)" and turns the backslashes round. Null for no folder.</summary>
        public static string FolderUrl(string folder) => string.IsNullOrEmpty(folder) ? null : new System.Uri(folder).AbsoluteUri;

        /// <summary>A path with a line-break chance (zero-width space) after every separator, so a long path wraps inside the saved box instead of
        /// running out of it (a path has no spaces for the text to break at).</summary>
        public static string WrappablePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var sb = new System.Text.StringBuilder(path.Length + 16);
            foreach (char c in path)
            {
                sb.Append(c);
                if (c == '/' || c == '\u005C') sb.Append('\u200B');
            }
            return sb.ToString();
        }

        /// <summary>Which folder MatchLogZip.TryZip zips into: the live one when MatchTelemetry still has it, else the last one this
        /// client knew. GameQuit.Quit's PhotonNetwork.Disconnect can raise MatchTelemetry.OnLeftRoom (clearing CurrentFolder) before
        /// this client's OnApplicationQuit runs, so the second, quit-driven zip would find nothing and lose a late line (a bug mark, a
        /// final chat message). lastKnownFolder is MatchLogZip's remembered value, set in HandleBeforeClose (which runs BEFORE
        /// OnLeftRoom clears CurrentFolder) and after every zip.</summary>
        public static string ResolveZipFolder(string currentFolder, string lastKnownFolder) =>
            !string.IsNullOrEmpty(currentFolder) ? currentFolder : lastKnownFolder;

        /// <summary>Whether MatchLogZip.TryZip should attempt the zip: true whenever this client's file has EVER opened.
        /// MatchTelemetry.CurrentFolder is set once, when TryOpenFile first succeeds, and only OnLeftRoom clears it. Deliberately NOT
        /// keyed on MatchTelemetry.IsRecording (writer.IsOpen): that reads false once the writer closes on quit although the file is
        /// complete (TelemetryWriter.Close flushes), and the OnApplicationQuit order of the two components is not guaranteed
        /// (MatchLogZip handles both orders).</summary>
        public static bool ShouldAttemptZip(string currentFolder) => !string.IsNullOrEmpty(currentFolder);
    }
}
