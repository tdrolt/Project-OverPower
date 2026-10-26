using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class BountyRuleTests
    {
        private const int Hold = 300000;

        [Test]
        public void FourFiftyNinePaysNothing()
        {
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(newOwner: 1, lastOwner: 0, lastHeldMs: 299000, bounty: 900, holdMs: Hold));
        }

        [Test]
        public void FiveOhOnePaysTheBounty()
        {
            Assert.AreEqual(900, BountyRule.PayoutOnCapture(1, 0, 301000, 900, Hold));
        }

        [Test]
        public void ExactlyFiveMinutesPays()
        {
            Assert.AreEqual(1200, BountyRule.PayoutOnCapture(2, 0, Hold, 1200, Hold));
        }

        [Test]
        public void RetakingYourOwnZonePaysNothing()
        {
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(0, 0, 900000, 900, Hold));
        }

        [Test]
        public void AZoneNobodyHeldPaysNothing()
        {
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(1, TerritoryMap.Neutral, 900000, 900, Hold));
        }

        [Test]
        public void ATierWithoutABountyPaysNothing()
        {
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(1, 0, 900000, 0, Hold));
        }

        [Test]
        public void TheSnapshotSettlesTheHoldSoItCannotPayTwice()
        {
            var s = new TerritorySnapshot(10).WithCapture(3, 0, 0, 0).WithNeutral(3, 400000);
            int pay = BountyRule.PayoutOnCapture(1, s.LastOwnerOf(3), s.LastHeldMs(3), 900, Hold);
            var captured = s.WithCapture(3, 1, 410000, pay);
            Assert.AreEqual(900, captured.BountyPaidOnLastCapture(3));
            var again = captured.WithNeutral(3, 420000);
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(2, again.LastOwnerOf(3), again.LastHeldMs(3), 900, Hold));
        }
    }
}
