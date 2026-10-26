using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>A faint team colour laid on the dark blue-grey floor (an owned hexagon's fill, an Electric Fence's floor band) is pulled
    /// toward the floor's own blue: a faint orange reads red-brown, a faint purple bluish. The middle channel moves toward the brightest
    /// by ArenaLook's Owned Fill Hue Lift, which pushes the hue back the other way; the brightest and darkest channels stay, so a
    /// primary or a grey is unchanged. ArenaHex.hlsl's ArenaFillColour is the shader's copy.</summary>
    public static class GroundTint
    {
        /// <summary>On a linear colour, as the shader does.</summary>
        public static Color Lift(Color linear, float lift)
        {
            float hi = Mathf.Max(linear.r, Mathf.Max(linear.g, linear.b));
            float lo = Mathf.Min(linear.r, Mathf.Min(linear.g, linear.b));
            float t = Mathf.Clamp01(lift);
            return new Color(Channel(linear.r, lo, hi, t), Channel(linear.g, lo, hi, t), Channel(linear.b, lo, hi, t), linear.a);
        }

        /// <summary>On a theme colour (gamma), for a renderer that takes one.</summary>
        public static Color Faint(Color colour, float lift) => Lift(colour.linear, lift).gamma;

        private static float Channel(float value, float lo, float hi, float t) => value > lo && value < hi ? value + (hi - value) * t : value;
    }
}
