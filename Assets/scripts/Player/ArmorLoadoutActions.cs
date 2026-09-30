using Overpower.Combat;
using Overpower.Data;

/// <summary>
/// The one rule every armor-upgrade button in the game spends a purchase through - the F1 test
/// range panel's +Absorb/+Recharge/Reset buttons and the loadout screen's own. Pulled out of
/// TestRangePanel (Task 9a) so the loadout screen calls the exact same rule instead of growing a
/// second copy that could quietly drift from the first - see ArmorUpgradePath's own class comment
/// for why the rule itself lives there and not here.
///
/// Static and stateless: every call rebuilds an ArmorUpgradePath from whatever levels the caller's
/// PlayerHealth currently holds, spends (or refuses) one purchase, and - only on success -
/// publishes the result through PlayerLoadout.SetArmorLevels so every other client agrees. Nothing
/// here is a PUN RPC; PlayerLoadout is still the only thing that ever replicates a loadout change.
/// </summary>
public static class ArmorLoadoutActions
{
    /// <summary>Spends one purchase on the absorb path (upgradeAbsorb: true) or the recharge path
    /// (false), if the combined cap and that path's own top level both still allow it. Returns
    /// false and changes nothing when refused - the caller decides how to show that (TestRangePanel
    /// logs it; LoadoutScreen disables the button before this is ever called).</summary>
    public static bool TryUpgrade(PlayerHealth health, PlayerLoadout loadout, ArmorConfig armorConfig, bool upgradeAbsorb)
    {
        if (health == null || loadout == null || armorConfig == null)
            return false;

        var path = new ArmorUpgradePath(armorConfig, health.AbsorbLevel, health.RechargeLevel);
        bool upgraded = upgradeAbsorb ? path.TryUpgradeAbsorb() : path.TryUpgradeRecharge();
        if (!upgraded)
            return false;

        loadout.SetArmorLevels(path.AbsorbLevel, path.RechargeLevel);
        return true;
    }

    /// <summary>Returns both armor paths to level 0, through PlayerLoadout so every other client
    /// sees the reset too - same as TryUpgrade above, just with nothing to refuse.</summary>
    public static void Reset(PlayerLoadout loadout) => loadout?.SetArmorLevels(0, 0);
}
