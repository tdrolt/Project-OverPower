using Overpower.UI;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Turns a look's colour family into the UiTheme colour it stands for, so every effect has one colour source.</summary>
    public static class LookColours
    {
        /// <summary>Fire gives its pale hot yellow edge; the white-hot heart is <see cref="FireCore"/>. None gives a clear colour, since a look
        /// with no family draws nothing.</summary>
        public static Color For(LookFamily family, int team, UiTheme theme)
        {
            switch (family)
            {
                case LookFamily.Team: return theme.ShotColorFor(team);
                case LookFamily.Gold: return theme.immuneBarColor;
                case LookFamily.ElectricBlue: return theme.statusStunnedColor;
                case LookFamily.Silver: return theme.movementSilverColor;
                case LookFamily.Fire: return theme.fireEdgeColor;
                case LookFamily.Danger: return theme.dangerColor;
                case LookFamily.White: return theme.flashWhiteColor;
                default: return Color.clear;
            }
        }

        public static Color FireCore(UiTheme theme) => theme.fireCoreColor;

        /// <summary>The colour of an ability's row in the look table, so retuning the table recolours the effect. Without a table or a row the effect
        /// keeps the family it was built for, since hiding it would hide a rule from players.</summary>
        public static Color ForAbility(UiTheme theme, int abilityId, int team, LookFamily fallback)
        {
            LookFamily family = theme.arenaLook != null && theme.arenaLook.TryGetLook(abilityId, out AbilityLook look) ? look.Family : fallback;
            return For(family, team, theme);
        }
    }
}
