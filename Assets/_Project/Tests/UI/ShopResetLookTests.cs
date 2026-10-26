using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// Reset Weapon and Reset Armor look refused exactly when a click on them is refused (mid-round in Dominion, a shut shop, the Conquest gate),
    /// through the same look every other refused shop item uses; and a shop picture fades while its item cannot be picked.
    /// </summary>
    public class ShopResetLookTests
    {
        private static ShopContext Dominion(bool roundLocked, bool lateJoinGunPickLeft = false, PurchaseBlock closed = PurchaseBlock.None) =>
            new ShopContext(isFree: true, isWarmupSandbox: false, inOwnTerritory: true, secondsSinceCombat: 99f, requiredOutOfCombatSeconds: 3f,
                balance: 0, closed: closed, roundLocked: roundLocked, lateJoinGunPickLeft: lateJoinGunPickLeft);

        [Test]
        public void MidRoundBothResetsAreLockedUntilTheBreak()
        {
            ShopContext ctx = Dominion(roundLocked: true);
            Assert.AreEqual(PurchaseBlock.LockedUntilBreak, ctx.WeaponResetGate());
            Assert.AreEqual(PurchaseBlock.LockedUntilBreak, ctx.ArmorResetGate());
        }

        [Test]
        public void InTheBreakBothResetsAreOpen()
        {
            ShopContext ctx = Dominion(roundLocked: false);
            Assert.AreEqual(PurchaseBlock.None, ctx.WeaponResetGate());
            Assert.AreEqual(PurchaseBlock.None, ctx.ArmorResetGate());
        }

        [Test]
        public void ALateJoinerWithTheirGunPickLeftMayResetTheGunButNotTheArmour()
        {
            ShopContext ctx = Dominion(roundLocked: true, lateJoinGunPickLeft: true);
            Assert.AreEqual(PurchaseBlock.None, ctx.WeaponResetGate());
            Assert.AreEqual(PurchaseBlock.LockedUntilBreak, ctx.ArmorResetGate());
        }

        [Test]
        public void AShutShopSaysSoBeforeTheLock()
        {
            ShopContext ctx = Dominion(roundLocked: true, closed: PurchaseBlock.NotInBreak);
            Assert.AreEqual(PurchaseBlock.NotInBreak, ctx.WeaponResetGate());
            Assert.AreEqual(PurchaseBlock.NotInBreak, ctx.ArmorResetGate());
        }

        [Test]
        public void InConquestTheResetsFollowTheShopGate()
        {
            var away = new ShopContext(isFree: false, isWarmupSandbox: false, inOwnTerritory: false, secondsSinceCombat: 99f,
                requiredOutOfCombatSeconds: 3f, balance: 0);
            Assert.AreEqual(PurchaseBlock.NotInOwnTerritory, away.WeaponResetGate());
            Assert.AreEqual(PurchaseBlock.NotInOwnTerritory, away.ArmorResetGate());

            var home = new ShopContext(isFree: false, isWarmupSandbox: false, inOwnTerritory: true, secondsSinceCombat: 99f,
                requiredOutOfCombatSeconds: 3f, balance: 0);
            Assert.AreEqual(PurchaseBlock.None, home.WeaponResetGate(), "A reset has no price, so no gold is needed.");
        }

        [Test]
        public void ARefusedButtonGetsTheShopBlockedLookAndAnOpenOneTheNormalLook()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            try
            {
                theme.loadoutShopBlockedColor = new Color(0.3f, 0.2f, 0.1f, 1f);
                theme.barTrackColor = new Color(0.1f, 0.1f, 0.1f, 1f);
                theme.mutedTextColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                theme.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);

                Assert.AreEqual(theme.loadoutShopBlockedColor, ShopItemLook.ButtonFill(PurchaseBlock.LockedUntilBreak, theme));
                Assert.AreEqual(theme.mutedTextColor, ShopItemLook.ButtonText(PurchaseBlock.LockedUntilBreak, theme));
                Assert.AreEqual(theme.loadoutShopBlockedColor, ShopItemLook.ButtonFill(PurchaseBlock.InCombat, theme));
                Assert.AreEqual(theme.barTrackColor, ShopItemLook.ButtonFill(PurchaseBlock.None, theme));
                Assert.AreEqual(theme.textColor, ShopItemLook.ButtonText(PurchaseBlock.None, theme));
            }
            finally { Object.DestroyImmediate(theme); }
        }

        [Test]
        public void APictureFadesOnlyWhileItsItemCannotBePicked()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            try
            {
                theme.loadoutIconUnavailableTint = new Color(1f, 1f, 1f, 0.25f);
                Assert.AreEqual(theme.loadoutIconUnavailableTint, ShopItemLook.IconTint(true, theme));
                Assert.AreEqual(Color.white, ShopItemLook.IconTint(false, theme));
            }
            finally { Object.DestroyImmediate(theme); }
        }

        private static MethodInfo Gate(string name) => typeof(ShopContext).GetMethod(name);
        private static MethodInfo Look(string name) => typeof(ShopItemLook).GetMethod(name);

        [Test]
        public void TheResetClicksAskTheSameGateTheirLookDoes()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "OnResetWeaponClicked", Gate("WeaponResetGate")));
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "OnResetArmorClicked", Gate("ArmorResetGate")));
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "RefreshResetButtons", Gate("WeaponResetGate")));
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "RefreshResetButtons", Gate("ArmorResetGate")));
        }

        [Test]
        public void TheShopPaintsTheResetButtonsAndPicturesThroughTheSharedLook()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "RefreshResetButtons", Look("ButtonFill")));
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "RefreshResetButtons", Look("ButtonText")));
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "StyleNode", Look("IconTint")), "weapon tree");
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "RefreshAbilities", Look("IconTint")), "ability cards");
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "StyleArmorPath", Look("IconTint")), "armour paths");
        }
    }
}
