using UnityEngine;
using Overpower.Match;

namespace Overpower.UI
{
    /// <summary>
    /// The shop's "can this be picked right now" look, shared by every item and button: a refused one gets Loadout Shop Blocked Colour and muted
    /// text, and its picture fades (Loadout Icon Unavailable Tint). The colours are UiTheme's; this only says which one applies.
    /// </summary>
    public static class ShopItemLook
    {
        /// <summary>Fill of a labelled shop button (Reset Weapon, Reset Armor) under the gate's answer for it.</summary>
        public static Color ButtonFill(PurchaseBlock block, UiTheme theme) =>
            block != PurchaseBlock.None ? theme.loadoutShopBlockedColor : theme.barTrackColor;

        /// <summary>Text colour of a labelled shop button under the gate's answer for it.</summary>
        public static Color ButtonText(PurchaseBlock block, UiTheme theme) =>
            block != PurchaseBlock.None ? theme.mutedTextColor : theme.textColor;

        /// <summary>Tint on an item's picture: as drawn while it can be picked, faded while it cannot.</summary>
        public static Color IconTint(bool unavailable, UiTheme theme) => unavailable ? theme.loadoutIconUnavailableTint : Color.white;
    }
}
