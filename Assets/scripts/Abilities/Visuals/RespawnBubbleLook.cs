using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The Dominion respawn bubble's look: a hologram in its owner's team colour (read through the bubble row of the look table),
    /// sized from the Shield's diameter times the Dominion bubble scale so the size is tuned in one place.</summary>
    public static class RespawnBubbleLook
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int RimId = Shader.PropertyToID("_Rim");
        private static readonly int ScanDensityId = Shader.PropertyToID("_ScanDensity");
        private static readonly int ScanSpeedId = Shader.PropertyToID("_ScanSpeed");
        private static readonly int FlickerId = Shader.PropertyToID("_Flicker");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        public static float Diameter(float shieldDiameter, float bubbleScale) => shieldDiameter * bubbleScale;

        /// <summary>A player with no team (-1) gets the theme's unknown colour.</summary>
        public static Color ColourFor(int team, UiTheme theme)
        {
            LookFamily family = theme.arenaLook != null ? theme.arenaLook.RespawnBubbleLook.Family : LookFamily.Team;
            return LookColours.For(family, team, theme);
        }

        /// <summary>Copies the look's hologram numbers and the colour onto one bubble's own material. The seed keeps two bubbles from
        /// flickering in step.</summary>
        public static void Dress(Material hologram, ArenaLook look, Color colour, float seed)
        {
            hologram.SetColor(BaseColorId, colour);
            hologram.SetFloat(RimId, look.hologramRim);
            hologram.SetFloat(ScanDensityId, look.hologramScanDensity);
            hologram.SetFloat(ScanSpeedId, look.hologramScanSpeed);
            hologram.SetFloat(FlickerId, look.hologramFlicker);
            hologram.SetFloat(IntensityId, look.hologramIntensity);
            hologram.SetFloat(SeedId, seed);
        }
    }
}
