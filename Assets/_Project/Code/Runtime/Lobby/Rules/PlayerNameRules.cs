using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Overpower.Lobby
{
    /// <summary>Pure C# rules for player names on the name screen and in a lobby. Called by the name screen and the
    /// lobby room.</summary>
    public static class PlayerNameRules
    {
        private static readonly Regex LettersAndDigits = new Regex("^[a-zA-Z0-9]+$");

        /// <summary>Letters and digits only, with a maximum so a name fits above a player's head.</summary>
        public static bool IsValid(string name, int min, int max) =>
            name != null && name.Length >= min && name.Length <= max && LettersAndDigits.IsMatch(name);

        /// <summary>The wanted name if nobody has it, otherwise the format with 2, 3, ... (Tudor 2), comparing
        /// ignoring case so "tudor" cannot pass for "Tudor".</summary>
        public static string UniqueName(string wanted, IEnumerable<string> others, string format)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string other in others)
                if (other != null) taken.Add(other);
            if (!taken.Contains(wanted)) return wanted;
            for (int n = 2; ; n++)
            {
                string candidate = string.Format(format, wanted, n);
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
