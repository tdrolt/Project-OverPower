using Overpower.Combat;
using Overpower.Data;

/// <summary>
/// The one rule every armor-upgrade button spends a purchase through (F1 test range panel and the
/// loadout screen), so the two cannot drift; the rule itself lives in ArmorUpgradePath.
/// Static and stateless: each call rebuilds an ArmorUpgradePath from the caller's PlayerHealth
/// levels and, only on success, publishes through PlayerLoadout.SetArmorLevels so every client
/// agrees. Not a PUN RPC; PlayerLoadout is the only thing that replicates a loadout change.
/// </summary>
public static class ArmorLoadoutActions
{
    /// <summary>Spends one purchase on the absorb or recharge path. Returns false and changes
    /// nothing when the combined cap or that path's top level refuses; the caller shows that.</summary>
    public static bool TryUpgrade(PlayerHealth health, PlayerLoadout loadout, ArmorConfig armorConfig, bool upgradeAbsorb, int upgradeCap = int.MaxValue)
    {
        if (health == null || loadout == null || armorConfig == null)
            return false;

        var path = new ArmorUpgradePath(armorConfig, health.AbsorbLevel, health.RechargeLevel, upgradeCap);
        bool upgraded = upgradeAbsorb ? path.TryUpgradeAbsorb() : path.TryUpgradeRecharge();
        if (!upgraded)
            return false;

        loadout.SetArmorLevels(path.AbsorbLevel, path.RechargeLevel);
        return true;
    }

    /// <summary>Both armor paths back to level 0, through PlayerLoadout so other clients see it.</summary>
    public static void Reset(PlayerLoadout loadout) => loadout?.SetArmorLevels(0, 0);
}
