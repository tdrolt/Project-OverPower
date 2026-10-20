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
            float barrier = Spread(RenderFace("Arena Barrier", null));
            float wall = Spread(RenderFace("Arena Wall", null));
            Assert.Greater(wall, 0.02f, "the control: a wall face of the same size shows stripes");
            Assert.Less(barrier, 0.002f, "a barrier face is one even colour");
        }

        // A boundary wall stands on tiles whose centre is past the arena's outline; the painter keeps those for the walls alone.
        [Test]
        public void WallStripesReadAWallsOnlyTile()
        {
            Color brightest = Brightest(RenderFace("Arena Wall", OwnerTileTexel.Encode(new TileLook(TileState.Owned, Color.red, 1f).OnWallsOnly())));
            Assert.Greater(brightest.r, 0.3f, "the stripes glow in the owner's colour");
            Assert.Less(brightest.g + brightest.b, 0.2f * brightest.r, "in that colour only");
            Color plain = Brightest(RenderFace("Arena Wall", null));
            Assert.Greater(plain.g, 0.5f * plain.r, "the control: away from every zone the stripes are the plain white");
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static float Spread(Color[] pixels)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Color c in pixels)
            {
                lo = Mathf.Min(lo, Luma(c));
                hi = Mathf.Max(hi, Luma(c));
            }
            return hi - lo;
        }

        private static Color Brightest(Color[] pixels)
        {
            Color best = Color.black;
            foreach (Color c in pixels)
                if (Luma(c) > Luma(best))
                    best = c;
            return best;
        }

        // Renders a 5 x 5 m face of the material straight on (orthographic, so the light and the shine are even across it). With an
        // owner texel, one tile big enough to hold the whole face is bound with it.
        private static Color[] RenderFace(string materialName, Color32? ownerTile)
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
            var tiles = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                if (ownerTile.HasValue)
                {
                    tiles.SetPixels32(new[] { ownerTile.Value });
                    tiles.Apply(false);
                    ArenaShaderGlobals.SetOwnerTiles(tiles, new Vector4(3.7f, 10.95f, 40f, 1f), new Vector4(0f, 0f, 1f, 1f));
                }
                else
                    ArenaShaderGlobals.ClearOwnerTiles();
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
                Color[] pixels = read.GetPixels();
                Assert.Greater(Spread(pixels) + Luma(Brightest(pixels)), 0f, materialName + " drew something");
                return pixels;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                ArenaShaderGlobals.ClearOwnerTiles();
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(block);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(tiles);
            }
        }

        private const float FloorSize = 2f, FloorHalf = 4f; // FloorHalf: metres from the image centre to its edge
        private const int FloorPx = 256;
        private static readonly Vector2 FloorOrigin = new Vector2(0.6f, -0.4f);
        private static readonly Vector2Int FloorMin = new Vector2Int(-2, -2);

        // Renders a floor plane straight from above (orthographic, centred on a tile centre: the grid is mirror-symmetric about it, so
        // an image read upside down still lines up) with a 5 x 5 owner texture whose texel (0, 0) is tile FloorMin.
        private static Texture2D RenderFloor(ArenaLook look, Color32[] ownerTiles)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + "Arena Floor.mat");
            Assert.NotNull(material);
            const float Y = -500f;
            const int Layer = 31;
            var tiles = new Texture2D(5, 5, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            tiles.SetPixels32(ownerTiles);
            tiles.Apply(false);
            bool async = ShaderUtil.allowAsyncCompilation;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var camGo = new GameObject("ArenaShaderTests Camera");
            var target = new RenderTexture(FloorPx, FloorPx, 24, RenderTextureFormat.ARGBHalf);
            var read = new Texture2D(FloorPx, FloorPx, TextureFormat.RGBAHalf, false, true);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                ArenaShaderGlobals.Push(look);
                ArenaShaderGlobals.SetOwnerTiles(tiles, new Vector4(FloorOrigin.x, FloorOrigin.y, FloorSize, 1f), new Vector4(FloorMin.x, FloorMin.y, 5f, 5f));
                floor.hideFlags = camGo.hideFlags = HideFlags.HideAndDontSave;
                Object.DestroyImmediate(floor.GetComponent<Collider>());
                floor.layer = Layer;
                floor.transform.position = new Vector3(FloorOrigin.x, Y, FloorOrigin.y);
                floor.transform.localScale = new Vector3(2f, 1f, 2f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = material;
                floor.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = FloorHalf;
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.transform.position = new Vector3(FloorOrigin.x, Y + 5f, FloorOrigin.y);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.targetTexture = target;
                cam.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, FloorPx, FloorPx), 0, 0);
                RenderTexture.active = previous;
                return read;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                Object.DestroyImmediate(tiles);
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(target);
            }
        }

        private static Color FloorAt(Texture2D read, Vector2 xz)
        {
            int x = Mathf.RoundToInt((xz.x - FloorOrigin.x) / (2f * FloorHalf) * FloorPx + FloorPx * 0.5f - 0.5f);
            int y = Mathf.RoundToInt((xz.y - FloorOrigin.y) / (2f * FloorHalf) * FloorPx + FloorPx * 0.5f - 0.5f);
            return read.GetPixel(x, y);
        }

        // The floor's lines are drawn by ArenaHex.hlsl; the owner tiles are painted from HexGrid. Rendered from straight above,
        // every HexGrid centre must be plain floor and every midpoint between two neighbouring centres (the middle of the shared
        // edge) a line; and a tile painted as owned in the texel HexGrid.Texel gives must light up in the owner's colour there.
        [Test]
        public void TheFloorShaderDrawsHexGridsTiles()
        {
            ArenaLook look = PlainLook();
            look.floorTileSize = FloorSize;
            look.floorLineWidth = 0.12f;
            look.floorGridGlow = 1f;
            look.ownedEdgeGlow = 1f;
            look.ownedFillGlow = 1f;
            var owned = new Vector2Int(1, 0);
            var pixels = new Color32[25];
            Vector2Int texel = HexGrid.Texel(owned, FloorMin);
            pixels[texel.y * 5 + texel.x] = new Color32(255, 0, 0, 255); // owned by a red team, fill at its strongest
            Texture2D read = RenderFloor(look, pixels);
            try
            {
                Vector2Int[] plain = { new Vector2Int(0, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(-1, 1) };
                foreach (Vector2Int t in plain)
                {
                    Vector2 c = HexGrid.CentreOf(t, FloorOrigin, FloorSize);
                    Assert.AreEqual(t, HexGrid.TileAt(c, FloorOrigin, FloorSize));
                    Assert.Less(Luma(FloorAt(read, c)), 0.05f, "the middle of tile " + t + " is plain floor");
                }
                Vector2Int[] neighbours = { new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(-1, 1), new Vector2Int(1, -1) };
                foreach (Vector2Int n in neighbours)
                {
                    Vector2 edge = 0.5f * (HexGrid.CentreOf(Vector2Int.zero, FloorOrigin, FloorSize) + HexGrid.CentreOf(n, FloorOrigin, FloorSize));
                    Assert.Greater(Luma(FloorAt(read, edge)), 0.3f, "a line between tile (0, 0) and " + n);
                }
                Color mid = FloorAt(read, HexGrid.CentreOf(owned, FloorOrigin, FloorSize));
                Assert.Greater(mid.r, 0.3f, "the owned tile glows in its owner's colour");
                Assert.Less(mid.g + mid.b, 0.1f * mid.r, "in that colour only");
            }
            finally
            {
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(read);
            }
        }

        // The texels as the painter writes them (OwnerTileTexel.Encode), read by the shader: an owned tile fills in its colour, and a
        // tile kept for the walls alone (its centre past the arena's outline) leaves the floor plain, lines in the plain white included.
        [Test]
        public void TheFloorReadsEncodedTilesAndSkipsWallsOnlyTiles()
        {
            ArenaLook look = PlainLook();
            look.floorTileSize = FloorSize;
            look.floorLineWidth = 0.3f;
            look.floorGridGlow = 1f;
            look.ownedEdgeGlow = 1f;
            look.ownedFillGlow = 1f;
            var owned = new Vector2Int(1, 0);
            var wallsOnly = new Vector2Int(-1, 0);
            var pixels = new Color32[25];
            Vector2Int a = HexGrid.Texel(owned, FloorMin), b = HexGrid.Texel(wallsOnly, FloorMin);
            pixels[a.y * 5 + a.x] = OwnerTileTexel.Encode(new TileLook(TileState.Owned, Color.red, 1f));
            pixels[b.y * 5 + b.x] = OwnerTileTexel.Encode(new TileLook(TileState.Owned, Color.red, 1f).OnWallsOnly());
            Texture2D read = RenderFloor(look, pixels);
            try
            {
                Color mid = FloorAt(read, HexGrid.CentreOf(owned, FloorOrigin, FloorSize));
                Assert.Greater(mid.r, 0.3f, "the encoded owned tile fills in its owner's colour");
                Assert.Less(mid.g + mid.b, 0.1f * mid.r, "in that colour only");

                Vector2 centre = HexGrid.CentreOf(wallsOnly, FloorOrigin, FloorSize);
                Vector2 edge = 0.5f * (centre + HexGrid.CentreOf(wallsOnly + new Vector2Int(-1, 0), FloorOrigin, FloorSize));
                Color line = FloorAt(read, Vector2.Lerp(edge, centre, 0.1f)); // on the line, 0.1 m inside the walls-only tile
                Assert.Greater(line.r, 0.3f, "the walls-only tile's own line is drawn");
                Assert.Greater(line.g, 0.5f * line.r, "in the plain grid's white, not the owner's red");
                Assert.Less(Luma(FloorAt(read, centre)), 0.05f, "and its middle is plain floor");
            }
            finally
            {
                Object.DestroyImmediate(look);
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
