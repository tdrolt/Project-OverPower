namespace Overpower.Bots
{
    /// <summary>Objective first: the zone a bot goes for comes before a chase; an enemy turns it away from that zone only by these rules.</summary>
    public static class BotFocusRules
    {
        /// <summary>Whether the bot leaves its way to the zone to fight its target: always with no zone to go for; else a target within
        /// engageMetres (its build's range plus a little), within guardMetres of the zone, one that hit it lately, or one fighting a teammate
        /// near it. Any other target is only shot if it comes into weapon range on the way.</summary>
        public static bool ShouldEngage(bool hasObjective, float targetMetres, float engageMetres, float targetToObjectiveMetres, float guardMetres,
            bool hitMeLately, bool fightingTeammateNear) =>
            !hasObjective || targetMetres <= engageMetres || targetToObjectiveMetres <= guardMetres || hitMeLately || fightingTeammateNear;

        /// <summary>Whether to go and look where an enemy was last seen: only near the zone it is going for, or with no zone to go for.</summary>
        public static bool ChaseMemory(bool hasObjective, float spotToObjectiveMetres, float guardMetres) =>
            !hasObjective || spotToObjectiveMetres <= guardMetres;
    }

    /// <summary>No spawn camping: an enemy inside its own spawn is no target unless it shoots the bot, and in Dominion a bot never walks
    /// into an enemy spawn. A capital the bot is pushing with its team does not count as a spawn.</summary>
    public static partial class BotSpawnRules
    {
        public static bool OffLimits(bool insideEnemySpawn, bool pushingThatCapital) => insideEnemySpawn && !pushingThatCapital;
    }

    /// <summary>Pace: a chosen goal is kept a moment so the bot does not turn back and forth, and now and then it stands still.</summary>
    public static class BotPaceRules
    {
        /// <summary>Whether to keep the goal chosen secondsHeld ago instead of a different one: for holdSeconds, unless the situation changed
        /// (a fight started or ended, it began backing off or regrouping) or something urgent came up (its capital, a zone being taken).</summary>
        public static bool KeepGoal(float secondsHeld, float holdSeconds, bool situationChanged, bool urgent) =>
            secondsHeld < holdSeconds && !situationChanged && !urgent;

        /// <summary>Whether it stands still this time: when allowed, on a roll (0..1) under the chance.</summary>
        public static bool Pause(bool allowed, float roll, float chance) => allowed && roll < chance;
    }
}
