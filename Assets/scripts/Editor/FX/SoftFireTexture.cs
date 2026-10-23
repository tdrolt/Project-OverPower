using Overpower.Abilities;
using UnityEditor;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>Draws the Soft Fire sheet the flames of the Flamethrower and the Rocket's burning ground use: FlameLook.FireSheetTiles x
    /// FireSheetTiles cells, each a soft, billowing puff (a few overlapping blobs broken up by noise) with a dense, bright middle and a
    /// ragged, feathered edge, white so each flame's own colour shows. Every cell draws only inside the circle that fits it: the cone and
    /// floor rules count on that (FlameLookTests.TheShippedFireShapeNeverDrawsOutsideItsCircle). Seeded, so a rerun draws the same sheet.</summary>
    public static class SoftFireTexture
    {
        public const string Path = "Assets/Gameplay/FX/Textures/Soft Fire.png";
        private const int CellPixels = 128;

        [MenuItem("OverPower/FX/Make Soft Fire Texture")]
        public static void Make()
        {
            int size = CellPixels * FlameLook.FireSheetTiles;
            var sheet = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            try
            {
                var pixels = new Color32[size * size];
                for (int cy = 0; cy < FlameLook.FireSheetTiles; cy++)
                    for (int cx = 0; cx < FlameLook.FireSheetTiles; cx++)
                        DrawPuff(pixels, size, cx * CellPixels, cy * CellPixels, cy * FlameLook.FireSheetTiles + cx);
                sheet.SetPixels32(pixels);
                sheet.Apply(false);
                System.IO.File.WriteAllBytes(Path, sheet.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(sheet);
            }
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void DrawPuff(Color32[] pixels, int stride, int x0, int y0, int seed)
        {
            // Three to five billows round the middle; the first sits on it, so every puff is dense where its particle is.
            int billows = 3 + seed % 3;
            var centres = new Vector2[billows];
            var radii = new float[billows];
            for (int b = 0; b < billows; b++)
            {
                float angle = Hash(seed * 31 + b * 7) * Mathf.PI * 2f;
                float away = b == 0 ? 0f : 0.28f + 0.25f * Hash(seed * 17 + b * 13);
                centres[b] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * away;
                radii[b] = b == 0 ? 0.55f : 0.3f + 0.18f * Hash(seed * 23 + b * 5);
            }
            var offset = new Vector2(Hash(seed * 3 + 1) * 50f, Hash(seed * 5 + 2) * 50f);
            for (int y = 0; y < CellPixels; y++)
                for (int x = 0; x < CellPixels; x++)
                {
                    // -1..1 across the cell: the circle that fits the cell is radius 1.
                    var p = new Vector2((x + 0.5f) / CellPixels * 2f - 1f, (y + 0.5f) / CellPixels * 2f - 1f);
                    float density = 0f;
                    for (int b = 0; b < billows; b++)
                    {
                        float d = (p - centres[b]).magnitude / radii[b];
                        density += Mathf.Exp(-d * d * 1.6f);
                    }
                    float noise = Fbm(p * 3.4f + offset);
                    density *= 0.35f + 1.3f * noise;
                    float r = p.magnitude;
                    // A ragged rim from the noise, then a clean fade to nothing just inside the circle.
                    float rim = 1f - Smooth(0.6f, 1f, r + 0.45f * (noise - 0.5f));
                    float alpha = (1f - Mathf.Exp(-density * 1.5f)) * rim * (1f - Smooth(0.9f, 0.97f, r));
                    float heat = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(density));
                    pixels[(y0 + y) * stride + x0 + x] = new Color(heat, heat, heat, alpha);
                }
        }

        private static float Smooth(float from, float to, float t)
        {
            t = Mathf.Clamp01((t - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        private static float Hash(int n)
        {
            unchecked
            {
                uint h = (uint)n * 747796405u + 2891336453u;
                h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
                h = (h >> 22) ^ h;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static float ValueNoise(Vector2 p)
        {
            int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y);
            float fx = p.x - ix, fy = p.y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix * 73856093 ^ iy * 19349663), b = Hash((ix + 1) * 73856093 ^ iy * 19349663);
            float c = Hash(ix * 73856093 ^ (iy + 1) * 19349663), d = Hash((ix + 1) * 73856093 ^ (iy + 1) * 19349663);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Fbm(Vector2 p)
        {
            float sum = 0f, weight = 0.5f, total = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += weight * ValueNoise(p);
                total += weight;
                p *= 2.03f;
                weight *= 0.5f;
            }
            return sum / total;
        }
    }
}
