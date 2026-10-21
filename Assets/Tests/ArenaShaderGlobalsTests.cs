using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The arena look numbers reach the arena shader only as shader globals (never per material or per object, so the SRP
    /// Batcher and Batching Static keep working). These check the hand-over from ArenaLook and the owner-tile switch.</summary>
    public class ArenaShaderGlobalsTests
    {
        [TearDown]
        public void PutTheRealLookBack()
        {
            ArenaShaderGlobals.ClearOwnerTiles();
            ArenaLookEditorGlobals.PushAll(); // the real look and the open arena's centre
        }

        [Test]
        public void TileSizeAndReflectionHandedOver()
        {
            ArenaLook look = Literal();
            try
            {
                ArenaGlobals g = ArenaShaderGlobals.From(look);
                Assert.AreEqual(2.5f, g.TileSize);
                Assert.AreEqual(0.3f, g.Reflection);
                Assert.AreEqual(0.07f, g.FloorLineWidth);
                Assert.AreEqual(0.11f, g.FloorGridGlow);
                Assert.AreEqual(1.1f, g.OwnedEdgeGlow);
                Assert.AreEqual(0.15f, g.OwnedFillGlow);
                Assert.AreEqual(1.3f, g.StripeGlow, "stripeGlow is the glow on owned ground");
                Assert.AreEqual(0.4f, g.OutsideStripeGlow, "the glow away from every zone");
                Assert.AreEqual(1.25f, g.CellSize);
                Assert.AreEqual(0.05f, g.StripeWidth);
                Assert.AreEqual(0.2f, g.NodeSize);
                Assert.AreEqual(0.09f, g.TopEdgeWidth);
                Assert.AreEqual(0.6f, g.AcrossChance);
                Assert.AreEqual(0.25f, g.UpChance);
                Assert.AreEqual(4, g.SurfaceBase.Length, "floor, wall, block, barrier");
                Assert.AreEqual(4, g.SurfaceGloss.Length);
                Assert.AreEqual(4, g.SurfaceShine.Length);
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(Gloss[i], g.SurfaceGloss[i], "gloss of surface " + i);
                    Assert.AreEqual(Shine[i], g.SurfaceShine[i], "sun shine of surface " + i);
                    Assert.AreEqual((Vector4)Tint[i].linear, g.SurfaceBase[i], "the tint is handed over in linear space, surface " + i);
                }
                Assert.AreEqual((Vector4)look.outsideLineColor.linear, g.OutsideLine, "outside line colour, linear");
                Assert.IsTrue(IlWiring.Uses(typeof(ArenaShaderGlobals), "Push", typeof(ArenaShaderGlobals).GetMethod(nameof(ArenaShaderGlobals.From))),
                    "Push sends what From reads");
            }
            finally { Object.DestroyImmediate(look); }
        }

        // The packing order inside each vector is what ArenaHex.hlsl reads (_ArenaStripe.y is the stripe width, and so on).
        [Test]
        public void PushReachesTheShaderGlobals()
        {
            ArenaLook look = Literal();
            try
            {
                ArenaShaderGlobals.SetGridOrigin(new Vector2(12f, -7f));
                ArenaShaderGlobals.Push(look);
                Vector4 grid = Shader.GetGlobalVector(ArenaShaderGlobals.OwnerGridId);
                Assert.AreEqual(new Vector3(12f, -7f, 2.5f), (Vector3)grid, "origin x, origin z, tile size");
                Assert.AreEqual(0.3f, Shader.GetGlobalFloat(ArenaShaderGlobals.ReflectionId), 1e-6f);
                Assert.AreEqual(1f, Shader.GetGlobalFloat(ArenaShaderGlobals.LookReadyId));
                AreClose(new Vector4(0.07f, 0.11f, 1.1f, 0.15f), Shader.GetGlobalVector(ArenaShaderGlobals.FloorLineId),
                    "floor: line width, outside grid glow, owned edge glow, owned fill glow");
                AreClose(new Vector4(1.25f, 0.05f, 0.2f, 0.09f), Shader.GetGlobalVector(ArenaShaderGlobals.StripeId),
                    "stripes: cell size, stripe width, node size, top edge width");
                AreClose(new Vector4(0.4f, 1.3f, 0.6f, 0.25f), Shader.GetGlobalVector(ArenaShaderGlobals.StripeGlowId),
                    "stripe glow: outside, owned, across chance, up chance");
                AreClose(look.outsideLineColor.linear, Shader.GetGlobalVector(ArenaShaderGlobals.OutsideLineId), "outside line colour, linear");
                Vector4[] bases = Shader.GetGlobalVectorArray(ArenaShaderGlobals.SurfaceBaseId);
                float[] gloss = Shader.GetGlobalFloatArray(ArenaShaderGlobals.SurfaceGlossId);
                float[] shine = Shader.GetGlobalFloatArray(ArenaShaderGlobals.SurfaceShineId);
                Assert.NotNull(shine, "sun shine per surface is pushed");
                for (int i = 0; i < 4; i++)
                {
                    AreClose(Tint[i].linear, bases[i], "linear base tint of surface " + i);
                    Assert.AreEqual(Gloss[i], gloss[i], 1e-6f, "gloss of surface " + i);
                    Assert.AreEqual(Shine[i], shine[i], 1e-6f, "sun shine of surface " + i);
                }
            }
            finally { Object.DestroyImmediate(look); }
        }

        [Test]
        public void ClearOwnerTilesMarksInvalid()
        {
            var tiles = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.DontSave };
            try
            {
                ArenaShaderGlobals.SetOwnerTiles(tiles, new Vector4(1f, 2f, 3f, 1f), new Vector4(-2f, -2f, 4f, 4f));
                Assert.AreEqual(1f, Shader.GetGlobalVector(ArenaShaderGlobals.OwnerGridId).w, "painted tiles are valid");
                Assert.AreSame(tiles, Shader.GetGlobalTexture(ArenaShaderGlobals.OwnerTexId));
                ArenaShaderGlobals.ClearOwnerTiles();
                Assert.AreEqual(0f, Shader.GetGlobalVector(ArenaShaderGlobals.OwnerGridId).w, "cleared: the shader draws the plain look and never samples");
                Assert.AreNotSame(tiles, Shader.GetGlobalTexture(ArenaShaderGlobals.OwnerTexId), "the painted texture is let go");
            }
            finally { Object.DestroyImmediate(tiles); }
        }

        [Test]
        public void RipplesReachTheShaderAndStopWithTheOwnerTiles()
        {
            var shape = new[] { new Vector4(1f, 2f, 3f, 0.5f), Vector4.zero, Vector4.zero, new Vector4(4f, 5f, 6f, 0.25f) };
            var colour = new[] { new Vector4(0.1f, 0.2f, 0.3f, 9f), Vector4.zero, Vector4.zero, new Vector4(0.4f, 0.5f, 0.6f, 7f) };
            try
            {
                ArenaShaderGlobals.SetRipples(shape, colour, 1.75f, true);
                Assert.AreEqual(new Vector4(1.75f, 1f, 0f, 0f), Shader.GetGlobalVector(ArenaShaderGlobals.RippleLookId), "ring width, running");
                Vector4[] handed = Shader.GetGlobalVectorArray(ArenaShaderGlobals.RippleId);
                Assert.AreEqual(4, handed.Length, "a fixed array of four");
                Assert.AreEqual(shape[3], handed[3]);
                Assert.AreEqual(colour[3], Shader.GetGlobalVectorArray(ArenaShaderGlobals.RippleColourId)[3]);
                ArenaShaderGlobals.ClearOwnerTiles();
                Assert.AreEqual(0f, Shader.GetGlobalVector(ArenaShaderGlobals.RippleLookId).y, "a plain arena (lobby, no room) runs no ripple");
            }
            finally { ArenaShaderGlobals.ClearOwnerTiles(); }
        }

        [Test]
        public void TheEditorHookPushesTheLookAndTheArenaCentre()
        {
            System.Reflection.MethodInfo pushAll = typeof(ArenaLookEditorGlobals).GetMethod(nameof(ArenaLookEditorGlobals.PushAll));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(ArenaLookEditorGlobals), "PushAll",
                typeof(ArenaShaderGlobals).GetMethod(nameof(ArenaShaderGlobals.Push))), "the Scene view shows the look");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(ArenaLookEditorGlobals), "PushAll",
                typeof(ArenaShaderGlobals).GetMethod(nameof(ArenaShaderGlobals.SetGridOrigin))), "the grid turns about the arena's centre");
            Assert.IsTrue(IlWiring.HandsOver(typeof(ArenaLookEditorGlobals), ".cctor", pushAll), "after a domain reload (delayCall)");
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaLookEditorGlobals), ".cctor", pushAll), "after a scene opens");
            System.Reflection.MethodInfo back = typeof(ArenaLookEditorGlobals).GetMethod("OnPlayModeChanged",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.NotNull(back, "a Play Mode listener");
            Assert.IsTrue(IlWiring.HandsOver(typeof(ArenaLookEditorGlobals), ".cctor", back), "listens for the way back from Play Mode");
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaLookEditorGlobals), "OnPlayModeChanged", pushAll),
                "back in Edit Mode the open scene's centre is put back (Dominion's was in use)");
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaSymmetry), "OnEnable", typeof(ArenaShaderGlobals).GetMethod(nameof(ArenaShaderGlobals.SetGridOrigin))),
                "in play the arena hands its centre to the grid");
        }

        // A player has no Editor hook: the look reaches the shader only when the asset loads (OnEnable), and an Inspector edit
        // shows at once (OnValidate). Without the first a build silently draws the old Lit look.
        [Test]
        public void TheAssetPushesItselfOnLoadAndOnEveryEdit()
        {
            System.Reflection.MethodInfo push = typeof(ArenaShaderGlobals).GetMethod(nameof(ArenaShaderGlobals.Push));
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaLook), "OnEnable", push), "a player pushes the look when the asset loads");
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaLook), "OnValidate", push), "an Inspector edit shows at once");
        }

        private static readonly float[] Gloss = { 0.81f, 0.72f, 0.63f, 0.54f };
        private static readonly float[] Shine = { 0.05f, 0.15f, 0.25f, 0.35f };
        private static readonly Color[] Tint =
        {
            new Color(0.1f, 0.2f, 0.3f, 1f), new Color(0.2f, 0.3f, 0.4f, 1f), new Color(0.3f, 0.4f, 0.5f, 1f), new Color(0.4f, 0.5f, 0.6f, 1f),
        };

        private static void AreClose(Vector4 expected, Vector4 actual, string what)
        {
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(expected[i], actual[i], 1e-5f, what + " [" + i + "]");
        }

        private static ArenaLook Literal()
        {
            var look = ScriptableObject.CreateInstance<ArenaLook>();
            look.floorTileSize = 2.5f;
            look.reflectionStrength = 0.3f;
            look.floorLineWidth = 0.07f;
            look.floorGridGlow = 0.11f;
            look.ownedEdgeGlow = 1.1f;
            look.ownedFillGlow = 0.15f;
            look.stripeGlow = 1.3f;
            look.outsideStripeGlow = 0.4f;
            look.circuitCellSize = 1.25f;
            look.stripeWidth = 0.05f;
            look.nodeSize = 0.2f;
            look.topEdgeWidth = 0.09f;
            look.circuitAcrossChance = 0.6f;
            look.circuitUpChance = 0.25f;
            look.outsideLineColor = new Color(0.5f, 0.6f, 0.7f, 1f);
            look.floorSurface = new SurfaceLook(Tint[0], Gloss[0], Shine[0]);
            look.wallSurface = new SurfaceLook(Tint[1], Gloss[1], Shine[1]);
            look.blockSurface = new SurfaceLook(Tint[2], Gloss[2], Shine[2]);
            look.barrierSurface = new SurfaceLook(Tint[3], Gloss[3], Shine[3]);
            return look;
        }
    }
}
