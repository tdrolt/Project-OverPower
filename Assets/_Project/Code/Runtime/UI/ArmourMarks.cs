using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The armour upgrades a player bought, as marks on their plate: a square per Absorb level side by side, a "v" per Recharge
    /// level stacked in one mark, and the "v" stack inside the first square when the player has both.
    /// </summary>
    public static class ArmourMarks
    {
        public struct Marks
        {
            /// <summary>Marks side by side: one per square, or one for a "v" stack with no square.</summary>
            public int cells;
            public int squares;
            public int chevrons;
            /// <summary>The "v" stack sits inside the first (leftmost) square.</summary>
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
                cells = squares > 0 ? squares : (chevrons > 0 ? 1 : 0),
            };
        }
    }
}
