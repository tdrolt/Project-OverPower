using Overpower.Combat;

namespace Overpower.Dominion
{
    /// <summary>
    /// What the Dominion shop opens in one round, bundled so the shop screen asks one object instead of passing four values around: the
    /// round the picks are for (the round coming up during a break), the weapon-depth and armour tables from DominionConfig, and the
    /// wording of a locked tier. Pure, so the tests give it literal tables.
    /// </summary>
    public sealed class DominionShopLimits
    {
        public readonly int Round;
        private readonly int[] depthByRound;
        private readonly int[] armorByRound;
        private readonly string lockedFormat;

        public DominionShopLimits(int round, int[] depthByRound, int[] armorByRound, string lockedFormat)
        {
            Round = round;
            this.depthByRound = depthByRound;
            this.armorByRound = armorByRound;
            this.lockedFormat = lockedFormat;
        }

        /// <summary>True when the round has not opened this weapon yet: it is deeper in the tree than the round allows, or the tree does not know it.
        /// What you hold and what you passed on the way are never round-locked.</summary>
        public bool IsRoundLocked(UpgradeNodeState treeState, int depth) =>
            treeState != UpgradeNodeState.Equipped && treeState != UpgradeNodeState.Owned
            && !DominionShopRules.WeaponAllowed(depth, Round, depthByRound);

        /// <summary>How a weapon node is offered in the Dominion shop. Round-locked stays Locked. Anything else the round opens is pickable
        /// straight from where you stand, even from another branch or past an upgrade step: every pick is free, so switching family or
        /// jumping to an upgrade is one click, not a sell-and-rebuy.</summary>
        public UpgradeNodeState NodeState(UpgradeNodeState treeState, int depth)
        {
            if (IsRoundLocked(treeState, depth)) return UpgradeNodeState.Locked;
            return treeState == UpgradeNodeState.Locked ? UpgradeNodeState.Selectable : treeState;
        }

        /// <summary>The text on a round-locked node ("Round 2"), or empty when no round opens it.</summary>
        public string LockedLabel(int depth) =>
            DominionShopRules.LockedLabel(lockedFormat, DominionShopRules.FirstRoundAllowingDepth(depth, depthByRound));

        /// <summary>The most armour upgrades the + buttons offer this round.</summary>
        public int ArmorCap(int shopMax) => DominionShopRules.ArmorCap(shopMax, Round, armorByRound);
    }
}
