namespace Overpower.Lobby
{
    /// <summary>
    /// Pure rules behind the lobby room screen and the warm-up bar: what a taken seat reads, and what a team is called in a
    /// sentence. No Unity UI here: the panels read these and draw.
    /// </summary>
    public static class LobbyRoomRules
    {
        /// <summary>What a taken seat's button reads: the player's name, then " (you)" on your own seat and " · host" on the host's.
        /// A seat whose holder has no readable name reads "?".</summary>
        public static string SeatText(string playerName, bool mine, bool host, string mineSuffix, string hostSuffix)
        {
            string text = string.IsNullOrEmpty(playerName) ? "?" : playerName;
            if (mine) text += mineSuffix;
            if (host) text += hostSuffix;
            return text;
        }

        /// <summary>A seat's text with the lobby-only badge after a bot's name (in a match a bot's name stands alone, like a player's).</summary>
        public static string SeatText(string playerName, bool mine, bool host, string mineSuffix, string hostSuffix, bool bot, string botSuffix)
        {
            string text = SeatText(playerName, mine, host, mineSuffix, hostSuffix);
            return bot ? text + botSuffix : text;
        }

        /// <summary>Whether a seat shows the small x that takes its bot out: to the host only, on a bot's seat only. A click on the rest of
        /// the seat asks for it, as on any seat.</summary>
        public static bool ShowsRemoveBot(bool iAmHost, int holderActor) => iAmHost && Overpower.Net.BotRosterRules.IsBotActor(holderActor);

        /// <summary>The name of a team from a list of team names (index = team number); a team with no name reads "Team 3" (number + 1).</summary>
        public static string TeamName(string[] names, int team)
        {
            if (names != null && team >= 0 && team < names.Length && !string.IsNullOrEmpty(names[team])) return names[team];
            return "Team " + (team + 1);
        }
    }
}
