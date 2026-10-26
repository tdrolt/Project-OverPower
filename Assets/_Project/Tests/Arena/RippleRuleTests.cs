using NUnit.Framework;
using Overpower.Arena;
using Overpower.Match;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The ring of brightened floor tiles that runs out from a tower when the owner this client's floor shows flips: which
    /// changes start one (never a first paint, a late join or a room's lobby), how the ring runs and ends, which of the few shader
    /// slots it takes, and what reaches the shader. Literal numbers, never the real asset's tuning.</summary>
    public class RippleRuleTests
    {
        private UiTheme theme;

        [SetUp]
        public void SetUp()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.teamShotColors = new[] { new Color(0.9f, 0.9f, 0.95f, 1f), new Color(0.6f, 0.2f, 1f, 1f), new Color(0.1f, 0.9f, 1f, 1f) };
            theme.arenaNeutralLineColor = new Color(0.5f, 0.52f, 0.56f, 1f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(theme);

        private static ZoneState Zone(int owner, bool registered = true, bool spawn = false, bool outOfPlay = false, float radius = 8f) =>
            new ZoneState { OwnerShown = owner, Registered = registered, Spawn = spawn, OutOfPlay = outOfPlay, Radius = radius, Centre = new Vector2(10f, -4f) };

        private static RippleNumbers Numbers(float band, float seconds, float glow = 1f, float ringWidth = 3f) =>
            new RippleNumbers { Band = band, Seconds = seconds, Glow = glow, RingWidth = ringWidth };

        // The painter hands over these and nothing else: the band is the floor's own (Owner Band Tiles x Floor Tile Size, as PaintBands paints it).
        [Test]
        public void TheLookNumbersAreHandedOverAsSet()
        {
            var look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                look.floorTileSize = 2.5f;
                look.ownerBandTiles = 2f;
                look.rippleSeconds = 0.7f;
                look.rippleGlow = 1.3f;
                look.rippleRingWidth = 2.2f;
                RippleNumbers n = RippleRule.NumbersOf(look);
                Assert.AreEqual(5f, n.Band, 1e-5f, "the band reaches as far as the floor paints it");
                Assert.AreEqual(5f, FloorTileRule.BandMetres(look), 1e-5f, "the floor's own band");
                Assert.AreEqual(0.7f, n.Seconds, "Ripple Seconds");
                Assert.AreEqual(1.3f, n.Glow, "Ripple Glow");
                Assert.AreEqual(2.2f, n.RingWidth, "Ripple Ring Width");
            }
            finally { Object.DestroyImmediate(look); }
        }

        [Test]
        public void AShownOwnerFlipStartsARipple()
        {
            Assert.IsTrue(RippleRule.Starts(Zone(-1), Zone(1)), "neutral taken by team 1");
            Assert.IsTrue(RippleRule.Starts(Zone(0), Zone(2)), "team 0 lost it to team 2");
        }

        [Test]
        public void NoRippleWhenAZoneTurnsNeutral()
        {
            Assert.IsFalse(RippleRule.Starts(Zone(1), Zone(-1)),
                "drained back to neutral: neutral ground never glows (lit vs. unlit tells it from owned ground)");
            Assert.IsFalse(RippleRule.Starts(Zone(0), Zone(-1)), "nor for team 0's own zone going neutral");
        }

        // The live start and every Dominion break and round start reset the zones in one go: owned zones go neutral, the capitals
        // (spawn areas in Dominion) go home. None of that is a flip worth a ripple.
        [Test]
        public void AZoneResetStartsNoRipple()
        {
            var slots = new Ripple[RippleRule.Slots];
            ZoneState[] painted = { Zone(0), Zone(1), Zone(2), Zone(1), Zone(0), Zone(2), Zone(0, spawn: true) };
            ZoneState[] now = { Zone(-1), Zone(-1), Zone(-1), Zone(-1), Zone(-1), Zone(-1), Zone(1, spawn: true) };
            Assert.AreEqual(0, RippleRule.StartFlips(painted, now, now.Length, Numbers(2f, 0.5f), theme, slots, 5f), "a reset starts nothing");
            foreach (Ripple r in slots)
                Assert.IsFalse(r.Running);
        }

        [Test]
        public void NoRippleOnAFirstPaint()
        {
            Assert.IsFalse(RippleRule.Starts(Zone(-1, registered: false), Zone(1)),
                "never painted (a late joiner, a fresh room, after leaving the lobby): the first paint just shows the owners");
            Assert.IsFalse(RippleRule.Starts(default, Zone(0)), "a slot after Forget");
            Assert.IsFalse(RippleRule.Starts(Zone(-1), Zone(1, registered: false)), "a zone whose tower is gone");
        }

        [Test]
        public void NoRippleWithoutAShownOwnerChange()
        {
            Assert.IsFalse(RippleRule.Starts(Zone(1), Zone(1)), "nothing changed");
            Assert.IsFalse(RippleRule.Starts(Zone(1, radius: 8f), Zone(1, radius: 5.4f)), "a cut corner shrinks the ring, the owner stays");
            Assert.IsFalse(RippleRule.Starts(Zone(1), Zone(1, outOfPlay: true)), "out of play is not a flip");
        }

        [Test]
        public void NoRippleWhereTheFloorShowsNoOwner()
        {
            Assert.IsFalse(RippleRule.Starts(Zone(0), Zone(1, outOfPlay: true)), "out-of-play ground keeps its own paint whoever owns it");
            Assert.IsFalse(RippleRule.Starts(Zone(0, spawn: true), Zone(1, spawn: true)), "a spawn area keeps its team's colour");
        }

        [Test]
        public void TheRippleIsTheColourTheFloorNowShows()
        {
            var look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                for (int owner = 0; owner <= 2; owner++)
                {
                    var zones = new[] { new ZoneBand(new Vector2(10f, -4f), 8f, FloorTileRule.FloorPaint(owner, false, -1, false)) };
                    TileLook tile = FloorTileRule.LookOf(new Vector2(12f, -4f), zones, 2f, look, theme);
                    Assert.AreEqual(TileState.Owned, tile.State, "owner " + owner + " paints the tile as owned");
                    Assert.AreEqual(tile.Colour, RippleRule.ColourOf(owner, theme), "owner " + owner);
                }
            }
            finally { Object.DestroyImmediate(look); }
        }

        [Test]
        public void TheRingRunsFromTheTowerToTheBandEdge()
        {
            Assert.AreEqual(0f, RippleRule.Radius(0f, 0.5f, 12f), 1e-5f, "starts at the tower");
            Assert.AreEqual(12f, RippleRule.Radius(0.5f, 0.5f, 12f), 1e-5f, "ends at the edge of the zone's painted band");
            Assert.AreEqual(12f, RippleRule.Radius(0.9f, 0.5f, 12f), 1e-5f, "and stays there");
            float last = -1f;
            for (float t = 0f; t <= 0.5f; t += 0.02f)
            {
                float r = RippleRule.Radius(t, 0.5f, 12f);
                Assert.GreaterOrEqual(r, last, "never runs back, at " + t);
                last = r;
            }
        }

        [Test]
        public void TheFloorRestsAsPaintedOnceTheRippleIsOver()
        {
            Assert.Greater(RippleRule.Strength(0f, 0.5f), 0.9f, "bright as it starts");
            Assert.Greater(RippleRule.Strength(0.25f, 0.5f), 0f, "still running halfway");
            Assert.AreEqual(0f, RippleRule.Strength(0.5f, 0.5f), "gone at the end");
            Assert.AreEqual(0f, RippleRule.Strength(3f, 0.5f), "and after it");
            Assert.AreEqual(0f, RippleRule.Strength(-0.1f, 0.5f), "nothing before it began");
            Assert.AreEqual(0f, RippleRule.Strength(0f, 0f), "no time set: no ripple");
            Assert.LessOrEqual(RippleRule.Strength(0.4f, 0.5f), RippleRule.Strength(0.2f, 0.5f), "fades, never flares up again");
        }

        [Test]
        public void AFreeSlotIsUsedBeforeTheOldestIsReplaced()
        {
            var slots = new Ripple[RippleRule.Slots];
            Assert.AreEqual(4, RippleRule.Slots, "a few at once");
            slots[0] = new Ripple { Start = 10f, Running = true };
            slots[1] = new Ripple { Start = 10.1f, Running = true };
            Assert.AreEqual(2, RippleRule.Slot(slots, 10.2f, 0.5f), "the first slot nobody uses");
            slots[2] = new Ripple { Start = 10.2f, Running = true };
            slots[3] = new Ripple { Start = 10.3f, Running = true };
            Assert.AreEqual(0, RippleRule.Slot(slots, 10.4f, 0.5f), "all busy: the oldest makes way");
            slots[3].Running = false;
            Assert.AreEqual(3, RippleRule.Slot(slots, 10.4f, 0.5f), "a free slot before the oldest running one");

            slots = new[]
            {
                new Ripple { Start = 10.2f, Running = true },
                new Ripple { Start = 10.0f, Running = true },
                new Ripple { Start = 9.9f, Running = true },
                new Ripple { Start = 10.3f, Running = true },
            };
            Assert.AreEqual(1, RippleRule.Slot(slots, 10.55f, 0.5f), "a ripple that has run its time is free: the first such slot");
        }

        [Test]
        public void StartFlipsStartsOneRipplePerFlipAndNoneOnTheFirstPaint()
        {
            var slots = new Ripple[RippleRule.Slots];
            var painted = new ZoneState[3];
            ZoneState[] now = { Zone(-1), Zone(1), Zone(0) };
            Assert.AreEqual(0, RippleRule.StartFlips(painted, now, 3, Numbers(2f, 0.5f), theme, slots, 5f), "the first paint starts nothing");
            foreach (Ripple r in slots)
                Assert.IsFalse(r.Running);

            painted = (ZoneState[])now.Clone();
            now = new[] { Zone(-1), Zone(2, radius: 6f), Zone(0) };
            Assert.AreEqual(1, RippleRule.StartFlips(painted, now, 3, Numbers(2f, 0.5f), theme, slots, 7f), "zone 1 flipped");
            Ripple started = slots[0];
            Assert.IsTrue(started.Running);
            Assert.AreEqual(new Vector2(10f, -4f), started.Centre, "from the tower");
            Assert.AreEqual(6f + 2f, started.Reach, 1e-5f, "across the zone's band: its live radius plus the band");
            Assert.AreEqual(7f, started.Start);
            Assert.AreEqual(RippleRule.ColourOf(2, theme), started.Colour, "in the new owner's colour");
        }

        [Test]
        public void PackHandsTheRunningRipplesToTheShader()
        {
            var slots = new Ripple[RippleRule.Slots];
            slots[1] = new Ripple { Centre = new Vector2(3f, -7f), Reach = 11f, Colour = new Color(0.6f, 0.2f, 1f, 1f), Start = 2f, Running = true };
            slots[2] = new Ripple { Centre = new Vector2(1f, 1f), Reach = 9f, Colour = Color.white, Start = 0.5f, Running = true };
            var shape = new Vector4[RippleRule.Slots];
            var colour = new Vector4[RippleRule.Slots];
            Assert.IsTrue(RippleRule.Pack(slots, 2.25f, Numbers(2f, 0.5f, 1.5f), shape, colour), "one is running");
            Assert.AreEqual(3f, shape[1].x);
            Assert.AreEqual(-7f, shape[1].y);
            Assert.AreEqual(RippleRule.Radius(0.25f, 0.5f, 11f), shape[1].z, 1e-5f, "the ring's radius now");
            Assert.AreEqual(RippleRule.Strength(0.25f, 0.5f) * 1.5f, shape[1].w, 1e-5f, "its strength times Ripple Glow");
            Color linear = new Color(0.6f, 0.2f, 1f, 1f).linear;
            Assert.AreEqual(linear.r, colour[1].x, 1e-5f, "the colour in linear space, like the owner tiles");
            Assert.AreEqual(linear.g, colour[1].y, 1e-5f);
            Assert.AreEqual(linear.b, colour[1].z, 1e-5f);
            Assert.AreEqual(11f, colour[1].w, "the reach: the ring never runs past the zone's band");
            Assert.AreEqual(0f, shape[2].w, "a finished ripple hands over nothing");
            Assert.AreEqual(0f, shape[0].w, "an empty slot hands over nothing");
            Assert.IsFalse(slots[2].Running, "and is free again");

            Assert.IsFalse(RippleRule.Pack(slots, 3f, Numbers(2f, 0.5f, 1.5f), shape, colour), "all over");
            Assert.AreEqual(0f, shape[1].w);
        }

        [Test]
        public void StopAllEndsEveryRipple()
        {
            var slots = new Ripple[RippleRule.Slots];
            slots[0] = new Ripple { Start = 1f, Running = true };
            slots[3] = new Ripple { Start = 1.2f, Running = true };
            RippleRule.StopAll(slots);
            foreach (Ripple r in slots)
                Assert.IsFalse(r.Running);
            var shape = new Vector4[RippleRule.Slots];
            Assert.IsFalse(RippleRule.Pack(slots, 1.3f, Numbers(2f, 0.5f), shape, new Vector4[RippleRule.Slots]), "nothing left to draw");
        }
    }
}
