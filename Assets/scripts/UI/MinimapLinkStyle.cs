namespace Overpower.UI
{
    public enum MinimapLinkKind
    {
        /// <summary>A thin grey line: nobody's way in (both ends neutral).</summary>
        Neutral,
        /// <summary>A solid line in the team's colour: the team owns both ends.</summary>
        Owned,
        /// <summary>A line with an arrowhead in the team's colour, from its zone toward the neutral one: a way in (GDD p.27).</summary>
        WayIn,
        /// <summary>A line split at its midpoint, each half in its own end's owner colour, no arrowhead: a border
        /// between two different teams' zones (controller amendment 2, 2026-09-17). A way in for both teams, so grey
        /// would hide the front line.</summary>
        Border,
    }

    /// <summary>How the minimap draws the link between two adjacent zones, from their owners only (spec 2026-09-16,
    /// "Links", amended by controller amendment 2, 2026-09-17: a border between two different teams' zones is
    /// <see cref="MinimapLinkKind.Border"/>, not the spec's thin grey line). Pure, so the rule is tested.</summary>
    public readonly struct MinimapLinkStyle
    {
        public readonly MinimapLinkKind Kind;
        /// <summary>The team whose colour the line takes; for Border, the owner of zone A. -1 for Neutral.</summary>
        public readonly int Team;
        /// <summary>For Border, the owner of zone B (its half takes this team's colour). Equal to Team for every
        /// other kind.</summary>
        public readonly int TeamB;
        /// <summary>WayIn only: true when the arrow points from zone A toward zone B.</summary>
        public readonly bool TowardB;

        public MinimapLinkStyle(MinimapLinkKind kind, int team, int teamB, bool towardB)
        {
            Kind = kind;
            Team = team;
            TeamB = teamB;
            TowardB = towardB;
        }

        /// <param name="ownerA">Owner of zone A, -1 = neutral.</param>
        /// <param name="ownerB">Owner of zone B, -1 = neutral.</param>
        public static MinimapLinkStyle For(int ownerA, int ownerB)
        {
            if (ownerA >= 0 && ownerA == ownerB)
                return new MinimapLinkStyle(MinimapLinkKind.Owned, ownerA, ownerA, false);
            if (ownerA >= 0 && ownerB < 0)
                return new MinimapLinkStyle(MinimapLinkKind.WayIn, ownerA, ownerA, true);
            if (ownerB >= 0 && ownerA < 0)
                return new MinimapLinkStyle(MinimapLinkKind.WayIn, ownerB, ownerB, false);
            if (ownerA >= 0 && ownerB >= 0)
                return new MinimapLinkStyle(MinimapLinkKind.Border, ownerA, ownerB, false);
            return new MinimapLinkStyle(MinimapLinkKind.Neutral, -1, -1, false);
        }
    }
}
