using UnityEngine;

namespace Overpower.UI
{
    /// <summary>Where one level of an armour path stands for the player: already reached, the one the + buys next, reachable later with the
    /// upgrades still left, or out of reach because the shared upgrade budget (or this Dominion round's) runs out first.</summary>
    public enum ArmorStep
    {
        Reached,
        Next,
        Later,
        OutOfReach,
    }

    /// <summary>
    /// The steps drawn along one armour path in the shop (level 0 up to the path's top level) and their colours. Both paths draw from ONE
    /// budget of upgrades, so a step this path could only reach with more upgrades than are left reads out of reach.
    /// </summary>
    public static class ArmorPathSteps
    {
        /// <param name="level">The path's current level.</param>
        /// <param name="topLevel">The path's last level (ArmorConfig's level count minus one).</param>
        /// <param name="upgradesLeft">Upgrades still allowed on either path: the limit (this round's in Dominion) minus those bought.</param>
        public static ArmorStep[] For(int level, int topLevel, int upgradesLeft)
        {
            int top = Mathf.Max(0, topLevel);
            int current = Mathf.Clamp(level, 0, top);
            int left = Mathf.Max(0, upgradesLeft);
            var steps = new ArmorStep[top + 1];
            for (int i = 0; i <= top; i++)
            {
                int needed = i - current;
                steps[i] = needed <= 0 ? ArmorStep.Reached
                    : needed > left ? ArmorStep.OutOfReach
                    : needed == 1 ? ArmorStep.Next
                    : ArmorStep.Later;
            }
            return steps;
        }

        /// <summary>The ring round a step.</summary>
        public static Color Border(ArmorStep step, UiTheme theme)
        {
            switch (step)
            {
                case ArmorStep.Reached: return theme.loadoutArmorStepReachedColor;
                case ArmorStep.Next: return theme.loadoutArmorStepNextColor;
                case ArmorStep.Later: return theme.loadoutArmorStepLaterColor;
                default: return theme.loadoutArmorStepOutOfReachColor;
            }
        }

        /// <summary>The inside of a step: solid once reached, hollow (the panel's colour) otherwise.</summary>
        public static Color Fill(ArmorStep step, UiTheme theme) =>
            step == ArmorStep.Reached ? theme.loadoutArmorStepReachedColor : theme.loadoutPanelColor;

        /// <summary>The line leading into a step from the one before it: lit once the step is reached; the next step's line stays unlit
        /// until it is bought.</summary>
        public static Color LineInto(ArmorStep step, UiTheme theme)
        {
            switch (step)
            {
                case ArmorStep.Reached: return theme.loadoutArmorStepReachedColor;
                case ArmorStep.OutOfReach: return theme.loadoutArmorStepOutOfReachColor;
                default: return theme.loadoutArmorStepLaterColor;
            }
        }
    }
}
