namespace Overpower.Match
{
    /// <summary>GDD p.15: health only comes back in safe territory, fastest in the capital. Gated on the
    /// same out-of-combat clock as armor, so standing in your zone mid-fight is not free healing.</summary>
    public static class HealthRegenRule
    {
        public static float RegenPerSecond(bool standingInOwnZone, float tierRegenPerSecond,
                                           float secondsSinceCombat, float outOfCombatSeconds)
        {
            if (!standingInOwnZone || tierRegenPerSecond <= 0f) return 0f;
            return secondsSinceCombat >= outOfCombatSeconds ? tierRegenPerSecond : 0f;
        }
    }
}
