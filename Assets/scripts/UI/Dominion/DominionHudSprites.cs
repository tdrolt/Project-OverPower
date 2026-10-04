using UnityEngine;

namespace Overpower.UI
{
    /// <summary>The one ring sprite of the Dominion HUD (an empty round-win dot), tinted by whoever draws it. Built once per ring thickness.</summary>
    public static class DominionHudSprites
    {
        private static Sprite ring;
        private static float ringFraction = -1f;

        /// <summary>A ring: the edge of a disc, <paramref name="fraction"/> of its radius thick (0..1).</summary>
        public static Sprite Ring(float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            if (ring != null && Mathf.Approximately(ringFraction, fraction)) return ring;
            if (ring != null) { Object.Destroy(ring.texture); Object.Destroy(ring); }
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Dominion Round Dot Ring", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[size * size];
            float half = size * 0.5f, outer = half - 1f, inner = outer * (1f - fraction);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half));
                    float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            ring = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            ring.hideFlags = HideFlags.HideAndDontSave;
            ringFraction = fraction;
            return ring;
        }

        /// <summary>The ring of an empty round-win dot at the theme's sizes.</summary>
        public static Sprite EmptyDot(UiTheme theme) => Ring(theme.dominionDotRing / Mathf.Max(1f, theme.dominionDotSize * 0.5f));
    }
}
