using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>What a mech wears for a team colour: a body that takes part of the colour but is never dark enough to read as black,
    /// and glow lines in the team's own hue, bright (unclamped) so the scene's Bloom gives them a thin halo.</summary>
    public static class MechLook
    {
        // The model's own albedo darkens whatever the body is tinted with, so the tint starts from a pale grey and keeps a floor.
        private static readonly Color PaleGrey = new Color(0.85f, 0.85f, 0.85f, 1f);
        private const float Floor = 0.25f;

        public static Color Base(Color team, float mix)
        {
            Color tint = Color.Lerp(PaleGrey, team, Mathf.Clamp01(mix));
            return new Color(Mathf.Max(tint.r, Floor), Mathf.Max(tint.g, Floor), Mathf.Max(tint.b, Floor), 1f);
        }

        /// <summary>The team colour (linear light) scaled so its brightest channel is the strength: a dim team colour glows as bright as
        /// a bright one, and the screen clips the same share of every hue. A black colour has no hue and gives no glow.</summary>
        public static Color Glow(Color team, float strength)
        {
            float peak = Mathf.Max(team.r, Mathf.Max(team.g, team.b));
            float scale = peak > 0f ? Mathf.Max(0f, strength) / peak : 0f;
            return new Color(team.r * scale, team.g * scale, team.b * scale, 1f);
        }
    }
}
