using System;
using System.Collections.Generic;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>What the page leaves out. Everything here stays in the CSV files and the raw logs; the page shows what a designer reads a
    /// match by.</summary>
    public static class ReportVisibility
    {
        private static readonly string[] Hidden =
        {
            // Bookkeeping: the match id, code version and the raw settings dump, the player and log coverage tables, the skipped-line
            // counters and the console.
            "matchId", "commits", "tuning", "coverage", "logCoverage", "skippedLines", "console",
            // Numbers that name things instead of the names.
            "actor", "itemId",
            // Combat detail.
            "pulls", "projectiles", "splashHits", "armorDamage", "healthDamage", "meanDistance", "medianDistance",
            "statusCount", "statusSeconds", "marksPlaced",
            // Gold that is not play.
            "goldDebug", "goldOther", "goldRefund",
        };

        /// <summary>The page's names of the stats it leaves out (the page's table columns carry the same names).</summary>
        public static IReadOnlyList<string> HiddenStats() => Hidden;

        public static bool HidesStat(string stat) => stat != null && Array.IndexOf(Hidden, stat) >= 0;

        private static readonly string[] LobbyNotePrefixes = { "seat taken: ", "seat left: ", "lobby created: " };

        private static readonly string[] LobbyNotes =
        {
            LobbyMarkerNotes.StartGame, LobbyMarkerNotes.EndWarmup, "countdown start", "countdown cancelled",
            "two-team lobby on", "two-team lobby off",
        };

        // The early-lines note after its count ("12 earliest lines were dropped ...").
        private static readonly string EarlyLinesSuffix = LobbyMarkerNotes.EarlyLinesDropped(0).Substring(1);

        /// <summary>True for a marker the page leaves out: the lobby's own notes (seat taken or left, a bot or spectator joined, the lobby
        /// created, start game, end of warm-up, the countdown starting or cancelled, the two-team lobby switch), the note that early lines were
        /// dropped, and Dominion's round story (the Result section tells it). F1 markers, a new host and anything else stay.</summary>
        public static bool HidesMarker(string note)
        {
            if (string.IsNullOrEmpty(note)) return false;
            if (LobbyMarkerNotes.TryReadBot(note, out _) || LobbyMarkerNotes.TryReadSpectator(note, out _)) return true;
            foreach (string prefix in LobbyNotePrefixes)
                if (note.StartsWith(prefix, StringComparison.Ordinal)) return true;
            if (Array.IndexOf(LobbyNotes, note) >= 0) return true;
            if (note.EndsWith(EarlyLinesSuffix, StringComparison.Ordinal)) return true;
            return note.StartsWith("dominion ", StringComparison.Ordinal);
        }
    }
}
