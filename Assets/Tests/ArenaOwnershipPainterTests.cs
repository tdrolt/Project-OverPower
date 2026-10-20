using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using Overpower.Match;
using Overpower.UI;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The painter that turns every zone's ownership into the floor's owner-tile texture: it paints every tile through the tested
    /// rules, the texture covers every tile a band can reach, it repaints only when the signature rule says so, it reads the owner the
    /// minimap shows and the live radius, and it frees its texture and lets the shader go plain when it is destroyed.</summary>
    public class ArenaOwnershipPainterTests
    {
        private static readonly System.Type Painter = typeof(ArenaOwnershipPainter);
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private UiTheme theme;
        private ArenaLook look;

        [SetUp]
        public void SetUp()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            look = ScriptableObject.CreateInstance<ArenaLook>();
            look.floorTileSize = 2f;
            look.ownerBandTiles = 1f;
            theme.arenaLook = look;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(theme);
            Object.DestroyImmediate(look);
            ArenaShaderGlobals.ClearOwnerTiles();
            ArenaLookEditorGlobals.PushAll();
        }

        private static int LiveTextures() => Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t != null && t.name == ArenaOwnershipPainter.TextureName);

        private static void DestroyLikeTheGame(GameObject go)
        {
            // Edit mode never sends OnDestroy to a plain MonoBehaviour, so the test sends it the way the game's Destroy does.
            foreach (ArenaOwnershipPainter painter in go.GetComponents<ArenaOwnershipPainter>())
                Painter.GetMethod("OnDestroy", Any).Invoke(painter, null);
            Object.DestroyImmediate(go);
        }

        private ArenaOwnershipPainter NewPainter(GameObject go)
        {
            var painter = go.AddComponent<ArenaOwnershipPainter>();
            var so = new SerializedObject(painter);
            so.FindProperty("theme").objectReferenceValue = theme;
            so.ApplyModifiedPropertiesWithoutUndo();
            return painter;
        }

        private static ZoneBand[] TwoZones() => new[]
        {
            new ZoneBand(new Vector2(10f, 4f), 6f, new OwnerPaint(OwnerPaintBase.Team, 1, false, -1)),
            new ZoneBand(new Vector2(-12f, -9f), 5f, new OwnerPaint(OwnerPaintBase.Neutral, -1, false, -1)),
        };

        [Test]
        public void DestroyFreesTheTexture()
        {
            int before = LiveTextures();
            var go = new GameObject("painter test");
            try
            {
                ArenaOwnershipPainter painter = NewPainter(go);
                painter.PaintBands(TwoZones(), new Vector2(1f, 1f));
                Assert.AreEqual(before + 1, LiveTextures(), "the painter makes one texture of its own");
                Texture tiles = Shader.GetGlobalTexture(ArenaShaderGlobals.OwnerTexId);
                Assert.AreEqual(ArenaOwnershipPainter.TextureName, tiles.name);
                Assert.AreEqual(FilterMode.Point, tiles.filterMode, "one texel per tile, never blended with its neighbour");
                Assert.IsTrue((tiles.hideFlags & HideFlags.DontSave) != 0, "never saved into a scene");
                Assert.AreEqual(1f, Shader.GetGlobalVector(ArenaShaderGlobals.OwnerGridId).w, "painted tiles are bound");
                painter.PaintBands(TwoZones(), new Vector2(1f, 1f));
                Assert.AreEqual(before + 1, LiveTextures(), "a repaint reuses the texture");
            }
            finally
            {
                DestroyLikeTheGame(go);
            }
            Assert.AreEqual(before, LiveTextures(), "the texture leaked past its painter");
            Assert.AreEqual(0f, Shader.GetGlobalVector(ArenaShaderGlobals.OwnerGridId).w, "with the painter gone the arena draws plain");
        }

        [Test]
        public void EveryTileIsPaintedThroughTheRulesAndTheTextureCoversEveryBand()
        {
            ZoneBand[] zones = TwoZones();
            var origin = new Vector2(1f, 1f);
            float size = look.floorTileSize, band = look.ownerBandTiles * size;
            ArenaOwnershipPainter.TileRange(zones, band, origin, size, out Vector2Int min, out Vector2Int extent);
            var pixels = new Color32[extent.x * extent.y];
            ArenaOwnershipPainter.PaintTiles(pixels, min, extent, origin, size, zones, band, look, theme, null);

            int owned = 0, neutral = 0;
            for (int r = min.y - 3; r < min.y + extent.y + 3; r++)
                for (int q = min.x - 3; q < min.x + extent.x + 3; q++)
                {
                    var axial = new Vector2Int(q, r);
                    TileLook expected = FloorTileRule.LookOf(HexGrid.CentreOf(axial, origin, size), zones, band, look, theme);
                    Vector2Int texel = HexGrid.Texel(axial, min);
                    bool inside = texel.x >= 0 && texel.y >= 0 && texel.x < extent.x && texel.y < extent.y;
                    if (!inside)
                    {
                        Assert.AreEqual(TileState.Plain, expected.State, $"tile {axial} is in a band but outside the texture");
                        continue;
                    }
                    Color32 got = pixels[texel.y * extent.x + texel.x];
                    Assert.AreEqual(OwnerTileTexel.Encode(expected), got, $"tile {axial}");
                    if (expected.State == TileState.Owned) owned++;
                    if (expected.State == TileState.Neutral) neutral++;
                }
            Assert.Greater(owned, 20, "the owned zone paints its tiles");
            Assert.Greater(neutral, 20, "the neutral zone paints its tiles");

            // With an arena outline whose wall runs through the owned zone, the floor past it stays plain: the tiles under the wall
            // are kept for the wall alone, and past those nothing is painted.
            ArenaBounds arena = ArenaBounds.FromPolygon(new[] { new Vector2(-40f, -40f), new Vector2(-40f, 40f), new Vector2(12f, 40f), new Vector2(12f, -40f) });
            var mask = new TileReach[pixels.Length];
            ArenaOwnershipPainter.ArenaMask(mask, min, extent, origin, size, arena);
            ArenaOwnershipPainter.PaintTiles(pixels, min, extent, origin, size, zones, band, look, theme, mask);
            int wallsOnly = 0, cut = 0;
            for (int y = 0; y < extent.y; y++)
                for (int x = 0; x < extent.x; x++)
                {
                    Vector2 centre = HexGrid.CentreOf(new Vector2Int(min.x + x, min.y + y), origin, size);
                    TileReach reach = FloorTileRule.ReachOf(arena, centre, size);
                    Assert.AreEqual(reach, mask[y * extent.x + x], $"tile {x},{y}: the mask is the rule's");
                    TileLook expected = FloorTileRule.LookAt(centre, zones, band, look, theme, reach);
                    Assert.AreEqual(OwnerTileTexel.Encode(expected), pixels[y * extent.x + x], $"tile {x},{y} with the arena outline");
                    if (expected.State != TileState.Plain && expected.WallsOnly) wallsOnly++;
                    if (reach == TileReach.None && FloorTileRule.LookOf(centre, zones, band, look, theme).State != TileState.Plain) cut++;
                }
            Assert.Greater(wallsOnly, 3, "the outline really kept some of the band for the wall alone");
            Assert.Greater(cut, 3, "and cut some of it off");
        }

        [Test]
        public void TheGameCallsTheTestedRules()
        {
            MethodInfo lookAt = typeof(FloorTileRule).GetMethod(nameof(FloorTileRule.LookAt));
            MethodInfo encode = typeof(OwnerTileTexel).GetMethod(nameof(OwnerTileTexel.Encode));
            MethodInfo paintTiles = Painter.GetMethod(nameof(ArenaOwnershipPainter.PaintTiles));
            MethodInfo paintBands = Painter.GetMethod(nameof(ArenaOwnershipPainter.PaintBands));
            MethodInfo needsRepaint = typeof(OwnershipSignature).GetMethod(nameof(OwnershipSignature.NeedsRepaint));
            MethodInfo floorPaint = typeof(FloorTileRule).GetMethod(nameof(FloorTileRule.FloorPaint));
            MethodInfo ownerShown = typeof(ZoneOwnerShown).GetMethod(nameof(ZoneOwnerShown.Of));
            MethodInfo radiusOf = typeof(BuildingManager).GetMethod(nameof(BuildingManager.CaptureRadiusOf));
            MethodInfo setTiles = typeof(ArenaShaderGlobals).GetMethod(nameof(ArenaShaderGlobals.SetOwnerTiles));
            MethodInfo clear = typeof(ArenaShaderGlobals).GetMethod(nameof(ArenaShaderGlobals.ClearOwnerTiles));

            // Every check is reported, so one run with the wiring reverted shows each of them failing.
            var missing = new System.Collections.Generic.List<string>();
            void Check(bool ok, string what) { if (!ok) missing.Add(what); }
            Check(IlWiring.Uses(Painter, "PaintTiles", lookAt), "each tile's look comes from FloorTileRule.LookAt");
            Check(IlWiring.Uses(Painter, "PaintTiles", encode), "and is stored through OwnerTileTexel.Encode");
            Check(IlWiring.Uses(Painter, "ArenaMask", typeof(FloorTileRule).GetMethod(nameof(FloorTileRule.ReachOf))),
                "the ground past the arena wall stays plain");
            Check(IlWiring.Uses(Painter, "PaintBands", Painter.GetMethod(nameof(ArenaOwnershipPainter.ArenaMask))), "the arena mask is built when painting");
            Check(IlWiring.Uses(Painter, "PaintBands", typeof(ArenaSymmetry).GetProperty(nameof(ArenaSymmetry.FullBounds)).GetGetMethod()),
                "the whole arena's outline, so a cut corner keeps its out-of-play paint");
            Check(IlWiring.Uses(Painter, "PaintBands", paintTiles), "painting goes through PaintTiles");
            Check(IlWiring.Uses(Painter, "PaintBands", setTiles), "the painted texture is handed to the shader");
            Check(IlWiring.Uses(Painter, "Repaint", floorPaint), "each zone's paint comes from FloorTileRule.FloorPaint");
            Check(IlWiring.Uses(Painter, "Repaint", paintBands), "a repaint paints the bands");
            Check(IlWiring.ResultDecidesABranch(Painter, "LateUpdate", needsRepaint), "repaint only when the signature rule says so");
            Check(IlWiring.Uses(Painter, "ReadZones", ownerShown), "the owner the minimap shows");
            Check(IlWiring.Uses(Painter, "ReadZones", radiusOf), "the live capture radius");
            Check(IlWiring.Uses(Painter, "ReadZones", typeof(BuildingManager).GetMethod(nameof(BuildingManager.IsSpawnZone))), "whether the zone is a spawn");
            Check(IlWiring.Uses(Painter, "ReadZones", typeof(MatchDirector).GetMethod(nameof(MatchDirector.IsOutOfPlay))), "whether the zone is out of play");
            Check(IlWiring.Uses(Painter, "Repaint", typeof(TerritoryMap).GetMethod(nameof(TerritoryMap.CapitalTeamOf))), "the spawn's team");
            Check(IlWiring.ResultDecidesABranch(Painter, "LateUpdate", typeof(OwnershipSignature).GetMethod(nameof(OwnershipSignature.ShowsOwnerTiles))),
                "the room's lobby (and no room) draws the arena plain");
            Check(IlWiring.Uses(Painter, "LateUpdate", typeof(MatchDirector).GetProperty(nameof(MatchDirector.IsLive)).GetGetMethod()), "the match being live");
            Check(IlWiring.Uses(Painter, "LateUpdate", typeof(MatchDirector).GetProperty(nameof(MatchDirector.LobbyStageNow)).GetGetMethod()), "the room's lobby stage");
            Check(IlWiring.Uses(Painter, "LateUpdate", clear), "while plain the arena's owner tiles are cleared");
            Check(IlWiring.Uses(Painter, "LateUpdate", typeof(OwnershipSignature).GetMethod(nameof(OwnershipSignature.Forget))),
                "and the painted state forgotten, so the next room paints on its first frame");
            Check(IlWiring.Uses(Painter, "LateUpdate", typeof(FloorTileRule).GetMethod(nameof(FloorTileRule.PaintLookKey))),
                "a change of the look's numbers or colours repaints");
            Check(IlWiring.Uses(typeof(BuildingManager), "CaptureRadiusOf",
                typeof(BuildingCapture).GetProperty(nameof(BuildingCapture.CaptureRadius)).GetGetMethod()), "the tower's live radius, never a copy");
            Assert.IsEmpty(missing, string.Join("; ", missing));
        }

        [Test]
        public void OwnershipAndLiveEventsMarkARepaint()
        {
            MethodInfo onOwnership = Painter.GetMethod("HandleOwnershipChanged", Any);
            MethodInfo onLive = Painter.GetMethod("HandleLiveStateChanged", Any);
            Assert.IsNotNull(onOwnership);
            Assert.IsNotNull(onLive);
            var missing = new System.Collections.Generic.List<string>();
            void Check(bool ok, string what) { if (!ok) missing.Add(what); }
            // The += itself, through the event's add accessor (a HandsOver of the handler would also pass on the -= alone).
            Check(IlWiring.Uses(Painter, "Hook", typeof(BuildingManager).GetEvent(nameof(BuildingManager.OwnershipChanged)).GetAddMethod()),
                "subscribes to OwnershipChanged");
            Check(IlWiring.Uses(Painter, "Hook", typeof(MatchDirector).GetEvent(nameof(MatchDirector.LiveStateChanged)).GetAddMethod()),
                "subscribes to LiveStateChanged");
            FieldInfo marked = Painter.GetField("marked", Any);
            Check(IlWiring.StoresBool(Painter, "HandleOwnershipChanged", marked, true), "an ownership event marks a repaint");
            Check(IlWiring.StoresBool(Painter, "HandleLiveStateChanged", marked, true), "a live-state event marks a repaint");
            Assert.IsEmpty(missing, string.Join("; ", missing));
        }
    }

    /// <summary>The painter rides on MatchSystems, which both arena scenes hold, so neither scene file changes.</summary>
    public class ArenaOwnershipPainterPrefabTests
    {
        [Test]
        public void MatchSystemsCarriesPainterWithTheme()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Systems/MatchSystems.prefab");
            Assert.IsNotNull(prefab);
            ArenaOwnershipPainter[] painters = prefab.GetComponentsInChildren<ArenaOwnershipPainter>(true);
            Assert.AreEqual(1, painters.Length, "exactly one painter");
            Assert.IsTrue(painters[0].enabled);
            var theme = new SerializedObject(painters[0]).FindProperty("theme").objectReferenceValue as UiTheme;
            Assert.IsNotNull(theme, "the painter has a theme");
            Assert.AreEqual("Assets/Gameplay/Config/UiTheme.asset", AssetDatabase.GetAssetPath(theme));
            Assert.IsNotNull(theme.arenaLook, "and the theme reaches the arena look (tile size and band)");
        }
    }
}
