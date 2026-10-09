namespace Overpower.EditorTools.Telemetry
{
    /// <summary>One pure mapping from a team or zone id to the name Tudor reads by (a colour, or a colour-relative zone
    /// description), used everywhere the report shows one: tables, chart labels, legends, CSVs (as an added name column, never
    /// replacing the id), the bug cards' "Where:" line, the capture list. No IO, no live scene lookup: the arena (one map) was read
    /// once from BuildingManager.TowerDictionary/CathedralBuildingIDs and is hard-coded here so the report builds OFFLINE from a log.
    ///
    /// Zone 6/7/8 are the White/Purple/Cyan capitals (Tier I); 0/1/2 are each team's own Tier II (adjacent to its capital); 3/4/5 are
    /// the Tier III zones, each between exactly two teams' Tier IIs (3: White-Purple, 4: Purple-Cyan, 5: White-Cyan); 9 is the
    /// centre (Tier IV).</summary>
    public static class ArenaNames
    {
        /// <summary>Team 0 = White, 1 = Purple, 2 = Cyan - UiTheme.teamShotColors' own comment names
        /// the same three colours. Any other id (a malformed log, or TerritoryMap.Neutral = -1) reads
        /// as a plain "Team N" rather than a made-up colour.</summary>
        public static string TeamName(int teamId) => teamId switch
        {
            0 => "White",
            1 => "Purple",
            2 => "Cyan",
            _ => "Team " + teamId,
        };

        /// <summary>Every zone id 0-9 gets a unique, non-empty name - the three capitals read
        /// "&lt;colour&gt; base (T1)"; a Tier III between two teams names both, in team-id order,
        /// joined by an en dash. An id outside the real arena (a malformed log) falls back to a plain
        /// "Zone N" rather than throwing.</summary>
        public static string ZoneName(int zoneId) => zoneId switch
        {
            6 => TeamName(0) + " base (T1)",
            7 => TeamName(1) + " base (T1)",
            8 => TeamName(2) + " base (T1)",
            0 => TeamName(0) + " T2",
            1 => TeamName(1) + " T2",
            2 => TeamName(2) + " T2",
            3 => TeamName(0) + "–" + TeamName(1) + " T3",
            4 => TeamName(1) + "–" + TeamName(2) + " T3",
            5 => TeamName(0) + "–" + TeamName(2) + " T3",
            9 => "Centre (T4)",
            _ => "Zone " + zoneId,
        };
    }
}
