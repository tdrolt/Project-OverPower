using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Overpower.Lobby
{
    /// <summary>
    /// The name of a match's log folder (D13). It says when the match was, which mode and size and which lobby, for example
    /// "2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", instead of a random code. Pure text work: <see cref="For"/> builds a name that is not taken,
    /// <see cref="Resolve"/> picks the folder of a match on this PC - the one already holding the match's id, else a new name - so every client of
    /// one match on one machine writes into the same folder and two lobbies never share one.
    /// </summary>
    public static class MatchFolderName
    {
        /// <summary>The lobby part of the name is cut to this many characters.</summary>
        public const int MaxLobbyPart = 24;

        /// <summary>The mode part ("Conquest-3v3v3") is cut to this many characters.</summary>
        public const int MaxModePart = 32;

        private const string DateFormat = "yyyy-MM-dd_HHmm";
        private const string FallbackMode = "Match";
        private const string FallbackLobby = "Lobby";

        /// <summary>The folder name for a match that started at <paramref name="localStart"/>. <paramref name="exists"/> says whether a folder of
        /// that name is already taken by another match: then " (2)", " (3)"... is added until the name is free.</summary>
        public static string For(DateTime localStart, string modeDisplayName, string lobbyName, Func<string, bool> exists)
        {
            string baseName = localStart.ToString(DateFormat, CultureInfo.InvariantCulture)
                + "_" + Part(modeDisplayName, MaxModePart, FallbackMode)
                + "_" + Part(lobbyName, MaxLobbyPart, FallbackLobby);
            if (exists == null || !exists(baseName)) return baseName;
            for (int n = 2; ; n++)
            {
                string candidate = baseName + " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
                if (!exists(candidate)) return candidate;
            }
        }

        /// <summary>The folder a match's logs go into. <paramref name="idsByFolder"/> maps every folder name now under the logs root to the match id
        /// written inside it ("" when it has none). A folder holding <paramref name="matchId"/> is reused (<paramref name="existing"/> true);
        /// otherwise a new name from <see cref="For"/>, where every existing folder counts as taken - one with no id too, since it is never
        /// joined by guessing.</summary>
        public static string Resolve(DateTime localStart, string modeDisplayName, string lobbyName, string matchId,
            IReadOnlyDictionary<string, string> idsByFolder, out bool existing)
        {
            existing = false;
            if (idsByFolder != null && !string.IsNullOrEmpty(matchId))
            {
                string found = null;
                foreach (KeyValuePair<string, string> pair in idsByFolder)
                {
                    if (!string.Equals(pair.Value, matchId, StringComparison.Ordinal)) continue;
                    if (found == null || string.CompareOrdinal(pair.Key, found) < 0) found = pair.Key; // several: the same one every time
                }
                if (found != null)
                {
                    existing = true;
                    return found;
                }
            }

            var taken = new HashSet<string>(idsByFolder != null ? idsByFolder.Keys : (IEnumerable<string>)Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return For(localStart, modeDisplayName, lobbyName, taken.Contains);
        }

        /// <summary>One part of the name: only ASCII letters, digits, '-' and '_' kept (accents folded to the plain letter; control characters,
        /// path characters, apostrophes, '#' and '%' dropped, so the folder opens by path and by file address alike), runs of spaces turned
        /// into one dash, cut to <paramref name="maxLength"/>; <paramref name="fallback"/> when
        /// nothing is left.</summary>
        private static string Part(string text, int maxLength, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            var sb = new StringBuilder(text.Length);
            bool pendingDash = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingDash = sb.Length > 0;
                    continue;
                }
                string plain = Fold(c);
                if (plain.Length == 0) continue;
                if (pendingDash)
                {
                    sb.Append('-');
                    pendingDash = false;
                }
                sb.Append(plain);
            }
            string cleaned = sb.ToString();
            if (cleaned.Length > maxLength) cleaned = cleaned.Substring(0, maxLength);
            cleaned = cleaned.TrimEnd('-', '.'); // a name may not end in a dot, and a cut may leave a dash
            return cleaned.Length == 0 ? fallback : cleaned;
        }

        /// <summary>One character as plain ASCII: letters and digits stay, accented letters lose their accent (ș to s, ă to a, é to e), a few
        /// letters with no accent to strip are spelled out (ß to ss), '-' and '_' stay; everything else (apostrophes, '#', '%', brackets,
        /// symbols, other scripts) gives "" and is dropped.</summary>
        private static string Fold(char c)
        {
            if (c < 128) return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' ? c.ToString() : "";
            switch (c)
            {
                case 'ß': return "ss";
                case 'Æ': return "AE";
                case 'æ': return "ae";
                case 'Ø': return "O";
                case 'ø': return "o";
                case 'Đ': return "D";
                case 'đ': return "d";
                case 'Ł': return "L";
                case 'ł': return "l";
            }
            foreach (char d in c.ToString().Normalize(NormalizationForm.FormD))
                if ((d >= 'a' && d <= 'z') || (d >= 'A' && d <= 'Z')) return d.ToString(); // the base letter; the accent marks are not ASCII letters
            return "";
        }
    }
}
