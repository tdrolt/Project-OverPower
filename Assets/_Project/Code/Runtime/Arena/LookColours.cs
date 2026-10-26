using Overpower.UI;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Turns a look's colour family into the UiTheme colour it stands for, so every effect has one colour source, and gives the
    /// caster's team accent that says whose an ability is.</summary>
    public static class LookColours
    {
        /// <summary>Fire gives its orange-red; its glowing heart is <see cref="FireCore"/>. None gives a clear colour, since a look with no
        /// family draws nothing.</summary>
        public static Color For(LookFamily family, int team, UiTheme theme)
        {
            switch (family)
            {
                case LookFamily.Team: return theme.ShotColorFor(team);
                case LookFamily.Gold: return theme.immuneBarColor;
                case LookFamily.ElectricBlue: return theme.statusStunnedColor;
                case LookFamily.Movement: return Movement(team, theme);
                case LookFamily.Fire: return theme.fireEdgeColor;
                case LookFamily.Danger: return theme.dangerColor;
                case LookFamily.White: return theme.flashWhiteColor;
                case LookFamily.Heal: return theme.healGreenColor;
                default: return Color.clear;
            }
        }

        public static Color FireCore(UiTheme theme) => theme.fireCoreColor;

        /// <summary>The movement grey with Movement Team Tint of the caster's team colour mixed in: grey says movement, the tint says whose.
        /// No team known, or no Arena Look, leaves the plain grey.</summary>
        public static Color Movement(int team, UiTheme theme)
        {
            float tint = theme.arenaLook != null ? theme.arenaLook.movementTeamTint : 0f;
            return Color.Lerp(theme.movementGreyColor, Accent(team, theme, theme.movementGreyColor), tint);
        }

        /// <summary>The colour of an ability's thin team accent (a trail, an edge or a rim, never its body): the caster's team colour, the same
        /// one its shots wear. A team the theme does not know has no accent, so the part keeps <paramref name="own"/>, the ability's own
        /// colour, rather than the grey of an unknown shot.</summary>
        public static Color Accent(int team, UiTheme theme, Color own) => TeamPalette.Of(theme.teamShotColors, team, own);

        /// <summary>The colour of an ability's row in the look table, so retuning the table recolours the effect. Without a table or a row the effect
        /// keeps the family it was built for, since hiding it would hide a rule from players.</summary>
        public static Color ForAbility(UiTheme theme, int abilityId, int team, LookFamily fallback)
        {
            LookFamily family = theme.arenaLook != null && theme.arenaLook.TryGetLook(abilityId, out AbilityLook look) ? look.Family : fallback;
            return For(family, team, theme);
        }
    }
}
