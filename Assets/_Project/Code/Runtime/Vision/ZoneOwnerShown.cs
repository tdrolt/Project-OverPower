using Overpower.Match;

namespace Overpower.Vision
{
    /// <summary>The owner this player's team believes a zone has. Any display of zone ownership asks this, so two displays always agree.</summary>
    public static class ZoneOwnerShown
    {
        /// <summary>While the zone switch hides the live state (<paramref name="known"/>), the owner the team last saw, even a neutral
        /// one; otherwise the live owner.</summary>
        public static int Pick(bool known, int knownOwner, int live) => known ? knownOwner : live;

        public static int Of(TerritorySnapshot snapshot, int zone)
        {
            bool known = ZoneKnowledge.TryGetDisplayed(zone, out ZoneView view);
            return Pick(known, view.OwnerTeam, snapshot.OwnerOf(zone));
        }
    }
}
