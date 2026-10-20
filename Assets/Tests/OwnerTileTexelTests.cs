using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>One owner tile in one texel: the state in alpha bands the shader reads (0 plain, 64 neutral, 128..255 owned with the
    /// fill), the colour in linear space because the project renders in linear and the texture is read raw.</summary>
    public class OwnerTileTexelTests
    {
        [Test]
        public void RoundTripKeepsStateAndLinearColour()
        {
            var colour = new Color(0.55f, 0.2f, 0.95f, 1f);
            foreach (TileLook tile in new[]
                     {
                         new TileLook(TileState.Owned, colour, 0.7f),
                         new TileLook(TileState.Neutral, colour, 0f),
                         new TileLook(TileState.Plain, Color.black, 0f),
                     })
            {
                Color32 texel = OwnerTileTexel.Encode(tile);
                TileLook back = OwnerTileTexel.Decode(texel);
                Assert.AreEqual(tile.State, back.State);
                Assert.AreEqual(tile.Fill, back.Fill, 0.01f);
                if (tile.State == TileState.Plain)
                    continue;
                Color linear = colour.linear;
                Assert.AreEqual((byte)Mathf.RoundToInt(linear.r * 255f), texel.r, "the texel holds the linear colour");
                Assert.AreEqual((byte)Mathf.RoundToInt(linear.g * 255f), texel.g);
                Assert.AreEqual((byte)Mathf.RoundToInt(linear.b * 255f), texel.b);
                Assert.AreEqual(colour.r, back.Colour.r, 0.02f);
                Assert.AreEqual(colour.g, back.Colour.g, 0.02f);
                Assert.AreEqual(colour.b, back.Colour.b, 0.02f);
            }
            Assert.AreEqual(0, OwnerTileTexel.Encode(new TileLook(TileState.Plain, Color.white, 1f)).a, "plain is alpha 0");
            Assert.AreEqual(64, OwnerTileTexel.Encode(new TileLook(TileState.Neutral, Color.white, 1f)).a, "neutral is alpha 64");
            Assert.AreEqual(255, OwnerTileTexel.Encode(new TileLook(TileState.Owned, Color.white, 1f)).a, "a full fill is alpha 255");
        }

        [Test]
        public void WallsOnlyTileKeepsItsStateForTheWalls()
        {
            var colour = new Color(0.55f, 0.2f, 0.95f, 1f);
            foreach (TileState state in new[] { TileState.Owned, TileState.Neutral })
            {
                var floor = new TileLook(state, colour, 0.8f);
                Color32 texel = OwnerTileTexel.Encode(floor.OnWallsOnly());
                TileLook back = OwnerTileTexel.Decode(texel);
                Assert.AreEqual(state, back.State, state + ": the walls read it as before");
                Assert.IsTrue(back.WallsOnly, state + ": the floor skips it");
                Assert.IsFalse(OwnerTileTexel.Decode(OwnerTileTexel.Encode(floor)).WallsOnly, state + ": a floor tile is not walls-only");
                Assert.AreEqual(colour.r, back.Colour.r, 0.02f);
            }
            // ArenaHex.hlsl reads 32..95 as neutral and 96 and up as owned on the walls.
            Assert.That(OwnerTileTexel.Encode(new TileLook(TileState.Owned, colour, 1f).OnWallsOnly()).a, Is.InRange(96, 127));
            Assert.That(OwnerTileTexel.Encode(new TileLook(TileState.Neutral, colour, 0f).OnWallsOnly()).a, Is.InRange(32, 63));
            Assert.AreEqual(0, OwnerTileTexel.Encode(new TileLook(TileState.Plain, colour, 0f).OnWallsOnly()).a);
        }

        [Test]
        public void OwnedAtZeroFillStaysOwned()
        {
            Color32 texel = OwnerTileTexel.Encode(new TileLook(TileState.Owned, Color.cyan, 0f));
            Assert.GreaterOrEqual(texel.a, 128, "the shader reads 96 and up as owned");
            TileLook back = OwnerTileTexel.Decode(texel);
            Assert.AreEqual(TileState.Owned, back.State, "the faded edge of an owned band still glows");
            Assert.AreEqual(0f, back.Fill, 1e-6f);
            Assert.AreEqual(TileState.Owned, OwnerTileTexel.Decode(OwnerTileTexel.Encode(new TileLook(TileState.Owned, Color.cyan, -0.5f))).State, "a fill below 0 is clamped, not turned neutral");
        }
    }
}
