namespace Overpower.Dominion
{
    /// <summary>
    /// What Dominion changes about the ground and the gold (Task 4), as plain yes/no and number rules so they can be tested without a room.
    /// The wiring passes DominionMode.IsActive() for isDominion. A capital is a team's respawn and healing spot in Dominion: nobody captures
    /// or drains it, yet it still counts as held for "capture next to a zone you hold" (the owner never changes, so adjacency is untouched).
    /// Nobody earns or sees gold: Dominion has no income, no capture bounty in gold (the bounty POINTS are Task 3's and stay) and no gold readout.
    /// </summary>
    public static class DominionTerritoryRules
    {
        /// <summary>False only for a capital in a Dominion room: it refuses capture and drain, and is never "under attack".</summary>
        public static bool IsCapturable(bool isDominion, bool isCapital) => !(isDominion && isCapital);

        /// <summary>The gold a team earns each second from the ground it holds: whatever Conquest would pay, or nothing in Dominion.</summary>
        public static int TeamIncomeFor(bool isDominion, int conquestTeamIncome) => isDominion ? 0 : conquestTeamIncome;

        /// <summary>The gold a capture bounty pays: the bounty in Conquest, nothing in Dominion (its points are paid separately).</summary>
        public static int BountyGoldFor(bool isDominion, int conquestBounty) => isDominion ? 0 : conquestBounty;

        /// <summary>Whether the HUD draws the player's gold and income.</summary>
        public static bool ShowsGold(bool isDominion) => !isDominion;
    }
}
