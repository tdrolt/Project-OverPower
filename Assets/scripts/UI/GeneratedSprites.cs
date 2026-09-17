using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Plain white shapes the minimap draws with - a disc, an upward triangle, a larger disc for the round Mask,
    /// and a thin edge ring - generated once in code, so there's no sprite asset to keep in sync, and each is
    /// tinted by its Image's colour.
    ///
    /// A sprite is required, not decoration: a Filled Image without one ignores its fill amount and draws full (a
    /// bug this project has hit before). The capture progress ring is a Filled disc. Every shape now carries a mip
    /// chain (review fix, 2026-09-17): without one, a bubble or dot shrinking as the large map scales back to the
    /// corner size shimmered, because a texture with no mips has nothing but its full-resolution level to sample
    /// when minified.
    ///
    /// Textures and sprites are HideAndDontSave, not plain DontSave (review fix, 2026-09-17): DontSave alone let a
    /// small leak (each shape's native texture memory) survive every Play Mode session, since nothing ever called
    /// Destroy on them. The disc != null pattern below already re-creates the cache correctly either way - Unity
    /// overloads == for a destroyed Object to compare equal to null even though the C# reference itself isn't -
    /// this only needed the flag to change, not the caching pattern.
    /// </summary>
    public static class GeneratedSprites
    {
        private const int Size = 128;
        // The Mask's stencil test and the edge ring that covers its seam (BuildFrame) both want the smoothest
        // possible source contour, so they get a dedicated, higher-resolution texture rather than sharing Size.
        private const int LargeSize = 512;

        private static Sprite disc;
        private static Sprite triangle;
        private static Sprite maskDisc;
        private static Sprite edgeRing;
        private static Sprite maskTriangle;
        private static Sprite edgeTriangle;

        public static Sprite Disc => disc != null ? disc : (disc = Build("Generated Disc", Size, (x, y) => DiscAlpha(x, y, Size)));
        public static Sprite Triangle => triangle != null ? triangle : (triangle = Build("Generated Triangle", Size, (x, y) => TriangleAlpha(x, y, Size)));

        /// <summary>A 512 px disc used only for the minimap's round Mask (review fix, 2026-09-17): a UGUI Mask reads
        /// its sprite's alpha as a 1-bit stencil test, so the shared 128 px Disc's contour visibly stepped at
        /// minimap sizes. The finer source texture traces a rounder circle before the threshold test ever runs.</summary>
        public static Sprite MaskDisc => maskDisc != null ? maskDisc : (maskDisc = Build("Generated Mask Disc", LargeSize, (x, y) => DiscAlpha(x, y, LargeSize)));

        /// <summary>A thin ring at the very outer edge (~2% of the radius in from it), drawn UNMASKED on top of the
        /// minimap's masked content (review fix, 2026-09-17): even MaskDisc's finer contour is still a 1-bit test,
        /// so this covers whatever step remains with a normally anti-aliased edge instead of a stencilled one.</summary>
        public static Sprite EdgeRing => edgeRing != null ? edgeRing : (edgeRing = Build("Generated Edge Ring", LargeSize, (x, y) => RingAlpha(x, y, LargeSize)));

        /// <summary>A 512 px equilateral triangle, apex up, used for the minimap's Mask (Tudor, 2026-09-17: the mask
        /// becomes a triangle, one vertex toward each capital). Unlike Triangle above, its 3 vertices sit at equal
        /// distance from the sprite's own pixel centre - its centroid, incentre and circumcentre all coincide for
        /// an equilateral triangle, so rotating the Image (about its pivot, the sprite's centre) turns the triangle
        /// rigidly in place instead of swinging it off-centre.</summary>
        public static Sprite MaskTriangle => maskTriangle != null ? maskTriangle : (maskTriangle = Build("Generated Mask Triangle", LargeSize, (x, y) => TriangleMaskAlpha(x, y, LargeSize)));

        /// <summary>A thin triangular ring following MaskTriangle's own edge, drawn UNMASKED on top (same idea as
        /// EdgeRing): covers the Mask's 1-bit stencil seam with a normally anti-aliased edge instead.</summary>
        public static Sprite EdgeTriangle => edgeTriangle != null ? edgeTriangle : (edgeTriangle = Build("Generated Edge Triangle", LargeSize, (x, y) => TriangleEdgeAlpha(x, y, LargeSize)));

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

        // The outer edge feathers exactly like DiscAlpha; the inner cutoff sits ~2% of the radius in from it, so at
        // the minimap's default corner size (340 units) the visible band is roughly 3 canvas units wide - just
        // enough to sit over the mask's seam without eating into the bubbles and links it frames.
        private static float RingAlpha(float x, float y, int size)
        {
            float half = size / 2f;
            float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half));
            float outerEdge = (half - 1f) - distance + 0.5f;
            float innerEdge = distance - half * 0.98f + 0.5f;
            return Mathf.Min(outerEdge, innerEdge);
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

        // The 3 vertices of an apex-up equilateral triangle centred on the sprite's own pixel centre (unlike
        // TriangleAlpha's left/right/apex, whose centroid sits off the sprite's centre - fine for a static marker,
        // wrong for a mask that has to rotate in place). v0 top, v1 bottom-left, v2 bottom-right - that order is
        // counter-clockwise (same winding EdgeDistance already assumes).
        private static void CentredTriangleVertices(int size, out Vector2 v0, out Vector2 v1, out Vector2 v2)
        {
            float half = size / 2f;
            float r = half - 2f;
            v0 = new Vector2(half, half + r);
            v1 = new Vector2(half - r * 0.8660254f, half - r * 0.5f);
            v2 = new Vector2(half + r * 0.8660254f, half - r * 0.5f);
        }

        private static float TriangleMaskAlpha(float x, float y, int size)
        {
            CentredTriangleVertices(size, out Vector2 v0, out Vector2 v1, out Vector2 v2);
            var p = new Vector2(x, y);
            return Mathf.Min(EdgeDistance(p, v0, v1), Mathf.Min(EdgeDistance(p, v1, v2), EdgeDistance(p, v2, v0))) + 0.5f;
        }

        // Same band idea as RingAlpha, but measured as a straight perpendicular distance to the nearest edge rather
        // than a radius - the natural measure for a polygon, and gives a uniform-width band on every side.
        private static float TriangleEdgeAlpha(float x, float y, int size)
        {
            CentredTriangleVertices(size, out Vector2 v0, out Vector2 v1, out Vector2 v2);
            var p = new Vector2(x, y);
            float inside = Mathf.Min(EdgeDistance(p, v0, v1), Mathf.Min(EdgeDistance(p, v1, v2), EdgeDistance(p, v2, v0)));
            float bandWidth = size * 0.02f;
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
