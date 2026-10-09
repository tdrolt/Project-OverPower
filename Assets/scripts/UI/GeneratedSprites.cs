using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Plain white shapes the minimap draws with (disc, upward triangle, the triangular Mask and its edge shape),
    /// generated once in code and tinted by each Image's colour. A sprite is required: a Filled Image without one
    /// ignores its fill amount and draws full (the capture ring is a Filled disc). Every shape carries a mip chain,
    /// or a bubble shrinking with the map shimmers. Textures and sprites are HideAndDontSave, not DontSave, which
    /// leaked their native memory across Play Mode sessions; the `disc != null` cache re-creates correctly either way.
    /// </summary>
    public static class GeneratedSprites
    {
        private const int Size = 128;
        // The Mask's stencil test and the edge shape that covers its seam (BuildFrame) both want the smoothest
        // possible source contour, so they get a dedicated, higher-resolution texture rather than sharing Size.
        private const int LargeSize = 512;

        private static Sprite disc;
        private static Sprite triangle;

        public static Sprite Disc => disc != null ? disc : (disc = Build("Generated Disc", Size, (x, y) => DiscAlpha(x, y, Size)));
        public static Sprite Triangle => triangle != null ? triangle : (triangle = Build("Generated Triangle", Size, (x, y) => TriangleAlpha(x, y, Size)));

        /// <summary>A 512 px equilateral triangle, apex up, for the minimap's Mask. Unlike Triangle above its 3
        /// vertices sit at equal distance from the sprite's pixel centre, so rotating the Image about its pivot turns
        /// the triangle rigidly in place instead of swinging it off-centre. Not cached: bandFraction comes from
        /// UiTheme.minimapFrameWidth and MinimapView calls this once per session. Shrunk inward by half the frame
        /// band (see EdgeTriangle) so the Mask's 1-bit stencil cut sits under solid frame colour, not at its
        /// outer visible edge where a faint jagged step would show.</summary>
        public static Sprite BuildTriangleMask(float bandFraction)
        {
            float band = LargeSize * Mathf.Max(0f, bandFraction);
            return Build("Generated Mask Triangle", LargeSize, (x, y) => TriangleMaskAlpha(x, y, LargeSize, band / 2f));
        }

        /// <summary>A thin triangular ring following the TRUE (unshrunk) triangle edge, drawn UNMASKED on top: covers
        /// the Mask's 1-bit stencil seam with a normally anti-aliased edge. The band is bandFraction x LargeSize and
        /// is scaled back down by the Image's displayed size / LargeSize, so pass minimapFrameWidth /
        /// minimapCornerSize to get exactly minimapFrameWidth canvas units at the corner size (it scales up with
        /// the large map like bubbles and links). Not cached, same reason as BuildTriangleMask.</summary>
        public static Sprite BuildTriangleEdge(float bandFraction)
        {
            float band = LargeSize * Mathf.Max(0f, bandFraction);
            return Build("Generated Edge Triangle", LargeSize, (x, y) => TriangleEdgeAlpha(x, y, LargeSize, band));
        }

        private static Sprite Build(string name, int size, System.Func<float, float, float> alphaAt)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(alphaAt(x + 0.5f, y + 0.5f))));
            texture.SetPixels32(pixels);
            texture.Apply(true, true); // builds the mip chain, then frees the CPU copy (no longer readable)

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static float DiscAlpha(float x, float y, int size)
        {
            float half = size / 2f;
            float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half));
            return (half - 1f) - distance + 0.5f;
        }

        // Apex at the top centre, base along the bottom: points up (+y) before any rotation.
        private static float TriangleAlpha(float x, float y, int size)
        {
            var p = new Vector2(x, y);
            var left = new Vector2(2f, 2f);
            var right = new Vector2(size - 2f, 2f);
            var apex = new Vector2(size / 2f, size - 2f);
            float inside = Mathf.Min(EdgeDistance(p, left, right), Mathf.Min(EdgeDistance(p, right, apex), EdgeDistance(p, apex, left)));
            return inside + 0.5f;
        }

        // Apex-up equilateral triangle centred on the sprite's pixel centre (TriangleAlpha's centroid is off-centre,
        // wrong for a mask that rotates in place). Counter-clockwise order, the winding EdgeDistance assumes.
        // shrink pulls every vertex in toward the centre by that many pixels (0 = the true edge).
        private static void CentredTriangleVertices(int size, float shrink, out Vector2 v0, out Vector2 v1, out Vector2 v2)
        {
            float half = size / 2f;
            float r = half - 2f - shrink;
            v0 = new Vector2(half, half + r);
            v1 = new Vector2(half - r * 0.8660254f, half - r * 0.5f);
            v2 = new Vector2(half + r * 0.8660254f, half - r * 0.5f);
        }

        private static float TriangleMaskAlpha(float x, float y, int size, float shrink)
        {
            CentredTriangleVertices(size, shrink, out Vector2 v0, out Vector2 v1, out Vector2 v2);
            var p = new Vector2(x, y);
            return Mathf.Min(EdgeDistance(p, v0, v1), Mathf.Min(EdgeDistance(p, v1, v2), EdgeDistance(p, v2, v0))) + 0.5f;
        }

        // Perpendicular distance to the nearest TRUE (unshrunk) edge, not a radius: a uniform-width band on every side.
        private static float TriangleEdgeAlpha(float x, float y, int size, float bandWidth)
        {
            CentredTriangleVertices(size, 0f, out Vector2 v0, out Vector2 v1, out Vector2 v2);
            var p = new Vector2(x, y);
            float inside = Mathf.Min(EdgeDistance(p, v0, v1), Mathf.Min(EdgeDistance(p, v1, v2), EdgeDistance(p, v2, v0)));
            float outerEdge = inside + 0.5f;
            float innerEdge = bandWidth - inside + 0.5f;
            return Mathf.Min(outerEdge, innerEdge);
        }

        // Signed distance from p to the line a->b, positive on the inside of a counter-clockwise triangle.
        private static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            return (edge.x * (p.y - a.y) - edge.y * (p.x - a.x)) / edge.magnitude;
        }
    }
}
