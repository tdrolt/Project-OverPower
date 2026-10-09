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

        /// <summary>The name of a team from a list of team names (index = team number); a team with no name reads "Team 3" (number + 1).</summary>
        public static string TeamName(string[] names, int team)
        {
            if (names != null && team >= 0 && team < names.Length && !string.IsNullOrEmpty(names[team])) return names[team];
            return "Team " + (team + 1);
        }
    }
}
