using Overpower.Match;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>One zone as the floor sees it: its tower's centre on the ground, its live capture radius and its paint.</summary>
    public readonly struct ZoneBand
    {
        public readonly Vector2 Centre;
        public readonly float Radius;
        public readonly OwnerPaint Paint;

        public ZoneBand(Vector2 centre, float radius, OwnerPaint paint)
        {
            Centre = centre;
            Radius = radius;
            Paint = paint;
        }
    }

    /// <summary>Plain: the arena's own look. Neutral: unlit lines in the tile's colour. Owned: lines glow in it, plus the fill.</summary>
    public enum TileState : byte { Plain, Neutral, Owned }

    public readonly struct TileLook
    {
        public readonly TileState State;
        public readonly Color Colour;
        /// <summary>0..1, owned tiles only: how much of the owner's colour fills the tile's middle.</summary>
        public readonly float Fill;
        /// <summary>A tile whose centre is past the arena's outline: the boundary wall standing on it shows its owner, the floor there
        /// draws plain, so no colour spills onto the ground outside the arena.</summary>
        public readonly bool WallsOnly;

        public TileLook(TileState state, Color colour, float fill, bool wallsOnly = false)
        {
            State = state;
            Colour = colour;
            Fill = fill;
            WallsOnly = wallsOnly;
        }

        public TileLook OnWallsOnly() => new TileLook(State, Colour, Fill, true);
    }

    /// <summary>Who reads a tile's owner: nobody (far outside the arena), the walls only (just past the outline), or floor and walls.</summary>
    public enum TileReach : byte { None, WallsOnly, FloorAndWalls }

    /// <summary>Which zone a floor tile shows and how: a tile whose centre lies within a zone's live capture radius plus the band takes
    /// that zone (the nearest centre where bands overlap) and wears exactly the colour the zone's ring and tower would, so the floor never
    /// disagrees with them. The floor never pulses: tiles change when the owner flips, not while a zone is attacked or drained.</summary>
    public static class FloorTileRule
    {
        public static float TileSize(ArenaLook look) => Mathf.Max(0.5f, look.floorTileSize);

        /// <summary>How far past a zone's live radius its paint reaches, in metres.</summary>
        public static float BandMetres(ArenaLook look) => Mathf.Max(0f, look.ownerBandTiles) * TileSize(look);

        /// <summary>Out of play first (as on the ring and the minimap), then a spawn in its team's colour like its tower, then the owner shown.</summary>
        public static OwnerPaint FloorPaint(int ownerShown, bool spawn, int spawnTeam, bool outOfPlay)
        {
            if (outOfPlay)
                return new OwnerPaint(OwnerPaintBase.OutOfPlay, TerritoryMap.Neutral, false, TerritoryMap.Neutral);
            int team = spawn && spawnTeam >= 0 ? spawnTeam : ownerShown;
            return team >= 0
                ? new OwnerPaint(OwnerPaintBase.Team, team, false, TerritoryMap.Neutral)
                : new OwnerPaint(OwnerPaintBase.Neutral, TerritoryMap.Neutral, false, TerritoryMap.Neutral);
        }

        /// <summary>The index of the zone whose band holds the tile centre, the nearest centre among several; -1 for none.</summary>
        public static int ZoneFor(Vector2 tile, ZoneBand[] zones, float band)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < zones.Length; i++)
            {
                ref readonly ZoneBand z = ref zones[i];
                float reach = z.Radius + band;
                float sqr = (tile - z.Centre).sqrMagnitude;
                if (sqr > reach * reach || sqr >= bestSqr)
                    continue;
                best = i;
                bestSqr = sqr;
            }
            return best;
        }

        /// <summary>From <paramref name="inside"/> at the centre down to <paramref name="edge"/> at the band's outer edge. An edge set
        /// above the inside is held at the inside, so the fill can never grow outward.</summary>
        public static float Fill(float dist, float radius, float band, float inside, float edge)
        {
            float reach = Mathf.Max(radius + band, 1e-4f);
            float from = Mathf.Clamp01(inside);
            return Mathf.Lerp(from, Mathf.Min(from, Mathf.Clamp01(edge)), Mathf.Clamp01(dist / reach));
        }

        /// <summary>The floor shows a tile only when its centre is inside the arena's outline; a tile whose centre lies at most
        /// <paramref name="wallMargin"/> metres outside it is kept for the boundary wall standing on it, so the wall's stripes and top
        /// line keep the colour while the ground beyond stays plain. Without an outline (a scene with no arena) nothing is cut off.</summary>
        public static TileReach ReachOf(ArenaBounds bounds, Vector2 tile, float wallMargin)
        {
            if (bounds == null)
                return TileReach.FloorAndWalls;
            float inside = bounds.SignedDistance(tile);
            return inside >= 0f ? TileReach.FloorAndWalls : inside >= -wallMargin ? TileReach.WallsOnly : TileReach.None;
        }

        /// <summary>LookOf, limited to who may read the tile (ReachOf).</summary>
        public static TileLook LookAt(Vector2 tile, ZoneBand[] zones, float band, ArenaLook look, UiTheme theme, TileReach reach)
        {
            if (reach == TileReach.None)
                return new TileLook(TileState.Plain, Color.black, 0f);
            TileLook tileLook = LookOf(tile, zones, band, look, theme);
            return reach == TileReach.WallsOnly ? tileLook.OnWallsOnly() : tileLook;
        }

        /// <summary>A key of every look number and colour baked into the painted tiles, so an edit to any of them repaints at once.</summary>
        public static int PaintLookKey(ArenaLook look, UiTheme theme)
        {
            int key = 17;
            void Add(float v) => key = key * 31 + v.GetHashCode();
            void AddColour(Color c) { Add(c.r); Add(c.g); Add(c.b); }
            Add(look.floorTileSize);
            Add(look.ownerBandTiles);
            Add(look.ownerFillInside);
            Add(look.ownerFillEdge);
            AddColour(theme.arenaNeutralLineColor);
            AddColour(theme.arenaOutOfPlayLineColor);
            if (theme.teamShotColors != null)
                foreach (Color team in theme.teamShotColors)
                    AddColour(team);
            return key;
        }

        /// <summary>Out-of-play ground shows unlit lines like a neutral zone (nobody owns it) in the arena's own out-of-play line colour:
        /// the ring's and minimap's out-of-play shade is so dark it would erase the stripes on the dark walls.</summary>
        public static TileLook LookOf(Vector2 tile, ZoneBand[] zones, float band, ArenaLook look, UiTheme theme)
        {
            int zone = ZoneFor(tile, zones, band);
            if (zone < 0)
                return new TileLook(TileState.Plain, Color.black, 0f);
            ZoneBand z = zones[zone];
            Color colour = z.Paint.Base == OwnerPaintBase.OutOfPlay
                ? theme.arenaOutOfPlayLineColor
                : OwnerPaintColours.For(z.Paint, theme, theme.arenaNeutralLineColor, 0f);
            if (z.Paint.Base != OwnerPaintBase.Team)
                return new TileLook(TileState.Neutral, colour, 0f);
            float fill = Fill(Vector2.Distance(tile, z.Centre), z.Radius, band, look.ownerFillInside, look.ownerFillEdge);
            return new TileLook(TileState.Owned, colour, fill);
        }
    }
}
