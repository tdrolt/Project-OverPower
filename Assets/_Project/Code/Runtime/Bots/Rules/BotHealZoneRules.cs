namespace Overpower.Bots
{
    /// <summary>A hurt bot uses a teammate's Heal Zone: below belowHealth it walks into one within maxMetres, while the zone lasts.</summary>
    public static class BotHealZoneRules
    {
        public static bool WalkIn(bool teammatesZone, float health01, float belowHealth, float zoneMetres, float maxMetres) =>
            teammatesZone && health01 < belowHealth && zoneMetres <= maxMetres;
    }
}
