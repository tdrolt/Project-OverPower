using System;
using Overpower.Data;

namespace Overpower.Match
{
    public enum PurchaseBlock
    {
        None,
        /// <summary>GDD p.18-19: shopping happens in territory your team holds.</summary>
        NotInOwnTerritory,
        /// <summary>GDD: out of combat for the shop's required seconds.</summary>
        InCombat,
        CannotAfford,
        /// <summary>Dominion: picks are only possible in the break (plus a late joiner's one pick).</summary>
        NotInBreak,
    }

    /// <summary>Which shop section a purchase/refund/refusal belongs to. LoadoutScreen's Purchased/Refunded/PurchaseRefused
    /// events carry it so PlayerTelemetry's `purchase`/`refund`/`shopBlocked` lines write the spec's plain "weapon" / "armor" /
    /// "attachment" / "mobility" / "ultimate" string without the two inventing their own naming.</summary>
    public enum PurchaseCategory { Weapon, Armor, Attachment, Mobility, Ultimate }

    /// <summary>The shop's purchase rules, apart from any UI, so the screen and any future shop ask the
    /// same questions in the same order (the first failing reason is the one a player needs to fix).</summary>
    public static class ShopRules
    {
        public static PurchaseBlock Check(bool inOwnTerritory, float secondsSinceCombat,
                                          float requiredOutOfCombatSeconds, int balance, int price)
        {
            if (!inOwnTerritory) return PurchaseBlock.NotInOwnTerritory;
            if (secondsSinceCombat < requiredOutOfCombatSeconds) return PurchaseBlock.InCombat;
            if (price > 0 && balance < price) return PurchaseBlock.CannotAfford;
            return PurchaseBlock.None;
        }

        /// <summary>A player waiting to respawn will respawn at home, so the "in your own territory" gate counts as
        /// passed while dead (D4). Living players are asked about where they really are.</summary>
        public static bool EffectiveInOwnTerritory(bool isAlive, bool inOwnTerritory) => !isAlive || inOwnTerritory;

        /// <summary>The text on a weapon node that can only be reached by selling the current
        /// weapon path first (D19): the new weapon's price and what selling back gives. Empty when there is nothing to
        /// sell back (the node then keeps its normal price line). format: {0} = price, {1} = refund.</summary>
        public static string SwapLine(int price, int refund, string format) =>
            refund > 0 ? string.Format(System.Globalization.CultureInfo.InvariantCulture, format, price, refund) : "";

        /// <summary>The message after selling armour upgrades. format has {0} = the refund.</summary>
        public static string SoldArmorMessage(string format, int refund) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, format, refund);

        /// <summary>The message after selling a weapon. format has {0} = what was sold, {1} = the refund.</summary>
        public static string SoldMessage(string format, string what, int refund) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, format, what, refund);

        public static float SecondsUntilOutOfCombat(float secondsSinceCombat, float requiredOutOfCombatSeconds) =>
            Math.Max(0f, requiredOutOfCombatSeconds - secondsSinceCombat);

        /// <summary>The ONE place "is the shop free right now?" is decided. The warm-up (countdown included) is a
        /// sandbox so testers can experiment - buy anything, reset for free, with no gate - and going live empties
        /// every loadout (PlayerLifecycle.ResetForMatchStart), so nothing bought free in the warm-up survives into
        /// the real match. Free Loadout keeps the whole match free, live or not. A Dominion match is free too (there
        /// is no gold in it); when it may be shopped in is a separate question (DominionShopRules.PickBlock).</summary>
        public static bool IsFree(bool freeLoadout, bool matchLive, bool dominionMatch = false) => dominionMatch || freeLoadout || !matchLive;

        /// <summary>Whether a reset hands out the prefab's starting ultimate: only while the shop is free, and never in a live Dominion match
        /// (the shop is free there too, but the ultimate is picked in the break like everything else).</summary>
        public static bool StartingUltimateHandedOut(bool freeLoadout, bool matchLive, bool dominionMatch) =>
            !(dominionMatch && matchLive) && IsFree(freeLoadout, matchLive, dominionMatch);

        /// <summary>What one ability pick actually costs. The prefab ships with the Mobility and
        /// Attachment slots EMPTY rather than pre-loaded with a free starting pick, so the GDD's
        /// "starting kit is free" [G p.17-18] has to be read here instead: the FIRST pick into an
        /// empty Mobility or Attachment slot is free, and only a later CHANGE away from it costs the
        /// ability's own price [C, assumptions-for-tudor.md]. Named explicitly rather than "free unless
        /// Ultimate": AbilitySlot has a fourth value, Primary, which that check would silently free;
        /// Primary is never offered through this screen, but the rule should not depend on that.</summary>
        public static int AbilityPrice(bool slotIsEmpty, AbilitySlot slot, int goldCost) =>
            slotIsEmpty && (slot == AbilitySlot.Mobility || slot == AbilitySlot.Attachment) ? 0 : goldCost;
    }

    /// <summary>What a player has paid per category, so "undo" can refund part of it. Local to the
    /// owner: only the resulting gold is replicated.</summary>
    public sealed class PurchaseLedger
    {
        public int WeaponSpent { get; private set; }
        public int ArmorSpent { get; private set; }

        /// <summary>What SellWeapon would give right now, without selling - the one number the Reset button's
        /// preview and every swap node show.</summary>
        public int WeaponRefundPreview(double refundRate) => GoldMath.Refund(WeaponSpent, refundRate);

        public void RecordWeapon(int price) { if (price > 0) WeaponSpent += price; }
        public void RecordArmor(int price) { if (price > 0) ArmorSpent += price; }

        public int SellWeapon(double refundRate)
        {
            int refund = GoldMath.Refund(WeaponSpent, refundRate);
            WeaponSpent = 0;
            return refund;
        }

        public int SellArmor(double refundRate)
        {
            int refund = GoldMath.Refund(ArmorSpent, refundRate);
            ArmorSpent = 0;
            return refund;
        }

        /// <summary>Forgets everything spent this match, so neither reset button can refund warm-up
        /// spending once the real economy starts (Decision 6).</summary>
        public void Clear()
        {
            WeaponSpent = 0;
            ArmorSpent = 0;
        }
    }
}
