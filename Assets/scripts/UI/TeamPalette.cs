using UnityEngine;

namespace Overpower.UI
{
    /// <summary>Picks a team's entry from one of the theme's per-team colour lists. A team the list does not hold (nobody, an id past the
    /// list) gets the caller's neutral colour: clamping it onto an entry would paint it in a real team's colour.</summary>
    public static class TeamPalette
    {
        public static Color Of(Color[] colours, int team, Color unknown) =>
            colours != null && team >= 0 && team < colours.Length ? colours[team] : unknown;
    }
}
