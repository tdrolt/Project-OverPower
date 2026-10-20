using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The glowing edge band of a placed cover wall, handed to the arena wall shader as one vector: the owner's colour in linear
    /// (the shader adds it as light) times the glow, and the band's width in metres. A zero vector draws the panel as a plain wall.</summary>
    public static class CoverPanelLook
    {
        public const string PropertyName = "_ArenaPanelEdge";

        public static Vector4 Edge(Color team, float glow, float width)
        {
            if (width <= 0f)
                return Vector4.zero;
            Color linear = team.linear;
            return new Vector4(linear.r * glow, linear.g * glow, linear.b * glow, width);
        }
    }
}
