using UnityEngine;
using Overpower.Data;

namespace Overpower.Combat
{
    /// <summary>
    /// Decides whether the next armor upgrade purchase is allowed and what it results in - a small
    /// calculator, not stored state. PlayerHealth keeps the two levels this class computes from
    /// (AbsorbLevel, RechargeLevel); a caller that wants to spend a purchase (the F1 panel today, a
    /// shop later) builds one of these from the player's CURRENT levels, calls TryUpgradeAbsorb or
    /// TryUpgradeRecharge, and - if it returned true - publishes the resulting levels through
    /// PlayerLoadout.SetArmorLevels so every client agrees.
    ///
    /// Pure logic, no MonoBehaviour and no reference to ArmorState: it only ever reads ArmorConfig,
    /// so an edit-mode test can exercise the whole upgrade rule with nothing but a ScriptableObject.
    ///
    /// The GDD says armor is "upgradeable up to three times, with a choice each time between the
    /// absorption path and the recharge path" - the two paths therefore share ONE combined purchase
    /// cap (ArmorConfig.MaxArmorUpgrades), not two separate budgets. Each path is additionally
    /// capped by its own array length in ArmorConfig, so a path cannot be upgraded past the last
    /// level configured for it even if the combined cap has room left.
    /// </summary>
    public sealed class ArmorUpgradePath
    {
        private readonly ArmorConfig config;

        public int AbsorbLevel { get; private set; }
        public int RechargeLevel { get; private set; }

        /// <summary>Purchases spent so far on either path - what ArmorConfig.MaxArmorUpgrades caps.</summary>
        public int TotalUpgrades => AbsorbLevel + RechargeLevel;

        /// <summary>How much armor the current absorb level holds.</summary>
        public float Capacity => config != null ? config.AbsorbFor(AbsorbLevel) : 0f;

        /// <summary>How long the current recharge level waits out of combat before refilling.</summary>
        public float RechargeDelaySeconds => config != null ? config.RechargeSecondsFor(RechargeLevel) : 0f;

        /// <summary>True while both the combined cap and the absorb path's own top level allow one more purchase.</summary>
        public bool CanUpgradeAbsorb =>
            config != null && TotalUpgrades < config.MaxArmorUpgrades && AbsorbLevel < config.AbsorbLevelCount - 1;

        /// <summary>True while both the combined cap and the recharge path's own top level allow one more purchase.</summary>
        public bool CanUpgradeRecharge =>
            config != null && TotalUpgrades < config.MaxArmorUpgrades && RechargeLevel < config.RechargeLevelCount - 1;

        /// <param name="config">Armor tiers asset. A null config makes every upgrade refused and
        /// every reading 0 - the same fail-safe every other Combat class uses for a missing asset.</param>
        /// <param name="absorbLevel">Starting absorb level - the caller's current one, e.g. read
        /// from a replicated Custom Property for a late joiner.</param>
        /// <param name="rechargeLevel">Starting recharge level - see absorbLevel.</param>
        public ArmorUpgradePath(ArmorConfig config, int absorbLevel = 0, int rechargeLevel = 0)
        {
            this.config = config;
            AbsorbLevel = Mathf.Max(0, absorbLevel);
            RechargeLevel = Mathf.Max(0, rechargeLevel);
        }

        /// <summary>Spends one purchase on the absorb path. Refused (returns false, nothing changes)
        /// once the combined cap or this path's own top level is reached.</summary>
        public bool TryUpgradeAbsorb()
        {
            if (!CanUpgradeAbsorb)
                return false;

            AbsorbLevel++;
            return true;
        }

        /// <summary>Spends one purchase on the recharge path - see TryUpgradeAbsorb.</summary>
        public bool TryUpgradeRecharge()
        {
            if (!CanUpgradeRecharge)
                return false;

            RechargeLevel++;
            return true;
        }

        /// <summary>Returns both paths to level 0 - backs the F1 panel's "Reset Armor" button, so a
        /// designer can re-run the upgrade sweep without restarting play mode.</summary>
        public void ResetLevels()
        {
            AbsorbLevel = 0;
            RechargeLevel = 0;
        }
    }
}
