using System.Collections.Generic;
using Overpower.Data;

namespace Overpower.Lobby
{
    /// <summary>
    /// Pure rules behind the name screen, the lobby list and the create screen (lobby Task 9): how a row writes its players, which game modes
    /// the create screen lets you pick, and what the lobby name box starts with. No Unity UI here: the panels read these and draw.
    /// </summary>
    public static class LobbyScreenRules
    {
        // ---- the players text on a list row ----

        private static void Parts(int filledTeamSeats, int teamSeats, int filledSpectatorSeats, out string main, out string spectators)
        {
            main = filledTeamSeats + " / " + teamSeats;
            spectators = filledSpectatorSeats > 0 ? "+" + filledSpectatorSeats + " spec" : "";
        }

        /// <summary>"5 / 9 +1 spec", or "5 / 9" with nobody watching.</summary>
        public static string PlayersText(LobbyEntry entry)
        {
            PlayersPartsOf(entry, out string main, out string spectators);
            return spectators.Length == 0 ? main : main + " " + spectators;
        }

        /// <summary>The two parts of a lobby row's players text: "5 / 9" and, when someone watches or spectator seats exist, "+1 spec" (the list
        /// draws the second part small and muted). A row without a readable fill text shows the room's own player count over its maximum.</summary>
        public static void PlayersPartsOf(LobbyEntry entry, out string main, out string spectators)
        {
            if (entry.FillKnown)
                Parts(entry.FilledTeamSeats, entry.TeamSeats, entry.FilledSpectatorSeats, out main, out spectators);
            else
            {
                main = entry.PlayerCount + " / " + entry.MaxPlayers;
                spectators = "";
            }
        }

        // ---- the connection behind the screens ----

        /// <summary>Find a lobby on a client that dropped off Photon (or never connected) must connect again: nothing else will, and the list would
        /// wait for ever. A client already connecting, joining or disconnecting is left alone (the same check as the rejoin's Leave).</summary>
        public static bool MustConnectForList(Photon.Realtime.ClientState state) =>
            Overpower.Match.BackToNameScreenRules.NextStep(state) == Overpower.Match.ReturnStep.Reconnect;

        /// <summary>Create lobby can be pressed: not already creating, connected to the master server, a mode chosen and a name typed.</summary>
        public static bool CreateMayBePressed(bool creating, bool connectedAndReady, bool hasMode, string name) =>
            !creating && connectedAndReady && hasMode && !string.IsNullOrWhiteSpace(name);

        // ---- the create screen's mode choice ----

        /// <summary>The mode families in the order the catalogue first mentions them (Conquest, then Dominion).</summary>
        public static IReadOnlyList<GameModeFamily> Families(IReadOnlyList<GameModeDefinition> modes)
        {
            var families = new List<GameModeFamily>();
            if (modes == null) return families;
            foreach (GameModeDefinition mode in modes)
                if (mode != null && !families.Contains(mode.Family)) families.Add(mode.Family);
            return families;
        }

        /// <summary>The modes of one family in catalogue order: the size buttons under that family.</summary>
        public static List<GameModeDefinition> SizesOf(IReadOnlyList<GameModeDefinition> modes, GameModeFamily family)
        {
            var sizes = new List<GameModeDefinition>();
            if (modes == null) return sizes;
            foreach (GameModeDefinition mode in modes)
                if (mode != null && mode.Family == family) sizes.Add(mode);
            return sizes;
        }

        /// <summary>Only a mode marked available can be created; the others are greyed with "coming soon".</summary>
        public static bool IsSelectable(GameModeDefinition mode) => mode != null && mode.Available;

        /// <summary>What the create screen starts on: the first available mode of the catalogue (null when none is).</summary>
        public static GameModeDefinition DefaultMode(IReadOnlyList<GameModeDefinition> modes)
        {
            if (modes == null) return null;
            foreach (GameModeDefinition mode in modes)
                if (IsSelectable(mode)) return mode;
            return null;
        }

        /// <summary>True when at least one size of the family can be created.</summary>
        public static bool FamilyIsAvailable(IReadOnlyList<GameModeDefinition> modes, GameModeFamily family)
        {
            foreach (GameModeDefinition mode in SizesOf(modes, family))
                if (IsSelectable(mode)) return true;
            return false;
        }

        /// <summary>Pressing a family button: the current mode when it is already in that family, else the family's first available size.
        /// A family with nothing available changes nothing.</summary>
        public static GameModeDefinition ChooseFamily(IReadOnlyList<GameModeDefinition> modes, GameModeFamily family, GameModeDefinition current)
        {
            if (!FamilyIsAvailable(modes, family)) return current;
            if (current != null && current.Family == family && IsSelectable(current)) return current;
            foreach (GameModeDefinition mode in SizesOf(modes, family))
                if (IsSelectable(mode)) return mode;
            return current;
        }

        /// <summary>Pressing a size button: that size when it can be created, else the choice stays.</summary>
        public static GameModeDefinition ChooseSize(GameModeDefinition candidate, GameModeDefinition current) =>
            IsSelectable(candidate) ? candidate : current;

        // ---- the lobby name ----

        /// <summary>What the lobby name box starts with: the player's name in the format ("{0}'s lobby"), cut to the longest lobby name allowed
        /// and with no space left hanging at the end. An empty player name gives the fallback.</summary>
        public static string LobbyNamePrefill(string playerName, string format, int maxLength, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(playerName)) return fallback ?? "";
            string text = string.Format(format, playerName.Trim());
            if (maxLength > 0 && text.Length > maxLength) text = text.Substring(0, maxLength);
            return text.TrimEnd();
        }
    }
}
