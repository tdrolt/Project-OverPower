using System;
using System.Collections.Generic;
using System.Linq;

namespace Overpower.Lobby
{
    /// <summary>Where a lobby stands, as the list shows it.</summary>
    public enum LobbyStage { Lobby = 0, Warmup = 1, InMatch = 2 }

    /// <summary>What the button on a lobby row does: take a team seat, watch, or nothing (greyed).</summary>
    public enum JoinAction { Join, Spectate, Full }

    /// <summary>Pure C# rules for the lobby list: its labels, the button and the order. Called by the lobby list
    /// screen.</summary>
    public static class LobbyListRules
    {
        public static string StatusText(LobbyStage stage)
        {
            switch (stage)
            {
                case LobbyStage.Lobby: return "In lobby";
                case LobbyStage.Warmup: return "Warm-up";
                default: return "In match";
            }
        }

        /// <summary>Join while a team seat is free; Spectate only when every team seat is full and a spectator seat
        /// is free; Full when both are.</summary>
        public static JoinAction ActionFor(SeatLayout layout, int filledTeamSeats, int filledSpectatorSeats)
        {
            if (filledTeamSeats < layout.Teams.Length * layout.SeatsPerTeam) return JoinAction.Join;
            if (filledSpectatorSeats < layout.SpectatorSeats) return JoinAction.Spectate;
            return JoinAction.Full;
        }

        /// <summary>
        /// Three groups in this order: joinable lobbies in the lobby or warm-up, then joinable lobbies already in a
        /// match, then full ones; newest first inside each group so a fresh lobby is easy to find.
        /// </summary>
        public static IReadOnlyList<T> Sort<T>(IEnumerable<T> lobbies, Func<T, JoinAction> action, Func<T, LobbyStage> stage, Func<T, long> createdAt)
        {
            int Group(T l)
            {
                if (action(l) == JoinAction.Full) return 2;
                return stage(l) == LobbyStage.InMatch ? 1 : 0;
            }
            return lobbies.OrderBy(Group).ThenByDescending(createdAt).ToList();
        }
    }
}
