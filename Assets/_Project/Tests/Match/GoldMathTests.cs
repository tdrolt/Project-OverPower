using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class GoldMathTests
    {
        // Zone id -> tier for the real map: 0-2 Tier 2, 3-5 Tier 3, 6-8 capitals, 9 centre.
        private static readonly int[] Tiers = { 2, 2, 2, 3, 3, 3, 1, 1, 1, 4 };
        private static readonly int[] TeamGoldByTier = { 0, 5, 10, 8 };

        private static int[] Owners(params (int zone, int team)[] held)
        {
            var owners = new int[10];
            for (int i = 0; i < owners.Length; i++) owners[i] = TerritoryMap.Neutral;
            foreach (var (zone, team) in held) owners[zone] = team;
            return owners;
        }

        [Test]
        public void TheGddStrugglingRowEarnsFifteenPerSecond()
        {
            // GDD p.37: one Tier 3 + one Tier 2 = 15/sec for the team. Capitals earn nothing.
            int[] owners = Owners((6, 0), (0, 0), (3, 0));
            Assert.AreEqual(15, GoldMath.TeamIncomePerSecond(0, owners, Tiers, TeamGoldByTier));
        }

        [Test]
        public void TheGddAverageRowEarnsTwentyThree()
        {
            int[] owners = Owners((6, 0), (0, 0), (3, 0), (9, 0));
            Assert.AreEqual(23, GoldMath.TeamIncomePerSecond(0, owners, Tiers, TeamGoldByTier));
        }

        [Test]
        public void OtherTeamsZonesEarnYouNothing()
        {
            int[] owners = Owners((3, 1), (4, 2));
            Assert.AreEqual(0, GoldMath.TeamIncomePerSecond(0, owners, Tiers, TeamGoldByTier));
        }

        [Test]
        public void AZoneWithNoTierEarnsNothing()
        {
            int[] owners = Owners((0, 0));
            int[] noTiers = new int[10];
            Assert.AreEqual(0, GoldMath.TeamIncomePerSecond(0, owners, noTiers, TeamGoldByTier));
        }

        [Test]
        public void EachPlayerGetsTheTeamIncomeDividedByPlayersPerTeam()
        {
            Assert.AreEqual(5.0, GoldMath.PlayerIncomePerSecond(15, 3), 1e-9);
            Assert.AreEqual(15.0, GoldMath.PlayerIncomePerSecond(15, 0), 1e-9); // 0 treated as 1
        }

        [Test]
        public void RefundIsHalfRoundedDown()
        {
            Assert.AreEqual(600, GoldMath.Refund(1201, 0.5));
            Assert.AreEqual(800, GoldMath.Refund(1600, 0.5));
            Assert.AreEqual(0, GoldMath.Refund(0, 0.5));
            Assert.AreEqual(0, GoldMath.Refund(1000, -1));
        }

        [Test]
        public void WholeGoldAccruesAndFractionsCarry()
        {
            var wallet = new GoldAccrual(0);
            wallet.Accrue(23.0 / 3.0, 1.0);
            Assert.AreEqual(7, wallet.Balance);
            wallet.Accrue(23.0 / 3.0, 2.0);
            Assert.AreEqual(23, wallet.Balance);
        }

        [Test]
        public void ManySmallFramesAddUpToTheSameGold()
        {
            var wallet = new GoldAccrual(0);
            for (int i = 0; i < 60; i++) wallet.Accrue(5.0, 1.0 / 60.0);
            Assert.AreEqual(5, wallet.Balance);
        }

        [Test]
        public void SpendingMoreThanYouHoldIsRefusedAndChangesNothing()
        {
            var wallet = new GoldAccrual(100);
            Assert.IsFalse(wallet.TrySpend(101));
            Assert.AreEqual(100, wallet.Balance);
            Assert.IsTrue(wallet.TrySpend(100));
            Assert.AreEqual(0, wallet.Balance);
        }

        [Test]
        public void NegativeAmountsAreIgnored()
        {
            var wallet = new GoldAccrual(50);
            Assert.IsFalse(wallet.TrySpend(-10));
            wallet.Add(-10);
            wallet.Accrue(-5, 10);
            Assert.AreEqual(50, wallet.Balance);
        }
    }
}
