using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Two plain white shapes the minimap draws with - a disc and an upward triangle - generated once in code, so
    /// there's no sprite asset to keep in sync, and each is tinted by its Image's colour.
    ///
    /// A sprite is required, not decoration: a Filled Image without one ignores its fill amount and draws full (a
    /// bug this project has hit before). The capture progress ring is a Filled disc. The edges are anti-aliased over
    /// one pixel, so shapes stay smooth when drawn small.
    /// </summary>
    public static class GeneratedSprites
    {
        private const int Size = 128;
        private static Sprite disc;
        private static Sprite triangle;

        public static Sprite Disc => disc != null ? disc : (disc = Build("Generated Disc", DiscAlpha));
        public static Sprite Triangle => triangle != null ? triangle : (triangle = Build("Generated Triangle", TriangleAlpha));

        private static Sprite Build(string name, System.Func<float, float, float> alphaAt)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(alphaAt(x + 0.5f, y + 0.5f))));
            texture.SetPixels32(pixels);
            texture.Apply(false, true); // no longer readable: frees the CPU copy

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static float DiscAlpha(float x, float y)
        {
            float half = Size / 2f;
            float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half));
            return (half - 1f) - distance + 0.5f;
        }

        // Apex at the top centre, base along the bottom: points up (+y) before any rotation.
        private static float TriangleAlpha(float x, float y)
        {
            var p = new Vector2(x, y);
            var left = new Vector2(2f, 2f);
            var right = new Vector2(Size - 2f, 2f);
            var apex = new Vector2(Size / 2f, Size - 2f);
            float inside = Mathf.Min(EdgeDistance(p, left, right), Mathf.Min(EdgeDistance(p, right, apex), EdgeDistance(p, apex, left)));
            return inside + 0.5f;
        }

        // Signed distance from p to the line a->b, positive on the inside of a counter-clockwise triangle.
        private static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            return (edge.x * (p.y - a.y) - edge.y * (p.x - a.x)) / edge.magnitude;
        }
    }
}
