using NUnit.Framework;
using Overpower.Match;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    public class IncomeAttributionTests
    {
        [Test]
        public void EachOwnedZoneGetsItsTierRateDividedByPlayersPerTeam()
        {
            // Team 0 owns zone 0 (T2, 5/s) and zone 3 (T3, 10/s); 3 players per team; 5 seconds.
            int[] owners = { 0, 1, -1, 0 };
            int[] tiers = { 2, 2, 2, 3 };
            int[] tierRates = { 0, 5, 10, 8 };
            var perZone = new double[4];
            IncomeAttribution.Accumulate(0, owners, tiers, tierRates, 3, 5.0, perZone);
            Assert.AreEqual(5.0 * 5 / 3, perZone[0], 1e-9);
            Assert.AreEqual(0.0, perZone[1], 1e-9);
            Assert.AreEqual(10.0 * 5 / 3, perZone[3], 1e-9);
        }

        [Test]
        public void TheSplitSumsToTheWalletsTerritoryIncome()
        {
            int[] owners = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            int[] tiers = { 2, 2, 2, 3, 3, 3, 1, 1, 1, 4 };
            int[] tierRates = { 0, 5, 10, 8 };
            var perZone = new double[10];
            IncomeAttribution.Accumulate(0, owners, tiers, tierRates, 3, 2.0, perZone);
            double sum = 0; foreach (double v in perZone) sum += v;

            // Derived from GoldMath itself (not hand-computed) so a future GoldMath change that would
            // change what the wallet actually pays breaks this test instead of leaving it silently wrong.
            int teamIncome = GoldMath.TeamIncomePerSecond(0, owners, tiers, tierRates);
            double walletRate = GoldMath.PlayerIncomePerSecond(teamIncome, 3);
            Assert.AreEqual(walletRate * 2.0, sum, 1e-9);
        }

        [Test]
        public void NoTeamAddsNothingButZeroPlayersIsTreatedAsOne()
        {
            var perZone = new double[2];
            IncomeAttribution.Accumulate(-1, new[] { -1, -1 }, new[] { 2, 2 }, new[] { 0, 5, 10, 8 }, 3, 1, perZone);
            Assert.AreEqual(0.0, perZone[0], 1e-9);
            Assert.AreEqual(0.0, perZone[1], 1e-9);

            // playersPerTeam <= 0 mirrors GoldMath.PlayerIncomePerSecond's Math.Max(1, playersPerTeam):
            // treated as one player, not skipped - a room that hasn't reported a player count yet must
            // never silently drop income from the split instead of just under-dividing it for a frame.
            IncomeAttribution.Accumulate(0, new[] { 0, 0 }, new[] { 2, 2 }, new[] { 0, 5, 10, 8 }, 0, 1, perZone);
            Assert.AreEqual(5.0, perZone[0], 1e-9);
            Assert.AreEqual(5.0, perZone[1], 1e-9);
        }

        [Test]
        public void MismatchedArrayLengthsCountOnlyTheOverlappingZones()
        {
            // ownerByZone is longer than tierByZone, which is shorter than perZoneGold - Accumulate
            // takes the shortest of the three rather than throwing, since the room's replicated arrays
            // (owners, tiers) and this caller's own output array need not agree in length for this to
            // stay safe.
            int[] owners = { 0, 0, 0, 0 };
            int[] tiers = { 2, 2 };
            int[] tierRates = { 0, 5, 10, 8 };
            var perZone = new double[3];

            Assert.DoesNotThrow(() => IncomeAttribution.Accumulate(0, owners, tiers, tierRates, 1, 1.0, perZone));
            Assert.AreEqual(5.0, perZone[0], 1e-9);
            Assert.AreEqual(5.0, perZone[1], 1e-9);
            Assert.AreEqual(0.0, perZone[2], 1e-9); // Beyond tierByZone's length - never touched.
        }
    }
}
