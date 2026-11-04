using System;
using System.Collections.Generic;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>A patch of the world, in metres: its lower-left corner (smallest x and z) and its size along x and z.</summary>
    public struct MapFrame
    {
        public float MinX;
        public float MinZ;
        public float Width;
        public float Depth;

        public MapFrame(float minX, float minZ, float width, float depth)
        {
            MinX = minX;
            MinZ = minZ;
            Width = width;
            Depth = depth;
        }
    }

    /// <summary>
    /// Each map's own facts for the report: its zone names, which zones are spawns, the corner a two-team Conquest closes, and the patch of the
    /// world the report's picture shows. Pure; the picture itself is the map's saved minimap image (ArenaReportRender).
    ///
    /// The lane (DominionLaneBuilder): zone 0 is the top zone, 1 the bottom zone, 2 Orange's spawn, 3 Purple's spawn. The triangle arena's names
    /// are ArenaNames'.
    /// </summary>
    public static class ReportMaps
    {
        /// <summary>The triangle arena's frame: its capital pockets included, x and z -7 to 137.</summary>
        public static readonly MapFrame TriangleFrame = new MapFrame(-7f, -7f, 144f, 144f);

        public static string Name(ReportMap map) => map == ReportMap.Lane ? "the lane" : "the triangle arena";

        /// <summary>The minimap settings whose saved picture the report draws on.</summary>
        public static string MinimapConfigPath(ReportMap map) =>
            map == ReportMap.Lane ? DominionLaneBuilder.MinimapConfigPath : MinimapBaker.ConfigPath;

        public static int ZoneCount(ReportMap map) => map == ReportMap.Lane ? 4 : 10;

        public static string ZoneName(ReportMap map, int zone)
        {
            if (map != ReportMap.Lane) return ArenaNames.ZoneName(zone);
            switch (zone)
            {
                case 0: return "Top zone";
                case 1: return "Bottom zone";
                case 2: return ArenaNames.TeamName(0) + " spawn";
                case 3: return ArenaNames.TeamName(1) + " spawn";
                default: return "Zone " + zone;
            }
        }

        /// <summary>A team's spawn zone (the capital on the triangle arena): it scores nothing and is never fought over in Dominion.</summary>
        public static bool IsSpawn(ReportMap map, int zone) => map == ReportMap.Lane ? zone == 2 || zone == 3 : zone >= 6 && zone <= 8;

        /// <summary>The triangle arena's zones a closed corner takes out of play (PhaseTwoCutRules.CutZones on this map): the team's capital,
        /// its Tier II and both Tier IIIs next to that Tier II, ascending. Empty for anything but team 0, 1 or 2.</summary>
        public static IReadOnlyList<int> ClosedZones(int cutTeam)
        {
            switch (cutTeam)
            {
                case 0: return new[] { 0, 3, 5, 6 };
                case 1: return new[] { 1, 3, 4, 7 };
                case 2: return new[] { 2, 4, 5, 8 };
                default: return Array.Empty<int>();
            }
        }

        /// <summary>The patch the report shows: the triangle arena's fixed frame, or for the lane the whole of its saved picture (which the bake
        /// already framed to the lane plus a margin).</summary>
        public static MapFrame FrameFor(ReportMap map, MapFrame picture) => map == ReportMap.Lane ? picture : TriangleFrame;

        /// <summary>The picture's height in pixels when it is <paramref name="pixelsAcross"/> wide: the same scale on both axes.</summary>
        public static int PixelsUp(MapFrame frame, int pixelsAcross) =>
            frame.Width <= 0f ? pixelsAcross : Math.Max(1, (int)Math.Round(pixelsAcross * frame.Depth / frame.Width, MidpointRounding.AwayFromZero));

        /// <summary>Where a world point lands on a picture of the frame <paramref name="pixelsAcross"/> wide: image up is +Z, so the frame's
        /// smallest z is the bottom row.</summary>
        public static (float Px, float Py) WorldToPixel(MapFrame frame, int pixelsAcross, float x, float z)
        {
            int pixelsUp = PixelsUp(frame, pixelsAcross);
            float px = (x - frame.MinX) / frame.Width * pixelsAcross;
            float py = pixelsUp - (z - frame.MinZ) / frame.Depth * pixelsUp;
            return (px, py);
        }
    }
}
