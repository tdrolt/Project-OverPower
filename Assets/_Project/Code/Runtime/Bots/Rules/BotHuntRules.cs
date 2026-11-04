namespace Overpower.Bots
{
    /// <summary>Finishing a team in Conquest: a team with no capital left is in its last stand, and its players are hunted wherever the
    /// bot's team knows of them.</summary>
    public static class BotHuntRules
    {
        /// <summary>A team in its last stand (asked while a Conquest match is live): it plays in the match, is not knocked out and holds no
        /// capital.</summary>
        public static bool LastStand(bool inMatch, bool knockedOut, bool hasCapital) => inMatch && !knockedOut && !hasCapital;

        /// <summary>Whether a player is hunted: its team is in its last stand and the bot's team saw it (or the scan showed it) within the
        /// last intelSeconds, at any distance.</summary>
        public static bool Hunted(bool teamInLastStand, float secondsSinceKnown, float intelSeconds) =>
            teamInLastStand && secondsSinceKnown <= intelSeconds;
    }
}
