using System;
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>The shop tooltip's timing (D5). Hover an item and, after the delay, its description
    /// appears next to the cursor; move off and it goes at once; move to another item and the wait starts over.
    /// Pure so the timing is unit tested; LoadoutScreen feeds it the pointer enter/exit and the frame time.</summary>
    public sealed class HoverTooltipTimer
    {
        private string target;
        private float hoveredSeconds;

        /// <summary>What is hovered right now (null = nothing).</summary>
        public string Target => target;
        public float HoveredSeconds => hoveredSeconds;

        /// <summary>The pointer entered an item. A different item than before restarts the wait.</summary>
        public void Enter(string key)
        {
            if (key == target) return;
            target = key;
            hoveredSeconds = 0f;
        }

        /// <summary>The pointer left an item. Only the item currently hovered can clear it (a late exit from an
        /// item we already moved off must not hide the new one).</summary>
        public void Exit(string key)
        {
            if (key != target) return;
            target = null;
            hoveredSeconds = 0f;
        }

        public void Reset()
        {
            target = null;
            hoveredSeconds = 0f;
        }

        public void Tick(float deltaSeconds)
        {
            if (target != null) hoveredSeconds += deltaSeconds;
        }

        public bool IsShown(float delaySeconds) => target != null && hoveredSeconds >= delaySeconds;
    }

    /// <summary>The wording of the armour rows (D5). The limit is ONE combined budget across the Absorb and
    /// Recharge rows (ArmorConfig.MaxArmorUpgrades, checked by ArmorUpgradePath), so both rows say the same
    /// thing: which upgrade the next click would be, or that the budget is spent. The wording itself lives in
    /// UiTheme as format strings ({0} = number, {1} = limit).</summary>
    public static class ArmorLimitLabel
    {
        public const string DefaultUpgradeFormat = "Upgrade {0} of {1}";
        public const string DefaultMaxFormat = "{0} of {0} (max)";

        /// <param name="upgradesBought">Total bought across both rows (AbsorbLevel + RechargeLevel).</param>
        /// <param name="maxUpgrades">ArmorConfig.MaxArmorUpgrades.</param>
        /// <param name="upgradeFormat">While upgrades remain: {0} = the next upgrade's number, {1} = the limit.</param>
        /// <param name="maxFormat">At the limit: {0} = the limit.</param>
        public static string Text(int upgradesBought, int maxUpgrades, string upgradeFormat, string maxFormat)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            if (upgradesBought >= maxUpgrades) return string.Format(c, maxFormat, maxUpgrades);
            return string.Format(c, upgradeFormat, Math.Max(0, upgradesBought) + 1, maxUpgrades);
        }
    }

    /// <summary>You are out of combat when you die and when you respawn.
    /// Every reader of the combat clock (the shop's gate, the armour recharge delay, health regen) asks "has the
    /// clock reached my threshold?", so "out of combat" is a clock value that clears all of them. PlayerHealth
    /// asks this class what the clock should become; nothing else decides it.</summary>
    public static class CombatClockRule
    {
        /// <summary>The clock value that counts as out of combat for every reader: the largest of their thresholds.</summary>
        public static float OutOfCombatValue(params float[] thresholds)
        {
            float value = 0f;
            foreach (float t in thresholds) if (t > value) value = t;
            return value;
        }

        /// <summary>The clock the moment you die.</summary>
        public static float AfterDeath(float regenGate, float shopGate, float longestArmourDelay) =>
            OutOfCombatValue(regenGate, shopGate, longestArmourDelay);

        /// <summary>The clock the moment you respawn.</summary>
        public static float AfterRespawn(float regenGate, float shopGate, float longestArmourDelay) =>
            OutOfCombatValue(regenGate, shopGate, longestArmourDelay);

        /// <summary>The clock after you deal damage: back to 0 (in combat) - unless you are dead, when a late
        /// credit for a hit must not put the corpse back in combat.</summary>
        public static float AfterDealtDamage(bool isDead, float currentClock) => isDead ? currentClock : 0f;
    }

    /// <summary>One weapon node's rectangle in the tree's own space (y up).</summary>
    public readonly struct TreeNodeBox
    {
        public readonly float XMin, XMax, YMin, YMax;
        public TreeNodeBox(float xMin, float xMax, float yMin, float yMax) { XMin = xMin; XMax = xMax; YMin = yMin; YMax = yMax; }
        public float CenterX => (XMin + XMax) * 0.5f;
        public float Width => XMax - XMin;
    }

    public enum TreeArrowShape { SCurve, Straight, SideLane }

    /// <summary>Which line the shop draws from a weapon to an upgrade, decided by where the
    /// nodes really are (not by sibling order): another column = an S-curve; directly below with nothing in
    /// between = straight; below with another node in between = a curve down the gap beside the column.</summary>
    public static class TreeArrowShapeRule
    {
        public static TreeArrowShape Choose(TreeNodeBox from, TreeNodeBox to, IEnumerable<TreeNodeBox> others)
        {
            if (Math.Abs(to.CenterX - from.CenterX) >= from.Width * 0.25f)
                return TreeArrowShape.SCurve;
            foreach (TreeNodeBox other in others)
            {
                bool inColumn = other.XMin < to.CenterX && to.CenterX < other.XMax;
                bool between = other.YMax <= from.YMin + 0.01f && other.YMin >= to.YMax - 0.01f;
                if (inColumn && between) return TreeArrowShape.SideLane;
            }
            return TreeArrowShape.Straight;
        }
    }
}
