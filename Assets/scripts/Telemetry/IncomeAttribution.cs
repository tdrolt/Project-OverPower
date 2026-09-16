namespace Overpower.Telemetry
{
    /// <summary>Which zones a player's territory gold came from. The wallet only knows a total income per second. This
    /// splits the same number back per zone (each owned zone's tier rate ÷ players per team, as GoldMath pays it), so
    /// the report can show how much each zone earned each team.</summary>
    public static class IncomeAttribution
    {
        /// <param name="teamGoldByTier">Element 0 = Tier 1's team gold per second (the TerritoryConfig tier order).</param>
        /// <param name="perZoneGold">Added to: gold this player earned from each zone over <paramref name="seconds"/>.</param>
        public static void Accumulate(int team, int[] ownerByZone, int[] tierByZone, int[] teamGoldByTier,
                                      int playersPerTeam, double seconds, double[] perZoneGold)
        {
            if (team < 0 || playersPerTeam <= 0 || seconds <= 0) return;
            int zones = System.Math.Min(System.Math.Min(ownerByZone.Length, tierByZone.Length), perZoneGold.Length);
            for (int zone = 0; zone < zones; zone++)
            {
                if (ownerByZone[zone] != team) continue;
                int index = tierByZone[zone] - 1;
                if (index < 0 || index >= teamGoldByTier.Length) continue;
                perZoneGold[zone] += teamGoldByTier[index] * seconds / playersPerTeam;
            }
        }
    }
}
