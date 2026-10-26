using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.UI;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>
    /// Structure only: every item the shop and the HUD show has an icon to draw (without one the HUD falls back to the name and the shop card
    /// shows no picture). Which picture an item gets is a look, judged on a capture, not here.
    /// </summary>
    public class ItemIconAssetTests
    {
        [Test]
        public void EveryWeaponInTheCatalogueHasAnIcon()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WeaponCatalogue>("Assets/_Project/Features/Weapons/Data/WeaponCatalogue.asset");
            Assert.NotNull(catalogue, "WeaponCatalogue moved? Update the path.");
            var missing = new List<string>();
            foreach (WeaponDefinition weapon in catalogue.Weapons)
                if (weapon != null && weapon.Icon == null)
                    missing.Add(weapon.name);
            Assert.IsEmpty(missing, "Weapons with no icon: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryShopAbilityInTheCatalogueHasAnIcon()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<AbilityCatalogue>("Assets/_Project/Shared/Config/AbilityCatalogue.asset");
            Assert.NotNull(catalogue, "AbilityCatalogue moved? Update the path.");
            var missing = new List<string>();
            foreach (AbilityDefinition ability in catalogue.Abilities)
                if (ability != null && ability.Id < 900 && ability.Icon == null) // 9xx are the F1-only debug abilities, never in the shop
                    missing.Add(ability.name);
            Assert.IsEmpty(missing, "Abilities with no icon: " + string.Join(", ", missing));
        }

        [Test]
        public void BothArmourUpgradesHaveAnIconInTheTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Project/Shared/Config/UiTheme.asset");
            Assert.NotNull(theme, "UiTheme moved? Update the path.");
            Assert.NotNull(theme.loadoutArmorAbsorbIcon, "UiTheme: Loadout Armor Absorb Icon is empty.");
            Assert.NotNull(theme.loadoutArmorRechargeIcon, "UiTheme: Loadout Armor Recharge Icon is empty.");
        }
    }
}
