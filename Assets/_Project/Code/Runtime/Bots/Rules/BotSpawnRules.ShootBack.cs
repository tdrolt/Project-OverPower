namespace Overpower.Bots
{
    public static partial class BotSpawnRules
    {
        /// <summary>An enemy in its own spawn is left alone unless it hit the bot lately: then the bot shoots back (from outside the spawn).</summary>
        public static bool LeaveAlone(bool inItsSpawn, bool hitMeLately) => inItsSpawn && !hitMeLately;

        /// <summary>Walking into an enemy spawn: off limits in Dominion only; in Conquest pushing a team, its capital included, is fine.</summary>
        public static bool KeepOut(bool dominion, bool insideEnemySpawn) => dominion && insideEnemySpawn;
    }
}
