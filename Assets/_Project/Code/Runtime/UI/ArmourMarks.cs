using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The armour upgrades a player bought, as one mark on their shield: a square per Absorb level, each inside the one before, a "v"
    /// per Recharge level stacked, and the "v" stack inside the innermost square when the player has both. The mark shows only while
    /// the shield holds any armour.
    /// </summary>
    public static class ArmourMarks
    {
        public struct Marks
        {
            /// <summary>1 when there is anything to draw: the squares nest and the "v"s stack, so there is never more than one.</summary>
            public int cells;
            public int squares;
            public int chevrons;
            /// <summary>The "v" stack sits inside the innermost square.</summary>
            public bool chevronsInSquare;
        }

        public static Marks Compose(int absorbLevel, int rechargeLevel)
        {
            int squares = Mathf.Max(0, absorbLevel);
            int chevrons = Mathf.Max(0, rechargeLevel);
            return new Marks
            {
                squares = squares,
                chevrons = chevrons,
                chevronsInSquare = squares > 0 && chevrons > 0,
                cells = squares > 0 || chevrons > 0 ? 1 : 0,
            };
        }

        /// <summary>The side of the Absorb square at this nesting depth (0 = the outer one).</summary>
        public static float SquareSize(float outerSize, int index, float nestedScale) => outerSize * Mathf.Pow(nestedScale, index);

        /// <summary>The mark goes with the shield: shown while it holds any armour (recharging included), gone at zero or on a corpse.</summary>
        public static bool Shown(bool barShown, int cells, float armour) => barShown && cells > 0 && armour > 0f;
    }
}
