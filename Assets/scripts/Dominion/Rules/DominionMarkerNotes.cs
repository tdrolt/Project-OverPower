using System.Globalization;

namespace Overpower.Dominion
{
    /// <summary>The telemetry marker notes of the points, bounties and the centre.</summary>
    public static class DominionMarkerNotes
    {
        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        public static string Bounty(int zone, int team, int points) => "dominion bounty zone " + N(zone) + " team " + N(team) + " +" + N(points);

        /// <summary>team -1 = nobody was holding the centre.</summary>
        public static string CentrePayout(int team, int points) =>
            team < 0 ? "dominion centre payout nobody" : "dominion centre payout team " + N(team) + " +" + N(points);
    }
}
