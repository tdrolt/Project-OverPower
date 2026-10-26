using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>One floor tile in one texel of the owner-tile texture, as ArenaHex.hlsl's ArenaOwnerAt reads it: RGB the colour in
    /// linear space (the texture is linear and read raw), alpha the state: 0 plain, 64 neutral, 128..255 owned with the fill 0..1,
    /// and for a tile only the walls read 40 neutral, 104 owned (the walls see 32..95 as neutral, 96 and up as owned; the floor
    /// skips 32..55 and 96..119). An owned tile at fill 0 stays at 128, well clear of the splits, so a band's faded edge still glows.</summary>
    public static class OwnerTileTexel
    {
        private const byte NeutralAlpha = 64;
        private const byte OwnedAlpha = 128;
        private const byte WallsOnlyNeutralAlpha = 40;
        private const byte WallsOnlyOwnedAlpha = 104;

        public static Color32 Encode(TileLook tile)
        {
            if (tile.State == TileState.Plain)
                return new Color32(0, 0, 0, 0);
            Color linear = tile.Colour.linear;
            byte alpha = tile.State == TileState.Neutral
                ? (tile.WallsOnly ? WallsOnlyNeutralAlpha : NeutralAlpha)
                : tile.WallsOnly ? WallsOnlyOwnedAlpha : (byte)(OwnedAlpha + Mathf.RoundToInt(Mathf.Clamp01(tile.Fill) * 127f));
            return new Color32(Byte(linear.r), Byte(linear.g), Byte(linear.b), alpha);
        }

        /// <summary>The shader's reading of a texel, for tests and probes (ArenaOwnerAt is the copy the game draws with).</summary>
        public static TileLook Decode(Color32 texel)
        {
            if (texel.a < 32)
                return new TileLook(TileState.Plain, Color.black, 0f);
            Color colour = new Color(texel.r / 255f, texel.g / 255f, texel.b / 255f, 1f).gamma;
            if (texel.a < 96)
                return new TileLook(TileState.Neutral, colour, 0f, texel.a < 56);
            return new TileLook(TileState.Owned, colour, Mathf.Clamp01((texel.a - OwnedAlpha) / 127f), texel.a < 120);
        }

        private static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
    }
}
