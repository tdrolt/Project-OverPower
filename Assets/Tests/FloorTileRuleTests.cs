using NUnit.Framework;
using Overpower.Arena;
using Overpower.Match;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Which floor tile shows which zone's ownership, and in what colour: a tile whose centre lies within a zone's live capture
    /// radius plus the band takes that zone (the nearest centre where bands overlap), fills more toward the zone, and wears exactly the
    /// colour the capture ring and the tower would (OwnerPaintColours.For), with neutral lines in the theme's neutral line colour.</summary>
    public class FloorTileRuleTests
    {
        private UiTheme theme;
        private ArenaLook look;

        [SetUp]
        public void SetUp()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.arenaNeutralLineColor = new Color(0.61f, 0.62f, 0.63f, 1f);
            theme.arenaOutOfPlayLineColor = new Color(0.41f, 0.43f, 0.44f, 1f);
            look = ScriptableObject.CreateInstance<ArenaLook>();
            look.ownerFillInside = 0.9f;
            look.ownerFillEdge = 0.3f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(theme);
            Object.DestroyImmediate(look);
        }

        private static OwnerPaint Team(int team) => new OwnerPaint(OwnerPaintBase.Team, team, false, -1);
        private static OwnerPaint Neutral => new OwnerPaint(OwnerPaintBase.Neutral, -1, false, -1);

        [Test]
        public void TileInsideBandTakesTheZone()
        {
            var zones = new[] { new ZoneBand(new Vector2(10f, 0f), 5f, Neutral), new ZoneBand(new Vector2(40f, 0f), 5f, Team(1)) };
            Assert.AreEqual(1, FloorTileRule.ZoneFor(new Vector2(40f, 0f), zones, 3f), "the zone's own centre");
            Assert.AreEqual(1, FloorTileRule.ZoneFor(new Vector2(47.9f, 0f), zones, 3f), "past the ring but inside the band");
            Assert.AreEqual(0, FloorTileRule.ZoneFor(new Vector2(10f, -7f), zones, 3f));
            Assert.AreEqual(TileState.Owned, FloorTileRule.LookOf(new Vector2(43f, 0f), zones, 3f, look, theme).State);
        }

        [Test]
        public void OutsideEveryBandIsPlain()
        {
            var zones = new[] { new ZoneBand(new Vector2(10f, 0f), 5f, Team(0)) };
            Assert.AreEqual(-1, FloorTileRule.ZoneFor(new Vector2(18.1f, 0f), zones, 3f));
            Assert.AreEqual(TileState.Plain, FloorTileRule.LookOf(new Vector2(18.1f, 0f), zones, 3f, look, theme).State);
            Assert.AreEqual(TileState.Plain, FloorTileRule.LookOf(Vector2.zero, new ZoneBand[0], 3f, look, theme).State, "no zones at all");
        }

        [Test]
        public void PastTheArenaOutlineOnlyTheWallsReadTheTile()
        {
            ArenaBounds square = ArenaBounds.FromPolygon(new[] { new Vector2(0f, 0f), new Vector2(0f, 20f), new Vector2(20f, 20f), new Vector2(20f, 0f) });
            Assert.AreEqual(TileReach.FloorAndWalls, FloorTileRule.ReachOf(square, new Vector2(10f, 10f), 3f));
            Assert.AreEqual(TileReach.FloorAndWalls, FloorTileRule.ReachOf(square, new Vector2(0.1f, 10f), 3f), "a tile whose centre is just inside");
            Assert.AreEqual(TileReach.WallsOnly, FloorTileRule.ReachOf(square, new Vector2(-0.1f, 10f), 3f),
                "a centre past the outline: the boundary wall on it keeps the colour, the ground beyond it stays plain");
            Assert.AreEqual(TileReach.WallsOnly, FloorTileRule.ReachOf(square, new Vector2(-2.9f, 10f), 3f));
            Assert.AreEqual(TileReach.None, FloorTileRule.ReachOf(square, new Vector2(-3.1f, 10f), 3f), "past the wall's tiles nothing reads it");
            Assert.AreEqual(TileReach.FloorAndWalls, FloorTileRule.ReachOf(null, new Vector2(-300f, 10f), 3f), "no arena outline known: nothing is cut off");
        }

        [Test]
        public void WallsOnlyTileKeepsTheZoneForTheWalls()
        {
            var zones = new[] { new ZoneBand(Vector2.zero, 6f, Team(1)) };
            TileLook all = FloorTileRule.LookOf(Vector2.zero, zones, 3f, look, theme);
            Assert.IsFalse(all.WallsOnly);
            Assert.AreEqual(all, FloorTileRule.LookAt(Vector2.zero, zones, 3f, look, theme, TileReach.FloorAndWalls));
            TileLook walls = FloorTileRule.LookAt(Vector2.zero, zones, 3f, look, theme, TileReach.WallsOnly);
            Assert.IsTrue(walls.WallsOnly, "only the walls read it");
            Assert.AreEqual(all.State, walls.State);
            Assert.AreEqual(all.Colour, walls.Colour);
            Assert.AreEqual(TileState.Plain, FloorTileRule.LookAt(Vector2.zero, zones, 3f, look, theme, TileReach.None).State);
        }

        [Test]
        public void EveryPaintInputChangesTheLookKey()
        {
            int last = FloorTileRule.PaintLookKey(look, theme);
            void Changes(string what, System.Action change)
            {
                change();
                int key = FloorTileRule.PaintLookKey(look, theme);
                Assert.AreNotEqual(last, key, what);
                last = key;
            }
            Changes("the tile size", () => look.floorTileSize = 2.5f);
            Changes("the band", () => look.ownerBandTiles = 2f);
            Changes("the fill at the zone", () => look.ownerFillInside = 0.5f);
            Changes("the fill at the band's edge", () => look.ownerFillEdge = 0.1f);
            Changes("the neutral line colour", () => theme.arenaNeutralLineColor = new Color(0.5f, 0.2f, 0.2f, 1f));
            Changes("the out-of-play line colour", () => theme.arenaOutOfPlayLineColor = new Color(0.2f, 0.5f, 0.2f, 1f));
            Changes("a team's colour", () => theme.teamShotColors[1] = new Color(0.2f, 0.2f, 0.5f, 1f));
            Assert.AreEqual(last, FloorTileRule.PaintLookKey(look, theme), "nothing changed: the same key");
        }

        [Test]
        public void OverlapGoesToNearestCentre()
        {
            // The big zone is listed first and reaches further; the tile is still nearer the small one's centre.
            var zones = new[] { new ZoneBand(new Vector2(0f, 0f), 10f, Team(0)), new ZoneBand(new Vector2(14f, 0f), 5f, Team(2)) };
            Assert.AreEqual(1, FloorTileRule.ZoneFor(new Vector2(7.5f, 0f), zones, 3f));
            Assert.AreEqual(0, FloorTileRule.ZoneFor(new Vector2(6.5f, 0f), zones, 3f));
        }

        [Test]
        public void BandFollowsTheLiveRadius()
        {
            var tile = new Vector2(10f, 0f);
            Assert.AreEqual(-1, FloorTileRule.ZoneFor(tile, new[] { new ZoneBand(Vector2.zero, 5.4f, Team(0)) }, 3f), "the cut centre's smaller circle");
            Assert.AreEqual(0, FloorTileRule.ZoneFor(tile, new[] { new ZoneBand(Vector2.zero, 8f, Team(0)) }, 3f), "its full circle");
            Assert.AreEqual(-1, FloorTileRule.ZoneFor(tile, new[] { new ZoneBand(Vector2.zero, 8f, Team(0)) }, 1.5f), "a narrower band");
        }

        [Test]
        public void FillNeverGrowsOutward()
        {
            foreach ((float inside, float edge) in new[] { (0.9f, 0.3f), (1f, 0f), (0.2f, 0.8f), (0.5f, 0.5f) })
            {
                float last = float.MaxValue;
                for (float d = 0f; d <= 14f; d += 0.25f)
                {
                    float fill = FloorTileRule.Fill(d, 8f, 3f, inside, edge);
                    Assert.LessOrEqual(fill, last + 1e-6f, $"fill rose at {d} m with inside {inside}, edge {edge}");
                    Assert.That(fill, Is.InRange(0f, 1f));
                    last = fill;
                }
            }
            Assert.Greater(FloorTileRule.Fill(0f, 8f, 3f, 0.9f, 0.3f), FloorTileRule.Fill(10f, 8f, 3f, 0.9f, 0.3f), "it fades toward the band's edge");
        }

        [Test]
        public void OutOfPlayUsesOutOfPlayPaint()
        {
            Assert.AreEqual(OwnerPaintBase.OutOfPlay, FloorTileRule.FloorPaint(1, false, -1, true).Base);
            Assert.AreEqual(OwnerPaintBase.OutOfPlay, FloorTileRule.FloorPaint(-1, true, 2, true).Base, "out of play beats the spawn");
            var zones = new[] { new ZoneBand(Vector2.zero, 5f, FloorTileRule.FloorPaint(1, false, -1, true)) };
            TileLook tile = FloorTileRule.LookOf(Vector2.zero, zones, 3f, look, theme);
            Assert.AreEqual(TileState.Neutral, tile.State, "unlit lines, no fill: nobody owns it");
            Assert.AreEqual(theme.arenaOutOfPlayLineColor, tile.Colour,
                "the arena's own out-of-play lines: the minimap's dark shade would erase the stripes on the dark walls");
        }

        [Test]
        public void SpawnZoneTakesItsTeamColour()
        {
            OwnerPaint spawn = FloorTileRule.FloorPaint(-1, true, 2, false);
            Assert.AreEqual(OwnerPaintBase.Team, spawn.Base);
            Assert.AreEqual(2, spawn.Team);
            Assert.AreEqual(1, FloorTileRule.FloorPaint(0, true, 1, false).Team, "the spawn team wins over the owner shown");
            Assert.AreEqual(0, FloorTileRule.FloorPaint(0, false, 1, false).Team, "not a spawn: the owner shown");
            Assert.AreEqual(OwnerPaintBase.Neutral, FloorTileRule.FloorPaint(-1, false, -1, false).Base);
        }

        [Test]
        public void FloorPaintNeverPulses()
        {
            foreach (int owner in new[] { -1, 0, 1, 2 })
                foreach (bool spawn in new[] { false, true })
                    foreach (bool outOfPlay in new[] { false, true })
                    {
                        OwnerPaint paint = FloorTileRule.FloorPaint(owner, spawn, 1, outOfPlay);
                        Assert.IsFalse(paint.PulsesToWarning);
                        Assert.AreEqual(-1, paint.PulseTeam);
                    }
        }

        [Test]
        public void TileColourEqualsOwnerPaintColoursFor()
        {
            foreach (OwnerPaint paint in new[] { Team(0), Team(1), Team(2), Neutral })
            {
                var zones = new[] { new ZoneBand(new Vector2(3f, 4f), 6f, paint) };
                TileLook tile = FloorTileRule.LookOf(new Vector2(4f, 5f), zones, 3f, look, theme);
                Assert.AreEqual(OwnerPaintColours.For(paint, theme, theme.arenaNeutralLineColor, 0f), tile.Colour, $"{paint.Base} {paint.Team}");
            }
            Assert.AreEqual(theme.arenaNeutralLineColor, FloorTileRule.LookOf(Vector2.zero, new[] { new ZoneBand(Vector2.zero, 6f, Neutral) }, 3f, look, theme).Colour);
            TileLook owned = FloorTileRule.LookOf(Vector2.zero, new[] { new ZoneBand(Vector2.zero, 6f, Team(1)) }, 3f, look, theme);
            Assert.AreEqual(FloorTileRule.Fill(0f, 6f, 3f, look.ownerFillInside, look.ownerFillEdge), owned.Fill, "the fill comes from the look's numbers");
            Assert.AreEqual(0f, FloorTileRule.LookOf(Vector2.zero, new[] { new ZoneBand(Vector2.zero, 6f, Neutral) }, 3f, look, theme).Fill, "neutral: lines only");
        }
    }
}
