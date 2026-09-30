using System.Collections.Generic;
using UnityEngine;
using Overpower.Arena;

namespace Overpower.UI
{
    /// <summary>
    /// The minimap's picture of a closed corner (Tudor, 2026-09-25: the minimap darkens the closed part and draws the
    /// wall). One texture laid over the baked arena picture, covering the same square of the world, so it turns with
    /// it. Pure: tested without a scene.
    /// </summary>
    public static class MinimapCutMask
    {
        public enum Pixel { Open, Closed, Wall }

        public static Pixel Classify(Vector2 worldXZ, PhaseTwoCutGeometry cut, float wallHalfWidthMetres)
        {
            if (DistanceToLine(worldXZ, cut.WallLine) <= wallHalfWidthMetres)
                return Pixel.Wall;
            return cut.Closed.SignedDistance(worldXZ) >= 0f ? Pixel.Closed : Pixel.Open;
        }

        /// <summary>size x size pixels over the worldSizeMetres square centred on worldCentreXZ, bottom row first
        /// (Texture2D.SetPixels32 order; the baked picture's +Z is up). Open ground is transparent.</summary>
        public static Color32[] Paint(int size, Vector2 worldCentreXZ, float worldSizeMetres, PhaseTwoCutGeometry cut,
                                      float wallHalfWidthMetres, Color32 closed, Color32 wall)
        {
            var pixels = new Color32[size * size];
            float metresPerPixel = worldSizeMetres / size;
            Vector2 corner = worldCentreXZ - Vector2.one * (worldSizeMetres * 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var world = corner + new Vector2((x + 0.5f) * metresPerPixel, (y + 0.5f) * metresPerPixel);
                    Pixel kind = Classify(world, cut, wallHalfWidthMetres);
                    pixels[y * size + x] = kind == Pixel.Wall ? wall : kind == Pixel.Closed ? closed : default;
                }
            return pixels;
        }

        private static float DistanceToLine(Vector2 p, IReadOnlyList<Vector2> line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
                best = Mathf.Min(best, ArenaBounds.DistanceToSegment(p, line[i], line[i + 1]));
            return best;
        }
    }
}
