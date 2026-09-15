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
    }

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

        public static float SecondsUntilOutOfCombat(float secondsSinceCombat, float requiredOutOfCombatSeconds) =>
            Math.Max(0f, requiredOutOfCombatSeconds - secondsSinceCombat);

        /// <summary>What one ability pick actually costs. The prefab ships with the Mobility and
        /// Equipment slots EMPTY (Task 2.5a) rather than pre-loaded with a free starting pick, so
        /// the GDD's "starting kit is free" [G p.17-18] has to be read here instead: the FIRST pick
        /// into an empty Mobility or Equipment slot is free, and only a later CHANGE away from it
        /// costs the ability's own price [C, assumptions-for-tudor.md]. The Ultimate slot is never
        /// free even when empty - GDD p.18 has it bought, not started with.</summary>
        public static int AbilityPrice(bool slotIsEmpty, AbilitySlot slot, int goldCost) =>
            slotIsEmpty && slot != AbilitySlot.Ultimate ? 0 : goldCost;
    }

    /// <summary>What a player has paid per category, so "undo" can refund part of it. Local to the
    /// owner: only the resulting gold is replicated.</summary>
    public sealed class PurchaseLedger
    {
        public int WeaponSpent { get; private set; }
        public int ArmorSpent { get; private set; }

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
    }
}
