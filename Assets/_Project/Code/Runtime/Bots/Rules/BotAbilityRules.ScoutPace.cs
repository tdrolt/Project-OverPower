namespace Overpower.Bots
{
    public static partial class BotAbilityRules
    {
        /// <summary>The Laser bot stops to look ahead at most once in everySeconds.</summary>
        public static bool ScoutDue(float secondsSinceLastScout, float everySeconds) => secondsSinceLastScout >= everySeconds;
    }
}
