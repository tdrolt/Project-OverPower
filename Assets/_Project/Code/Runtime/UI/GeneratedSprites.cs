using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Plain white shapes the minimap, the crosshair and the plates draw with (disc, upward triangle, the triangular Mask and its edge shape,
    /// hexagon and square outlines, the armour "v"),
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
        private const float TriangleCornerInset = 2f; // pixels between the centred triangle's corners and the sprite's edge

        private static Sprite disc;
        private static Sprite triangle;

        public static Sprite Disc => disc != null ? disc : (disc = Build("Generated Disc", Size, (x, y) => DiscAlpha(x, y, Size)));
        public static Sprite Triangle => triangle != null ? triangle : (triangle = Build("Generated Triangle", Size, (x, y) => TriangleAlpha(x, y, Size)));

        private static Sprite vignette;

        /// <summary>Clear in the middle, opaque in the corners (the death card's dark edge): stretched over the screen it follows its shape.</summary>
        public static Sprite Vignette => vignette != null ? vignette : (vignette = Build("Generated Vignette", Size, (x, y) => VignetteAlpha(x, y, Size)));

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

        /// <summary>A pointy-top hexagon outline (corners up and down, flat sides left and right), corners 2 px inside the sprite's
        /// edge. The line is bandFraction x the sprite's size, so pass the wanted line width / the Image's displayed size. Not
        /// cached: the band comes from UiTheme.</summary>
        public static Sprite BuildHexRing(float bandFraction)
        {
            float band = Size * Mathf.Clamp01(bandFraction);
            return Build("Generated Hex Ring", Size, (x, y) => HexRingAlpha(x, y, Size, band));
        }

        /// <summary>A square outline for the plate's Absorb marks; the line is bandFraction x the sprite's size, so pass the wanted
        /// line width / the Image's displayed size. Not cached: the band comes from UiTheme.</summary>
        public static Sprite BuildSquareRing(float bandFraction)
        {
            float band = Size * Mathf.Clamp01(bandFraction);
            return Build("Generated Square Ring", Size, (x, y) => SquareRingAlpha(x, y, Size, band));
        }

        /// <summary>A "v" pointing down for the plate's Recharge marks, twice as wide as it is tall (show it in a 2:1 rect), its stroke
        /// strokeFraction x the sprite's width. Not cached: the stroke comes from UiTheme.</summary>
        public static Sprite BuildChevron(float strokeFraction)
        {
            float stroke = Size * Mathf.Clamp(strokeFraction, 0.01f, 0.4f);
            return Build("Generated Chevron", Size, Size / 2, (x, y) => ChevronAlpha(x, y, Size, Size / 2, stroke));
        }

        private static float SquareRingAlpha(float x, float y, int size, float band)
        {
            float inside = Mathf.Min(Mathf.Min(x, size - x), Mathf.Min(y, size - y));
            return Mathf.Min(inside + 0.5f, band - inside + 0.5f);
        }

        // Two strokes meeting at the bottom centre, with round ends kept a pixel inside the sprite.
        private static float ChevronAlpha(float x, float y, int width, int height, float stroke)
        {
            float m = stroke * 0.5f + 1f;
            var p = new Vector2(x, y);
            var left = new Vector2(m, height - m);
            var tip = new Vector2(width * 0.5f, m);
            var right = new Vector2(width - m, height - m);
            float d = Mathf.Min(SegmentDistance(p, left, tip), SegmentDistance(p, tip, right));
            return stroke * 0.5f - d + 0.5f;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + t * ab);
        }

        /// <summary>Alpha of BuildHexRing's sprite at pixel centre (x, y): 1 on the line, 0 inside and outside, anti-aliased by half a pixel.</summary>
        public static float HexRingAlpha(float x, float y, int size, float band)
        {
            float half = size / 2f;
            float apothem = (half - TriangleCornerInset) * 0.8660254f;
            float ax = Mathf.Abs(x - half), ay = Mathf.Abs(y - half);
            // Distance along the nearest side's normal: the sides face 0, 60 and 120 degrees.
            float d = Mathf.Max(ax, 0.5f * ax + 0.8660254f * ay);
            return Mathf.Clamp01(Mathf.Min(apothem - d + 0.5f, d - (apothem - band) + 0.5f));
        }

        private static Sprite Build(string name, int size, System.Func<float, float, float> alphaAt) => Build(name, size, size, alphaAt);

        private static Sprite Build(string name, int width, int height, System.Func<float, float, float> alphaAt)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(alphaAt(x + 0.5f, y + 0.5f))));
            texture.SetPixels32(pixels);
            texture.Apply(true, true); // builds the mip chain, then frees the CPU copy (no longer readable)

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
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

        // 0 inside about half the way to the corners, rising smoothly to 1 at the corners (about 0.45 at the middle of an edge).
        private static float VignetteAlpha(float x, float y, int size)
        {
            float half = size / 2f;
            float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / (half * 1.41421356f);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, r));
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
            float r = half - TriangleCornerInset - shrink;
            v0 = new Vector2(half, half + r);
            v1 = new Vector2(half - r * 0.8660254f, half - r * 0.5f);
            v2 = new Vector2(half + r * 0.8660254f, half - r * 0.5f);
        }

        /// <summary>The centred triangle's corner distance from the centre as a share of the sprite's size (CentredTriangleVertices, unshrunk).</summary>
        public static float CentredTriangleRadiusFraction => (LargeSize / 2f - TriangleCornerInset) / LargeSize;

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
