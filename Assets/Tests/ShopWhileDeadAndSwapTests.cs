using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Task 5b-1: the shop while dead (D4) and what a swap costs (D19).</summary>
    public class ShopWhileDeadAndSwapTests
    {
        private static WeaponUpgradeTree RealShape() => new WeaponUpgradeTree(new List<(int, int)>
        {
            (1, -1),
            (2, 1), (3, 2), (4, 2),
            (5, 1), (6, 5), (7, 5),
            (8, 1), (9, 8), (10, 8),
            (11, 1), (12, 11), (13, 11),
        });

        [Test]
        public void ADeadPlayerCountsAsInTheirOwnTerritory()
        {
            Assert.IsTrue(ShopRules.EffectiveInOwnTerritory(false, false));
        }

        [Test]
        public void ALivingPlayerTerritoryIsUnchanged()
        {
            Assert.IsFalse(ShopRules.EffectiveInOwnTerritory(true, false));
            Assert.IsTrue(ShopRules.EffectiveInOwnTerritory(true, true));
        }

        [Test]
        public void ADeadPlayerCountsAsOutOfCombat()
        {
            Assert.AreEqual(PurchaseBlock.None,
                ShopRules.Check(ShopRules.EffectiveInOwnTerritory(false, false),
                    ShopRules.EffectiveSecondsSinceCombat(false, 0.5f, 5f), 5f, 5000, 1200));
        }

        [Test]
        public void ALivingPlayerCombatTimerIsUnchanged()
        {
            Assert.AreEqual(0.5f, ShopRules.EffectiveSecondsSinceCombat(true, 0.5f, 5f));
            Assert.AreEqual(PurchaseBlock.InCombat,
                ShopRules.Check(true, ShopRules.EffectiveSecondsSinceCombat(true, 0.5f, 5f), 5f, 5000, 1200));
        }

        [Test]
        public void ADeadPlayerStillCannotBuyWhatTheyCannotPayFor()
        {
            Assert.AreEqual(PurchaseBlock.CannotAfford,
                ShopRules.Check(ShopRules.EffectiveInOwnTerritory(false, false),
                    ShopRules.EffectiveSecondsSinceCombat(false, 0f, 5f), 5f, 100, 1200));
        }

        [Test]
        public void TheSwapRefundIsExactlyWhatTheResetWouldGive()
        {
            var ledger = new PurchaseLedger();
            ledger.RecordWeapon(1200);
            ledger.RecordWeapon(2500);
            int preview = ledger.WeaponRefundPreview(0.5);
            Assert.AreEqual(ledger.SellWeapon(0.5), preview);
            Assert.Greater(preview, 0);
        }

        [Test]
        public void AnotherPathNeedsASwapButYourOwnPathDoesNot()
        {
            WeaponUpgradeTree tree = RealShape();
            Assert.IsTrue(tree.NeedsSwap(5, 2));   // sibling path
            Assert.IsTrue(tree.NeedsSwap(12, 2));  // deeper on another path
            Assert.IsFalse(tree.NeedsSwap(3, 2));  // upgrade from where you are
            Assert.IsFalse(tree.NeedsSwap(4, 2));
            Assert.IsFalse(tree.NeedsSwap(2, 2));  // what you hold
            Assert.IsFalse(tree.NeedsSwap(1, 2));  // something you already passed
            Assert.IsFalse(tree.NeedsSwap(12, 1)); // from the start everything is a plain upgrade path
        }

        [Test]
        public void TheSwapTextShowsTheRefundOnlyWhereASwapIsNeeded()
        {
            Assert.AreEqual("Swap (sell back +600)", ShopRules.SwapLine(true, 600, "Swap (sell back +{0})"));
            Assert.AreEqual("", ShopRules.SwapLine(false, 600, "Swap (sell back +{0})"));
        }

        [Test]
        public void TheSoldMessagesNameWhatWasSold()
        {
            Assert.AreEqual("Sold Laser - Through Walls: +600 gold",
                ShopRules.SoldMessage("Sold {0}: +{1} gold", "Laser - Through Walls", 600));
        }
    }
}
