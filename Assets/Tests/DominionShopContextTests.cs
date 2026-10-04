using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 5: the shop's gate in a Dominion match (free, break only) and Conquest's gate unchanged. Literal numbers throughout.</summary>
    public class DominionShopContextTests
    {
        private static readonly DominionShopLimits Round2 = new DominionShopLimits(2, new[] { 0, 1, 2 }, new[] { 0, 1, 2 }, "Round {0}");

        // A Conquest context: real economy (not free), 100 gold, in own territory, out of combat for 5 s of the 4 s needed.
        private static ShopContext Conquest(bool inTerritory = true, float sinceCombat = 5f, int balance = 100) =>
            new ShopContext(false, false, inTerritory, sinceCombat, 4f, balance);

        private static ShopContext Dominion(PurchaseBlock closed, int balance = 0, bool inTerritory = false, float sinceCombat = 0f) =>
            new ShopContext(true, false, inTerritory, sinceCombat, 4f, balance, closed, Round2, "Free (break)", "The shop opens in the break");

        // ---- Dominion: free regardless of gold, territory and combat

        [Test] public void ADominionBreakPickIsFreeWhateverTheGoldTerritoryAndCombat()
        {
            ShopContext ctx = Dominion(PurchaseBlock.None, balance: 0, inTerritory: false, sinceCombat: 0f);
            Assert.AreEqual(PurchaseBlock.None, ctx.Check(5000));
            Assert.IsTrue(ctx.IsDominion);
        }

        [Test] public void ADominionPickOutsideTheBreakIsRefusedEvenWithPlentyOfGold()
        {
            ShopContext ctx = Dominion(PurchaseBlock.NotInBreak, balance: 99999, inTerritory: true, sinceCombat: 99f);
            Assert.AreEqual(PurchaseBlock.NotInBreak, ctx.Check(0));
            Assert.AreEqual(PurchaseBlock.NotInBreak, ctx.Check(5000));
        }

        [Test] public void EveryDominionPriceReadsFreeAndNothingIsChargedOrNeeded()
        {
            ShopContext ctx = Dominion(PurchaseBlock.None, balance: 0);
            Assert.AreEqual("Free", ctx.PriceLine(1200));
            Assert.AreEqual("Free", ctx.PriceLine(0));
        }

        [Test] public void TheDominionHeaderSaysFreeInTheBreakAndWhyItIsClosedOtherwise()
        {
            Assert.AreEqual("Free (break)", Dominion(PurchaseBlock.None).StatusText());
            Assert.AreEqual("The shop opens in the break", Dominion(PurchaseBlock.NotInBreak).StatusText());
            Assert.AreEqual("The shop opens in the break", Dominion(PurchaseBlock.NotInBreak).ReasonText(PurchaseBlock.NotInBreak, 0));
        }

        [Test] public void ALiveDominionMatchNeverHandsOutAStartingUltimateEvenThoughItsShopIsFree()
        {
            Assert.IsFalse(ShopRules.StartingUltimateHandedOut(freeLoadout: false, matchLive: true, dominionMatch: true));
            Assert.IsFalse(ShopRules.StartingUltimateHandedOut(freeLoadout: true, matchLive: true, dominionMatch: true));
        }

        [Test] public void TheOtherFreeShopsStillHandOutTheStartingUltimateAndTheRealEconomyDoesNot()
        {
            Assert.IsTrue(ShopRules.StartingUltimateHandedOut(freeLoadout: true, matchLive: true, dominionMatch: false), "Free Loadout test mode");
            Assert.IsTrue(ShopRules.StartingUltimateHandedOut(freeLoadout: false, matchLive: false, dominionMatch: false), "the warm-up sandbox");
            Assert.IsTrue(ShopRules.StartingUltimateHandedOut(freeLoadout: false, matchLive: false, dominionMatch: true), "the Dominion warm-up sandbox");
            Assert.IsFalse(ShopRules.StartingUltimateHandedOut(freeLoadout: false, matchLive: true, dominionMatch: false), "Conquest, live");
        }

        [Test] public void TheShopIsFreeInAnyDominionMatchLiveOrNot()
        {
            Assert.IsTrue(ShopRules.IsFree(freeLoadout: false, matchLive: true, dominionMatch: true));
            Assert.IsTrue(ShopRules.IsFree(freeLoadout: false, matchLive: false, dominionMatch: true));
        }

        // ---- Conquest: unchanged

        [Test] public void ConquestStillChargesAndStillHasItsThreeGates()
        {
            Assert.IsFalse(ShopRules.IsFree(false, true), "the real economy once live");
            Assert.AreEqual(PurchaseBlock.None, Conquest().Check(100));
            Assert.AreEqual(PurchaseBlock.CannotAfford, Conquest().Check(101));
            Assert.AreEqual(PurchaseBlock.NotInOwnTerritory, Conquest(inTerritory: false).Check(10));
            Assert.AreEqual(PurchaseBlock.InCombat, Conquest(sinceCombat: 1f).Check(10));
        }

        [Test] public void ConquestShowsRealPricesAndNoBreakRule()
        {
            ShopContext ctx = Conquest();
            Assert.IsFalse(ctx.IsDominion);
            Assert.AreEqual(PurchaseBlock.None, ctx.Closed);
            Assert.AreEqual("1200", ctx.PriceLine(1200));
            Assert.AreEqual("Free", ctx.PriceLine(0), "a free first pick still reads Free");
            Assert.AreEqual("1200 · need 1100", Conquest(balance: 100).PriceLine(1200, PurchaseBlock.CannotAfford));
        }
    }
}
