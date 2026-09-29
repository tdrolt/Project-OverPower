using System;

namespace Overpower.Match
{
    /// <summary>Task 5b-2 (D5): the shop tooltip's timing. Hover an item and, after the delay, its description
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

    /// <summary>Task 5b-2 (D5): the armour rows' wording. The limit is ONE combined budget across the Absorb and
    /// Recharge rows (ArmorConfig.MaxArmorUpgrades, checked by ArmorUpgradePath), so both rows say the same
    /// thing: which upgrade the next click would be, or that the budget is spent.</summary>
    public static class ArmorLimitLabel
    {
        /// <param name="upgradesBought">Total bought across both rows (AbsorbLevel + RechargeLevel).</param>
        /// <param name="maxUpgrades">ArmorConfig.MaxArmorUpgrades.</param>
        public static string Text(int upgradesBought, int maxUpgrades)
        {
            if (upgradesBought >= maxUpgrades) return maxUpgrades + " of " + maxUpgrades + " (max)";
            return "Upgrade " + (Math.Max(0, upgradesBought) + 1) + " of " + maxUpgrades;
        }
    }

    /// <summary>Task 5b-2 (Tudor, 2026-09-29, question 9): you are out of combat when you die and when you
    /// respawn. Every reader of the combat clock (the shop's gate, the armour recharge delay, health regen) asks
    /// "has the clock reached my threshold?", so "out of combat" is a clock value that clears all of them.</summary>
    public static class CombatClockRule
    {
        /// <summary>The clock value that counts as out of combat for every reader: the largest of their thresholds.</summary>
        public static float OutOfCombatValue(params float[] thresholds)
        {
            float value = 0f;
            foreach (float t in thresholds) if (t > value) value = t;
            return value;
        }
    }
}
