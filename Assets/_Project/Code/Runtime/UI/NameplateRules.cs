using Overpower.Arena;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>The colours of a player's plate: the name's (yours, a teammate's, an enemy's, or grey while either team is still arriving)
    /// and the edge's.</summary>
    public static class NameplateRules
    {
        /// <summary>A team below 0 is not known yet. Both teams arrive as Player Properties after the player spawns, so two missing
        /// teams must read as unknown, never as "the same team".</summary>
        public static Color NameColour(bool isMine, int myTeam, int theirTeam, Color self, Color teammate, Color enemy, Color unknown)
        {
            if (isMine)
                return self;
            if (myTeam < 0 || theirTeam < 0)
                return unknown;
            return myTeam == theirTeam ? teammate : enemy;
        }

        /// <summary>Your own plate shows no name (the scoreboard has it) and splits shield and HP into two bars like your HUD; anyone
        /// else's keeps the name over one bar, the shield drawn over HP, so nobody reads another player's HP under a shield.</summary>
        public static bool ShowsOwnBars(bool isMine) => isMine;

        /// <summary>The plate's edge: the player's own team colour, the same for every viewer (grey while their team is still arriving).</summary>
        public static Color EdgeColour(int theirTeam, UiTheme theme) => LookColours.For(LookFamily.Team, theirTeam, theme);
    }
}
