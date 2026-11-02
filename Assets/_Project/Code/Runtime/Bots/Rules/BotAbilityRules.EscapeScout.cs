namespace Overpower.Bots
{
    public static partial class BotAbilityRules
    {
        /// <summary>The Shotgun bot's Zip as an escape: low, an enemy close, and a wall behind it for the bolt to bite (the Zip pulls the
        /// bot to what its bolt hits; into open air it does nothing).</summary>
        public static bool ZipEscape(float health01, float escapeHealth, float nearestEnemyMetres, float enemyMetres, bool wallBehind) =>
            health01 < escapeHealth && nearestEnemyMetres <= enemyMetres && wallBehind;

        /// <summary>The Laser bot stops once per zone it goes for, between minMetres and maxMetres from it with no enemy in sight, to look
        /// ahead through its Scope before it walks in.</summary>
        public static bool Scout(bool hasScope, bool scoutedThisZone, float zoneMetres, float minMetres, float maxMetres, bool enemySeen) =>
            hasScope && !scoutedThisZone && !enemySeen && zoneMetres >= minMetres && zoneMetres <= maxMetres;
    }
}
