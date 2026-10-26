using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Fire burns in the colour of the team that fired it: each colour of the fire keeps its own brightness (so the flames stay at
    /// or under plain colour and only their heart blooms) and turns toward the team's hue and strength of colour by <c>hueShare</c>; the
    /// heart is also paled toward white by <c>whiteness</c>, so it stays the palest, hottest part of the flame.</summary>
    public static class TeamFire
    {
        public static Color Tint(Color fire, Color team, float hueShare, float whiteness)
        {
            Color.RGBToHSV(fire, out float hue, out float strength, out float brightness);
            Color.RGBToHSV(team, out float teamHue, out float teamStrength, out _);
            float share = Mathf.Clamp01(hueShare);
            // LerpAngle goes the short way round, so orange turns toward purple through red, never through green.
            float turned = Mathf.Repeat(Mathf.LerpAngle(hue * 360f, teamHue * 360f, share), 360f) / 360f;
            // The team's strength of colour, not the ramp's: the ramp's was set against the orange sheet, which deepens it, and on the
            // grey team sheet it reads as a washed-out pastel.
            float colour = Mathf.Lerp(strength, teamStrength, share) * (1f - Mathf.Clamp01(whiteness));
            Color tinted = Color.HSVToRGB(turned, colour, brightness);
            tinted.a = fire.a;
            return tinted;
        }

        /// <summary>The ramp with every colour key tinted at its own time; its fade (the alpha keys) and blend mode are kept. A new
        /// gradient: the theme's own ramp is never changed.</summary>
        public static Gradient Ramp(Gradient ramp, Color team, float hueShare, float whiteness)
        {
            GradientColorKey[] keys = ramp.colorKeys;
            for (int i = 0; i < keys.Length; i++)
                keys[i].color = Tint(keys[i].color, team, hueShare, whiteness);
            var tinted = new Gradient { mode = ramp.mode, colorSpace = ramp.colorSpace };
            tinted.SetKeys(keys, ramp.alphaKeys);
            return tinted;
        }
    }
}
