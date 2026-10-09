using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class BountyLabelRulesTests
    {
        private const int Hold = 300000;
        private const int None = -1;

        [Test]
        public void AHeldZoneShowsNothingBeforeTheHoldTime()
        {
            Assert.AreEqual(0, BountyLabelRules.LabelAmount(owner: 0, heldSinceMs: 1000, lastOwner: None, lastHeldMs: 0, nowMs: 1000 + Hold - 1, holdMs: Hold, bounty: 150));
        }

        [Test]
        public void AHeldZoneShowsTheBountyFromTheHoldTimeOn()
        {
            Assert.AreEqual(150, BountyLabelRules.LabelAmount(0, 1000, None, 0, 1000 + Hold, Hold, 150));
        }

        [Test]
        public void ANeutralZoneKeepsTheLabelAfterALongHold()
        {
            Assert.AreEqual(150, BountyLabelRules.LabelAmount(owner: None, heldSinceMs: 9000, lastOwner: 0, lastHeldMs: Hold + 5, nowMs: 9500, holdMs: Hold, bounty: 150));
        }

        [Test]
        public void ANeutralZoneAfterAShortHoldShowsNothing()
        {
            Assert.AreEqual(0, BountyLabelRules.LabelAmount(None, 9000, 0, Hold - 1, 9500, Hold, 150));
        }

        [Test]
        public void ANeutralZoneWithNoHistoryShowsNothing()
        {
            Assert.AreEqual(0, BountyLabelRules.LabelAmount(None, 9000, None, 0, 9500 + Hold * 5, Hold, 150));
        }

        [Test]
        public void ABountyOfZeroShowsNothing()
        {
            Assert.AreEqual(0, BountyLabelRules.LabelAmount(0, 1000, None, 0, 1000 + Hold * 2, Hold, 0));
        }

        [Test]
        public void TheServerClockWrappingRoundDoesNotHideTheLabel()
        {
            int since = int.MaxValue - 1000;
            int now = unchecked(since + Hold + 10);
            Assert.AreEqual(150, BountyLabelRules.LabelAmount(0, since, None, 0, now, Hold, 150));
        }

        [Test]
        public void ADominionTakeoverAfterALongHoldPopsThePoints()
        {
            Assert.AreEqual(150, BountyLabelRules.PopAmount(true, newOwner: 1, lastOwner: 0, lastHeldMs: Hold, holdMs: Hold, dominionPoints: 150, paidOnLastCapture: 900));
        }

        [Test]
        public void TheOldDominionHolderRetakingItPopsNothing()
        {
            Assert.AreEqual(0, BountyLabelRules.PopAmount(true, 0, 0, Hold * 2, Hold, 150, 0));
        }

        [Test]
        public void ADominionTakeoverAfterAShortHoldPopsNothing()
        {
            Assert.AreEqual(0, BountyLabelRules.PopAmount(true, 1, 0, Hold - 1, Hold, 150, 0));
        }

        [Test]
        public void ADominionCaptureOfAZoneWithNoOldHolderPopsNothing()
        {
            Assert.AreEqual(0, BountyLabelRules.PopAmount(true, 1, None, Hold * 2, Hold, 150, 0));
        }

        [Test]
        public void TheDominionPopIsExactlyWhatTheMasterScores()
        {
            foreach (int held in new[] { 0, Hold - 1, Hold, Hold + 1, Hold * 3 })
                foreach (int lastOwner in new[] { None, 0, 1 })
                    foreach (int newOwner in new[] { None, 0, 1 })
                        Assert.AreEqual(Overpower.Dominion.DominionPointsRules.BountyPoints(held, Hold, lastOwner, newOwner, 150),
                                        BountyLabelRules.PopAmount(true, newOwner, lastOwner, held, Hold, 150, 900), "held " + held + " last " + lastOwner + " new " + newOwner);
        }

        [Test]
        public void TheConquestPopIsWhatTheCaptureWroteIntoTheSnapshot()
        {
            Assert.AreEqual(900, BountyLabelRules.PopAmount(false, 1, None, 0, Hold, 150, 900));
            Assert.AreEqual(0, BountyLabelRules.PopAmount(false, 1, None, 0, Hold, 150, 0));
        }

        [Test]
        public void DominionOffersItsPointsAndRoundedHoldTime()
        {
            BountyLabelRules.Offer offer = BountyLabelRules.OfferFor(dominion: true, paysNow: true, tierBounty: 900, tierHoldSeconds: 300f,
                                                                      dominionPoints: 150, dominionHoldSeconds: 120.0004f);
            Assert.AreEqual(150, offer.Amount);
            Assert.AreEqual(120000, offer.HoldMs);
        }

        [Test]
        public void ConquestOffersTheTierGoldAndItsTruncatedHoldTime()
        {
            BountyLabelRules.Offer offer = BountyLabelRules.OfferFor(dominion: false, paysNow: true, tierBounty: 900, tierHoldSeconds: 300.9996f,
                                                                      dominionPoints: 150, dominionHoldSeconds: 120f);
            Assert.AreEqual(900, offer.Amount);
            Assert.AreEqual(300999, offer.HoldMs);
        }

        [Test]
        public void NothingIsOfferedWhereTheBountyCannotBePaid()
        {
            Assert.AreEqual(0, BountyLabelRules.OfferFor(true, false, 900, 300f, 150, 120f).Amount);
            Assert.AreEqual(0, BountyLabelRules.OfferFor(false, false, 900, 300f, 150, 120f).Amount);
        }

        [Test]
        public void ThePopStartsSolidAtTheTowerAndEndsInvisibleHigher()
        {
            Assert.AreEqual(1f, BountyLabelRules.PopAlpha(0f, 2f), 1e-4f);
            Assert.AreEqual(0f, BountyLabelRules.PopAlpha(2f, 2f), 1e-4f);
            Assert.AreEqual(0f, BountyLabelRules.PopRise(0f, 2f, 3f), 1e-4f);
            Assert.AreEqual(3f, BountyLabelRules.PopRise(2f, 2f, 3f), 1e-4f);
        }

        [Test]
        public void ThePopOnlyFadesAndOnlyRises()
        {
            float lastAlpha = 2f, lastRise = -1f;
            for (float age = 0f; age <= 2.5f; age += 0.1f)
            {
                float alpha = BountyLabelRules.PopAlpha(age, 2f);
                float rise = BountyLabelRules.PopRise(age, 2f, 3f);
                Assert.LessOrEqual(alpha, lastAlpha);
                Assert.GreaterOrEqual(rise, lastRise);
                lastAlpha = alpha;
                lastRise = rise;
            }
        }
    }
}
