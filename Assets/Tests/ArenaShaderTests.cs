using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Tests
{
    /// <summary>The four arena materials share one surface shader that picks its pattern by keyword. It must keep the depth and
    /// shadow passes (the fog is rebuilt from depth, so a wall without them would not darken), and the low barriers must stay
    /// plain so cover reads as cover; the barrier check renders a face and compares it with a wall's.</summary>
    public class ArenaShaderTests
    {
        private const string ShaderPath = "Assets/Gameplay/Arena/Shaders/ArenaSurface.shader";
        private const string ShaderName = "Overpower/Arena Surface";
        private const string Materials = "Assets/Gameplay/Arena/Materials/";

        private static readonly Dictionary<string, string> KeywordOf = new Dictionary<string, string>
        {
            { "Arena Floor", "_ARENA_FLOOR" },
            { "Arena Wall", "_ARENA_WALL" },
            { "Arena Block", "_ARENA_BLOCK" },
            { "Arena Barrier", "_ARENA_BARRIER" },
        };

        // The material inspector re-derives the keyword from _Arena, so the two must agree in the saved file.
        private static readonly string[] KeywordOrder = { "_ARENA_FLOOR", "_ARENA_WALL", "_ARENA_BLOCK", "_ARENA_BARRIER" };

        [TearDown]
        public void PutTheRealLookBack()
        {
            ArenaShaderGlobals.ClearOwnerTiles();
            ArenaLookEditorGlobals.PushAll();
        }

        [Test]
        public void HasDepthOnlyShadowCasterDepthNormalsPasses()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.NotNull(shader, ShaderPath);
            Assert.AreEqual(ShaderName, shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "the shader compiles");
            Assert.IsTrue(shader.isSupported, "the shader runs here");
            var modes = new List<string>();
            for (int i = 0; i < shader.passCount; i++)
                modes.Add(shader.FindPassTagValue(i, new ShaderTagId("LightMode")).name.ToUpperInvariant()); // Unity reports SHADOWCASTER in capitals
            CollectionAssert.IsSubsetOf(new[] { "UNIVERSALFORWARD", "SHADOWCASTER", "DEPTHONLY", "DEPTHNORMALS", "META" }, modes,
                "passes: " + string.Join(", ", modes));
            Assert.AreEqual("Opaque", shader.FindSubshaderTagValue(0, new ShaderTagId("RenderType")).name, "opaque, so it writes depth");
        }

        [Test]
        public void FourArenaMaterialsUseItWithTheirKeyword()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.NotNull(shader, ShaderPath);
            foreach (KeyValuePair<string, string> row in KeywordOf)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + row.Key + ".mat");
                Assert.NotNull(material, row.Key);
                Assert.AreSame(shader, material.shader, row.Key + " wears the arena shader");
                foreach (string keyword in KeywordOf.Values)
                    Assert.AreEqual(keyword == row.Value, material.IsKeywordEnabled(keyword), row.Key + " " + keyword);
                Assert.AreEqual(System.Array.IndexOf(KeywordOrder, row.Value), (int)material.GetFloat("_Arena"), row.Key + ": _Arena matches its keyword");
                Assert.LessOrEqual(material.renderQueue, (int)RenderQueue.GeometryLast, row.Key + " is drawn with the opaque geometry");
            }
        }

        [Test]
        public void BarrierHasNoStripes()
        {
            float barrier = FaceSpread("Arena Barrier");
            float wall = FaceSpread("Arena Wall");
            Assert.Greater(wall, 0.02f, "the control: a wall face of the same size shows stripes");
            Assert.Less(barrier, 0.002f, "a barrier face is one even colour");
        }

        // Renders a 5 x 5 m face of the material straight on (orthographic, so the light and the shine are even across it) and
        // returns how far its brightest pixel is from its darkest.
        private static float FaceSpread(string materialName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + materialName + ".mat");
            Assert.NotNull(material, materialName);
            ArenaLook look = PlainLook();
            ArenaShaderGlobals.Push(look);
            const int Layer = 31;
            bool async = ShaderUtil.allowAsyncCompilation;
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var camGo = new GameObject("ArenaShaderTests Camera");
            var target = new RenderTexture(96, 96, 24, RenderTextureFormat.ARGBHalf);
            var read = new Texture2D(96, 96, TextureFormat.RGBAHalf, false, true);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                block.hideFlags = camGo.hideFlags = HideFlags.HideAndDontSave;
                Object.DestroyImmediate(block.GetComponent<Collider>());
                block.layer = Layer;
                block.transform.position = new Vector3(3.7f, -500f, 11.3f);
                block.transform.localScale = new Vector3(8f, 8f, 0.7f);
                block.GetComponent<MeshRenderer>().sharedMaterial = material;
                block.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = 2.5f;
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.transform.position = new Vector3(3.7f, -500f, 5f);
                cam.transform.rotation = Quaternion.identity;
                cam.targetTexture = target;
                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 96, 96), 0, 0);
                RenderTexture.active = previous;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (Color c in read.GetPixels())
                {
                    float y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                }
                Assert.Greater(hi, 0f, materialName + " drew something");
                return hi - lo;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(block);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        // The floor's lines are drawn by ArenaHex.hlsl; the owner tiles will be painted from HexGrid. Rendered from straight above,
        // every HexGrid centre must be plain floor and every midpoint between two neighbouring centres (the middle of the shared
        // edge) a line; and a tile painted as owned in the texel HexGrid.Texel gives must light up in the owner's colour there.
        [Test]
        public void TheFloorShaderDrawsHexGridsTiles()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + "Arena Floor.mat");
            Assert.NotNull(material);
            ArenaLook look = PlainLook();
            look.floorTileSize = 2f;
            look.floorLineWidth = 0.12f;
            look.floorGridGlow = 1f;
            look.ownedEdgeGlow = 1f;
            look.ownedFillGlow = 1f;
            var origin = new Vector2(0.6f, -0.4f);
            const float Size = 2f, Y = -500f, Half = 4f; // Half: metres from the image centre to its edge
            const int Px = 256, Layer = 31;
            var owned = new Vector2Int(1, 0);
            var min = new Vector2Int(-2, -2);
            var tiles = new Texture2D(5, 5, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            tiles.SetPixels32(new Color32[25]);
            Vector2Int texel = HexGrid.Texel(owned, min);
            tiles.SetPixel(texel.x, texel.y, new Color(1f, 0f, 0f, 1f)); // owned by a red team, fill at its strongest
            tiles.Apply(false);

            bool async = ShaderUtil.allowAsyncCompilation;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var camGo = new GameObject("ArenaShaderTests Camera");
            var target = new RenderTexture(Px, Px, 24, RenderTextureFormat.ARGBHalf);
            var read = new Texture2D(Px, Px, TextureFormat.RGBAHalf, false, true);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                ArenaShaderGlobals.Push(look);
                ArenaShaderGlobals.SetOwnerTiles(tiles, new Vector4(origin.x, origin.y, Size, 1f), new Vector4(min.x, min.y, 5f, 5f));
                floor.hideFlags = camGo.hideFlags = HideFlags.HideAndDontSave;
                Object.DestroyImmediate(floor.GetComponent<Collider>());
                floor.layer = Layer;
                floor.transform.position = new Vector3(origin.x, Y, origin.y);
                floor.transform.localScale = new Vector3(2f, 1f, 2f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = material;
                floor.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = Half;
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                // Centred on a tile centre: the grid is mirror-symmetric about it, so an image read upside down still lines up.
                cam.transform.position = new Vector3(origin.x, Y + 5f, origin.y);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.targetTexture = target;
                cam.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Px, Px), 0, 0);
                RenderTexture.active = previous;

                Color At(Vector2 xz)
                {
                    int x = Mathf.RoundToInt((xz.x - origin.x) / (2f * Half) * Px + Px * 0.5f - 0.5f);
                    int y = Mathf.RoundToInt((xz.y - origin.y) / (2f * Half) * Px + Px * 0.5f - 0.5f);
                    return read.GetPixel(x, y);
                }
                float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

                Vector2Int[] plain = { new Vector2Int(0, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(-1, 1) };
                foreach (Vector2Int t in plain)
                {
                    Vector2 c = HexGrid.CentreOf(t, origin, Size);
                    Assert.AreEqual(t, HexGrid.TileAt(c, origin, Size));
                    Assert.Less(Luma(At(c)), 0.05f, "the middle of tile " + t + " is plain floor");
                }
                Vector2Int[] neighbours = { new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(-1, 1), new Vector2Int(1, -1) };
                foreach (Vector2Int n in neighbours)
                {
                    Vector2 edge = 0.5f * (HexGrid.CentreOf(Vector2Int.zero, origin, Size) + HexGrid.CentreOf(n, origin, Size));
                    Assert.Greater(Luma(At(edge)), 0.3f, "a line between tile (0, 0) and " + n);
                }
                Color mid = At(HexGrid.CentreOf(owned, origin, Size));
                Assert.Greater(mid.r, 0.3f, "the owned tile glows in its owner's colour");
                Assert.Less(mid.g + mid.b, 0.1f * mid.r, "in that colour only");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(tiles);
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        // Literal numbers (never the real asset's tuning): black, matte, no sky and no sun, so all that shows is the pattern's glow.
        private static ArenaLook PlainLook()
        {
            var look = ScriptableObject.CreateInstance<ArenaLook>();
            var black = new SurfaceLook(Color.black, 0f, 0f);
            look.floorSurface = look.wallSurface = look.blockSurface = black;
            look.barrierSurface = new SurfaceLook(new Color(0.8f, 0.5f, 0.2f, 1f), 0f, 0f);
            look.reflectionStrength = 0f;
            look.outsideLineColor = Color.white;
            look.outsideStripeGlow = 1f;
            look.stripeGlow = 1f;
            look.circuitCellSize = 1.5f;
            look.stripeWidth = 0.08f;
            look.nodeSize = 0.25f;
            look.topEdgeWidth = 0.08f;
            look.circuitAcrossChance = 0.6f;
            look.circuitUpChance = 0.4f;
            look.floorTileSize = 3f;
            look.floorLineWidth = 0.05f;
            look.floorGridGlow = 0.5f;
            return look;
        }
    }
}
