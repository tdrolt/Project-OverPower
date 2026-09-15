using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class ShopRulesTests
    {
        [Test]
        public void AllowedInYourZoneOutOfCombatWithEnoughGold()
        {
            Assert.AreEqual(PurchaseBlock.None, ShopRules.Check(true, 5f, 5f, 1200, 1200));
        }

        [Test]
        public void TerritoryIsCheckedFirst()
        {
            Assert.AreEqual(PurchaseBlock.NotInOwnTerritory, ShopRules.Check(false, 0f, 5f, 0, 1200));
        }

        [Test]
        public void RefusedInCombat()
        {
            Assert.AreEqual(PurchaseBlock.InCombat, ShopRules.Check(true, 4.9f, 5f, 5000, 1200));
        }

        [Test]
        public void RefusedWithoutEnoughGold()
        {
            Assert.AreEqual(PurchaseBlock.CannotAfford, ShopRules.Check(true, 10f, 5f, 1199, 1200));
        }

        [Test]
        public void AFreeItemNeedsNoGold()
        {
            Assert.AreEqual(PurchaseBlock.None, ShopRules.Check(true, 10f, 5f, 0, 0));
        }

        [Test]
        public void SecondsUntilAllowedCountsDown()
        {
            Assert.AreEqual(2.5f, ShopRules.SecondsUntilOutOfCombat(2.5f, 5f), 1e-5f);
            Assert.AreEqual(0f, ShopRules.SecondsUntilOutOfCombat(9f, 5f), 1e-5f);
        }

        [Test]
        public void SellingAWeaponPathRefundsHalfOfEverythingPaidOnIt()
        {
            var ledger = new PurchaseLedger();
            ledger.RecordWeapon(1200);
            ledger.RecordWeapon(1600);
            Assert.AreEqual(1400, ledger.SellWeapon(0.5));
            Assert.AreEqual(0, ledger.SellWeapon(0.5)); // nothing left to sell
        }

        [Test]
        public void ArmorAndWeaponRefundsAreSeparate()
        {
            var ledger = new PurchaseLedger();
            ledger.RecordWeapon(1200);
            ledger.RecordArmor(1400);
            Assert.AreEqual(700, ledger.SellArmor(0.5));
            Assert.AreEqual(1200, ledger.WeaponSpent);
        }
    }
}
