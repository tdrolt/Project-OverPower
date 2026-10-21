using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The Shield ultimate's colour and armed-ring shape. The colour is the Shield row of the look table (Gold, the immune bars'
    /// colour, the only gold in the game); without a table the Shield still draws gold, since hiding it would hide a rule from players.</summary>
    public static class ShieldLook
    {
        private const int RingSides = 6;

        public static Color Colour(UiTheme theme, int abilityId, int team) => LookColours.ForAbility(theme, abilityId, team, LookFamily.Gold);

        /// <summary>How wide the gold shell is drawn, in metres: the Shield's diameter times Shield Shell Scale. The Shield Diameter itself
        /// stays the size the respawn bubble is measured from.</summary>
        public static float ShellDiameter(float shieldDiameter, float shellScale) => shieldDiameter * shellScale;

        /// <summary>The armed ring (metres, measured from the player's feet) in the shell's own space, where the whole shell is one unit
        /// wide and centred <paramref name="lift"/> metres above the feet: x is the ring's radius, y its height.</summary>
        public static Vector2 RingInShellSpace(float ringDiameter, float ringHeight, float shieldDiameter, float lift) =>
            new Vector2(ringDiameter * 0.5f / shieldDiameter, (ringHeight - lift) / shieldDiameter);

        /// <summary>The armed ring as a flat hexagon, corners on <paramref name="radius"/>.</summary>
        public static void DrawRing(LineRenderer line, float radius) => VisualTint.FillFlatCircle(line, radius, RingSides);
    }
}
