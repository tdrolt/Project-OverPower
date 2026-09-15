using System;

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
