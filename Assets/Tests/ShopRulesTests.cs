using NUnit.Framework;
using Overpower.Data;
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

        [Test]
        public void ClearForgetsEverythingSpent()
        {
            // 2.7b step 4: the owner-side fresh start (Decision 6) - the purchase ledger is cleared so
            // neither reset button can refund warm-up spending once the real economy starts.
            var ledger = new PurchaseLedger();
            ledger.RecordWeapon(600);
            ledger.RecordArmor(1400);

            ledger.Clear();

            Assert.AreEqual(0, ledger.SellWeapon(0.5));
            Assert.AreEqual(0, ledger.SellArmor(0.5));
        }

        [Test]
        public void FirstPickIntoAnEmptyMobilityOrEquipmentSlotIsFree()
        {
            Assert.AreEqual(0, ShopRules.AbilityPrice(slotIsEmpty: true, AbilitySlot.Mobility, goldCost: 800));
            Assert.AreEqual(0, ShopRules.AbilityPrice(slotIsEmpty: true, AbilitySlot.Equipment, goldCost: 800));
        }

        [Test]
        public void ChangingAnAlreadyFilledMobilityOrEquipmentSlotCostsItsPrice()
        {
            Assert.AreEqual(800, ShopRules.AbilityPrice(slotIsEmpty: false, AbilitySlot.Mobility, goldCost: 800));
            Assert.AreEqual(800, ShopRules.AbilityPrice(slotIsEmpty: false, AbilitySlot.Equipment, goldCost: 800));
        }

        [Test]
        public void TheUltimateSlotIsNeverFreeEvenWhenEmpty()
        {
            Assert.AreEqual(1550, ShopRules.AbilityPrice(slotIsEmpty: true, AbilitySlot.Ultimate, goldCost: 1550));
            Assert.AreEqual(1550, ShopRules.AbilityPrice(slotIsEmpty: false, AbilitySlot.Ultimate, goldCost: 1550));
        }

        [Test]
        public void TheShopIsFreeInTheWarmupOrWithFreeLoadout()
        {
            // Tudor, 2026-09-18: before the match goes live the shop is a sandbox - free, unlimited, resettable.
            Assert.IsTrue(ShopRules.IsFree(freeLoadout: false, matchLive: false), "the warm-up, countdown included");
            Assert.IsFalse(ShopRules.IsFree(false, true), "the real economy once live");
            Assert.IsTrue(ShopRules.IsFree(true, true), "Free Loadout keeps the whole match free, as before");
            Assert.IsTrue(ShopRules.IsFree(true, false));
        }

        [Test]
        public void EmptyPrimarySlotIsNotFree()
        {
            // Task 2.5b review fix 5: AbilitySlot has FOUR values (Primary, Equipment, Ultimate,
            // Mobility) - the old rule read "free unless Ultimate", which silently also freed an
            // empty Primary slot. Nothing equips Primary through this screen today
            // (LoadoutScreen.SlotHeading's own comment: "Primary never reaches here"), but the RULE
            // itself must say "free only for Mobility/Equipment" explicitly rather than relying on
            // that coincidence.
            Assert.AreEqual(800, ShopRules.AbilityPrice(slotIsEmpty: true, AbilitySlot.Primary, goldCost: 800));
        }
    }
}
