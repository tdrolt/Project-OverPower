using System;
using Overpower.Combat;
using Overpower.Match;

namespace Overpower.Dominion
{
    /// <summary>
    /// What the Dominion shop opens each round, and when it takes picks. The tables come from DominionConfig and are
    /// passed in. Weapon depth counts steps up the weapon tree: the Baseline pistol (the root) is 0, a weapon family 1, a family's upgrade 2.
    /// </summary>
    public static class DominionShopRules
    {
        /// <summary>How deep in the weapon tree the shop opens in this round (1-based). A round past the table uses the last entry, a round
        /// under 1 the first; no table means the Baseline only.</summary>
        public static int MaxWeaponDepth(int round, int[] weaponDepthByRound) => ForRound(round, weaponDepthByRound);

        /// <summary>How many armour upgrades the shop opens in this round (same clamping).</summary>
        public static int ArmorUpgradesAllowed(int round, int[] armorUpgradesByRound) => ForRound(round, armorUpgradesByRound);

        private static int ForRound(int round, int[] table)
        {
            if (table == null || table.Length == 0) return 0;
            int index = Math.Min(Math.Max(round, 1), table.Length) - 1;
            return table[index];
        }

        /// <summary>The shop takes picks anywhere, in the break and at any time during a round (its play, overtime and sudden death), with
        /// the round's limits either way. Only while no round or break is on (before the first break, after the match) is it shut.</summary>
        public static bool MayPick(DominionStage stage) => stage == DominionStage.Break || LateJoinerWindowOpen(stage);

        /// <summary>NotInBreak while a live Dominion match has no round or break on, else None. Conquest and the warm-up are never blocked by this.</summary>
        public static PurchaseBlock PickBlock(bool dominionLive, DominionStage stage) =>
            dominionLive && !MayPick(stage) ? PurchaseBlock.NotInBreak : PurchaseBlock.None;

        /// <summary>A fresh joiner finds the shop opened by itself only while the round they joined in is still going (in a break the break
        /// card points everyone to it).</summary>
        public static bool LateJoinerWindowOpen(DominionStage stage) =>
            DominionRules.IsRoundPlay(stage) || stage == DominionStage.SuddenDeath;

        /// <summary>The most armour upgrades the + buttons offer: the smaller of the shop's own maximum and the round's allowance.</summary>
        public static int ArmorCap(int shopMax, int round, int[] table) => Math.Min(shopMax, ArmorUpgradesAllowed(round, table));

        /// <summary>A weapon's depth in the tree: 0 for the root, 1 for its children, and so on; -1 for a weapon the tree does not know or
        /// whose parents loop. <paramref name="parentOf"/> returns a weapon's parent id: any negative number for a root
        /// (WeaponUpgradeTree treats every negative parent as one) and null for a weapon it does not know.</summary>
        public static int WeaponDepth(int weaponId, Func<int, int?> parentOf)
        {
            const int MaxSteps = 64; // a parent chain longer than this can only be a loop
            int depth = 0, current = weaponId;
            for (int step = 0; step < MaxSteps; step++)
            {
                int? known = parentOf(current);
                if (known == null) return -1;
                int parent = known.Value;
                if (parent < 0) return depth;
                depth++;
                current = parent;
            }
            return -1;
        }

        public static bool WeaponAllowed(int weaponDepth, int round, int[] table) =>
            weaponDepth >= 0 && weaponDepth <= MaxWeaponDepth(round, table);

        /// <summary>The text on a locked shop tier ("Round 2"): the format with the round number put in. Empty when no round ever opens it.</summary>
        public static string LockedLabel(string format, int firstRoundOpen) =>
            firstRoundOpen < 1 ? "" : string.Format(format ?? "Round {0}", firstRoundOpen);

        /// <summary>The first round (1-based) whose table entry reaches this depth, or -1 if no round does (or the depth is unknown, below 0).</summary>
        public static int FirstRoundAllowingDepth(int depth, int[] table)
        {
            if (table == null || depth < 0) return -1;
            for (int i = 0; i < table.Length; i++)
                if (table[i] >= depth) return i + 1;
            return -1;
        }
    }
}
