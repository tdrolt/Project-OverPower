using System.Collections.Generic;
using System.Text;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>The four modes a match report tells apart.</summary>
    public enum ReportMode { Conquest3v3v3, Conquest3v3, Dominion3v3v3, Dominion2v2 }

    /// <summary>The maps a report draws on: the triangle arena (Game Scene) and the lane (Dominion 2v2).</summary>
    public enum ReportMap { Triangle, Lane }

    /// <summary>Which mode a match was and what its report shows. Pure: the mode is read from the logs, else the folder name, else guessed
    /// from the log itself; a log that names no mode and has no Dominion round notes is read as the report always read it (Conquest 3v3v3).</summary>
    public static class ReportModes
    {
        /// <summary>The mode a display name names ("Dominion 2v2"; case, dashes, underscores and extra spaces ignored), or null for any other
        /// text.</summary>
        public static ReportMode? FromName(string name)
        {
            switch (Normalised(name))
            {
                case "conquest 3v3v3": return ReportMode.Conquest3v3v3;
                case "conquest 3v3": return ReportMode.Conquest3v3;
                case "dominion 3v3v3": return ReportMode.Dominion3v3v3;
                case "dominion 2v2": return ReportMode.Dominion2v2;
                default: return null;
            }
        }

        /// <summary>The mode in a match folder's name ("2026-11-01_2346_Dominion-2v2_jjjjs-lobby"): the first part between underscores that
        /// names a mode, so the lobby's name (which comes after the mode) is never read first. Null when no part names one.</summary>
        public static ReportMode? FromFolderName(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return null;
            foreach (string part in folderName.Split('_'))
            {
                ReportMode? mode = FromName(part);
                if (mode.HasValue) return mode;
            }
            return null;
        }

        /// <summary>The mode to report: the first session that names one, else the folder's name, else the log itself: Dominion round notes
        /// make it Dominion (three teams seen: 3v3v3, else 2v2), anything else is Conquest 3v3v3.</summary>
        public static ReportMode Resolve(IEnumerable<string> sessionModeNames, string folderName, bool hasDominionNotes, int teamsSeen)
        {
            if (sessionModeNames != null)
                foreach (string name in sessionModeNames)
                {
                    ReportMode? fromSession = FromName(name);
                    if (fromSession.HasValue) return fromSession.Value;
                }
            ReportMode? fromFolder = FromFolderName(folderName);
            if (fromFolder.HasValue) return fromFolder.Value;
            if (hasDominionNotes) return teamsSeen >= 3 ? ReportMode.Dominion3v3v3 : ReportMode.Dominion2v2;
            return ReportMode.Conquest3v3v3;
        }

        public static string DisplayName(ReportMode mode)
        {
            switch (mode)
            {
                case ReportMode.Conquest3v3: return "Conquest 3v3";
                case ReportMode.Dominion3v3v3: return "Dominion 3v3v3";
                case ReportMode.Dominion2v2: return "Dominion 2v2";
                default: return "Conquest 3v3v3";
            }
        }

        /// <summary>A short id for the page ("dominion-2v2").</summary>
        public static string Key(ReportMode mode) => DisplayName(mode).ToLowerInvariant().Replace(' ', '-');

        public static bool IsDominion(ReportMode mode) => mode == ReportMode.Dominion3v3v3 || mode == ReportMode.Dominion2v2;

        public static int TeamCount(ReportMode mode) => mode == ReportMode.Conquest3v3v3 || mode == ReportMode.Dominion3v3v3 ? 3 : 2;

        /// <summary>The team ids that play: two-team modes play Orange and Purple.</summary>
        public static int[] Teams(ReportMode mode) => TeamCount(mode) == 3 ? new[] { 0, 1, 2 } : new[] { 0, 1 };

        /// <summary>Seats per team when the log does not say: 2 in a 2v2, else 3.</summary>
        public static int DefaultTeamSize(ReportMode mode) => mode == ReportMode.Dominion2v2 ? 2 : 3;

        /// <summary>Dominion 2v2 plays on the lane; every other mode on the triangle arena.</summary>
        public static ReportMap MapOf(ReportMode mode) => mode == ReportMode.Dominion2v2 ? ReportMap.Lane : ReportMap.Triangle;

        /// <summary>Only Conquest 3v3v3 has two phases (three teams, then two after the first knockout); Conquest 3v3 starts with two.</summary>
        public static bool HasPhaseTabs(ReportMode mode) => mode == ReportMode.Conquest3v3v3;

        /// <summary>The GDD's targets were written for Conquest 3v3v3 and are shown for it alone.</summary>
        public static bool ShowsTargets(ReportMode mode) => mode == ReportMode.Conquest3v3v3;

        /// <summary>OverPower cannot trigger with two teams, so a two-team report leaves it out.</summary>
        public static bool ShowsOverPower(ReportMode mode) => TeamCount(mode) == 3;

        /// <summary>Gold, income, prices and purchase timing are Conquest's; Dominion picks its build for free each round.</summary>
        public static bool ShowsGold(ReportMode mode) => !IsDominion(mode);

        /// <summary>Only Dominion 3v3v3 has a centre that pays points.</summary>
        public static bool HasCentrePayouts(ReportMode mode) => mode == ReportMode.Dominion3v3v3;

        private static string Normalised(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var sb = new StringBuilder(name.Length);
            bool space = false;
            foreach (char c in name.Trim())
            {
                bool separator = c == '-' || c == '_' || char.IsWhiteSpace(c);
                if (separator)
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space) sb.Append(' ');
                space = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
