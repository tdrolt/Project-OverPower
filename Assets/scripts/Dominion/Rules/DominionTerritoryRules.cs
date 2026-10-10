namespace Overpower.Dominion
{
    /// <summary>
    /// What Dominion changes about the ground and the gold, as plain yes/no and number rules so they can be tested without a room.
    /// The wiring passes DominionMode.IsActive() for isDominion. A capital is a team's respawn and healing spot in Dominion: nobody captures
    /// or drains it, yet it still counts as held for "capture next to a zone you hold" (the owner never changes, so adjacency is untouched).
    /// Nobody earns or sees gold: Dominion has no income, no capture bounty in gold (the bounty points stay) and no gold readout.
    /// </summary>
    public static class DominionTerritoryRules
    {
        /// <summary>A capital in a Dominion room is a team's spawn: it looks and is drawn as one, never as a zone.</summary>
        public static bool IsSpawn(bool isDominion, bool isCapital) => isDominion && isCapital;

        /// <summary>False only for a spawn: it refuses capture and drain, and is never "under attack".</summary>
        public static bool IsCapturable(bool isDominion, bool isCapital) => !IsSpawn(isDominion, isCapital);

        /// <summary>The gold a team earns each second from the ground it holds: whatever Conquest would pay, or nothing in Dominion.</summary>
        public static int TeamIncomeFor(bool isDominion, int conquestTeamIncome) => isDominion ? 0 : conquestTeamIncome;

        /// <summary>The gold a capture bounty pays: the bounty in Conquest, nothing in Dominion (its points are paid separately).</summary>
        public static int BountyGoldFor(bool isDominion, int conquestBounty) => isDominion ? 0 : conquestBounty;

        /// <summary>Whether the HUD draws the player's gold and income.</summary>
        public static bool ShowsGold(bool isDominion) => !isDominion;
    }
}
