using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Match;
using Overpower.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>The eight-sided prism a capture tower is built from: it replaces the unit cylinder one for one.</summary>
    public class OctagonPrismMeshTests
    {
        private static readonly System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        [Test]
        public void TheOctagonHasEightCornersTheFirstFacingFront()
        {
            Mesh mesh = OctagonPrismMesh.Create();
            try
            {
                float[] corners = mesh.vertices
                    .Where(v => Mathf.Abs(v.y - 1f) < 1e-4f && new Vector2(v.x, v.z).magnitude > 0.99f)
                    .Select(v => Mathf.Round(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg))
                    .Select(a => a < 0f ? a + 360f : a)
                    .Distinct().OrderBy(a => a).ToArray();
                CollectionAssert.AreEqual(new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f }, corners);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void TheOctagonFitsTheUnitCylinderItReplaces()
        {
            Mesh mesh = OctagonPrismMesh.Create();
            try
            {
                Assert.AreEqual(OctagonPrismMesh.VertexCount, mesh.vertexCount);
                foreach (Vector3 v in mesh.vertices)
                {
                    Assert.LessOrEqual(new Vector2(v.x, v.z).magnitude, 1f + 1e-4f);
                    Assert.LessOrEqual(Mathf.Abs(v.y), 1f + 1e-4f);
                }
                Assert.AreEqual(Vector3.zero, mesh.bounds.center);
                Assert.AreEqual(1f, mesh.bounds.extents.x, 1e-4f, "corners at 90 and 270 degrees reach the cylinder's side");
                Assert.AreEqual(1f, mesh.bounds.extents.y, 1e-4f);
                Assert.AreEqual(1f, mesh.bounds.extents.z, 1e-4f, "the first corner faces front");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void EveryFaceOfTheOctagonIsFlatAndFacesOutward()
        {
            Mesh mesh = OctagonPrismMesh.Create();
            try
            {
                Vector3[] v = mesh.vertices;
                int[] t = mesh.triangles;
                Vector3[] n = mesh.normals;
                Assert.AreEqual(v.Length, n.Length);
                Assert.AreEqual(4 * OctagonPrismMesh.Sides, t.Length / 3, "two per side and one per side on each cap");
                Assert.Less(t.Length / 3, 300);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 centroid = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    Vector3 geometric = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    Assert.Greater(Vector3.Dot(geometric, centroid), 0f, "triangle " + i / 3 + " is wound inward");
                    for (int k = 0; k < 3; k++)
                        Assert.AreEqual(1f, Vector3.Dot(n[t[i + k]], geometric.normalized), 1e-4f, "triangle " + i / 3 + " is not flat-shaded");
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void ItsNameNeverCollidesWithTheHexMesh()
        {
            Mesh octagon = OctagonPrismMesh.Create();
            Mesh hex = HexPrismMesh.Create();
            try
            {
                Assert.AreEqual(OctagonPrismMesh.MeshName, octagon.name);
                Assert.AreNotEqual(HexPrismMesh.MeshName, octagon.name);
                Assert.AreNotEqual(0, (int)(octagon.hideFlags & HideFlags.DontSave));
                Assert.AreEqual(HexPrismMesh.VertexCount, hex.vertexCount, "the spawn's hexagon is unchanged");
            }
            finally { Object.DestroyImmediate(octagon); Object.DestroyImmediate(hex); }
        }

        [Test]
        public void TheSharedOctagonMeshIsFreedWhenTheGameStarts()
        {
            var hook = typeof(TowerLook).GetMethod("FreeSharedMesh", Any);
            Assert.IsNotNull(hook);
            Mesh orphan = OctagonPrismMesh.Create();
            hook.Invoke(null, null);
            Assert.IsTrue(orphan == null, "an octagon mesh left over from an earlier session is destroyed");
            Assert.IsNull(typeof(TowerLook).GetField("octagonMesh", Any).GetValue(null));
            int left = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name == OctagonPrismMesh.MeshName && !EditorUtility.IsPersistent(m));
            Assert.AreEqual(0, left, "no runtime octagon mesh is left, counted by name");
        }
    }

    /// <summary>A glow brightens a colour without changing its hue on screen, in light and under the fog (a tower hides its own top
    /// from sight, so a crown is always under it). The screen here is the project's: the colour made linear, the fog blended over it,
    /// then URP's Neutral tonemapper; fed today's crown colours it gives the crown pixels captured in the Game view to within 3 levels.</summary>
    public class TowerGlowTests
    {
        private static readonly Color Orange = new Color(1f, 0.45f, 0.05f, 1f);
        private static readonly Color Purple = new Color(0.68f, 0.32f, 1f, 1f);
        private static readonly Color Cyan = new Color(0.15f, 0.95f, 1f, 1f);
        private static readonly Color StunBlue = new Color(0.2f, 0.45f, 1f, 1f);
        private static readonly Color White = new Color(0.93f, 0.97f, 1f, 1f);
        private static readonly Color Red = new Color(1f, 0.2f, 0.15f, 0.8f);
        private static readonly Color FogColour = new Color(0.102f, 0.11f, 0.133f, 1f);
        private static readonly float[] FogDarkness = { 0f, 0.35f, 0.65f };
        private const float HueToleranceDegrees = 8f;

        // URP Colors.hlsl NeutralTonemap, one channel of a linear colour.
        private static float NeutralCurve(float x) =>
            (x * (0.2f * x + 0.24f * 0.29f) + 0.272f * 0.02f) / (x * (0.2f * x + 0.29f) + 0.272f * 0.3f) - 0.02f / 0.3f;

        private static float Neutral(float x)
        {
            float whiteScale = 1f / NeutralCurve(5.3f);
            return Mathf.Min(1f, NeutralCurve(x * whiteScale) * whiteScale);
        }

        private static Color OnScreen(Color colour, float fogDarkness)
        {
            Color c = colour.linear, fog = FogColour.linear;
            float Channel(float value, float fogValue) => Mathf.LinearToGammaSpace(Neutral(Mathf.Lerp(value, fogValue, fogDarkness)));
            return new Color(Channel(c.r, fog.r), Channel(c.g, fog.g), Channel(c.b, fog.b), 1f);
        }

        private static float Hue(Color c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h * 360f;
        }

        [Test]
        public void TheScreenModelGivesTheCapturedCrowns()
        {
            AssertPixels(new Color(1.5f, 0.45f, 0.05f), 201, 75, 29);
            AssertPixels(new Color(0.68f, 0.32f, 1.5f), 110, 55, 201);
            AssertPixels(new Color(0.15f, 1.425f, 1.5f), 32, 195, 201);

            void AssertPixels(Color crown, int r, int g, int b)
            {
                Color shown = OnScreen(crown, 0.65f);
                Assert.AreEqual(r, shown.r * 255f, 3f, crown + " red");
                Assert.AreEqual(g, shown.g * 255f, 3f, crown + " green");
                Assert.AreEqual(b, shown.b * 255f, 3f, crown + " blue");
            }
        }

        [Test]
        public void EachTeamsGlowKeepsItsHueOnScreenInLightAndUnderTheFog()
        {
            var teamGlows = new[] { 1.2f, 1.5f, 2f, 2.5f };
            var cases = new (string name, Color colour, float[] glows)[]
            {
                ("orange", Orange, teamGlows), ("purple", Purple, teamGlows), ("cyan", Cyan, teamGlows),
                ("stun blue", StunBlue, new[] { 2f, 3f, 4f, 6f }),
            };
            foreach ((string name, Color colour, float[] glows) in cases)
                foreach (float glow in glows)
                    foreach (float fog in FogDarkness)
                    {
                        Color shown = OnScreen(TowerLookRules.GlowKeepingHue(colour, glow), fog);
                        Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(Hue(colour), Hue(shown))), HueToleranceDegrees,
                            $"{name} glowing x{glow} under fog {fog} shows as {shown}");
                    }
        }

        // The tonemapper squeezes every bright channel toward the same level: if only white's blue glowed, its red and green would fall
        // behind and the crown would read lavender (cyan's green: sky blue). A channel close to the brightest glows with it.
        [Test]
        public void WhiteGlowsWhiteAndCyanKeepsItsGreen()
        {
            foreach (float glow in new[] { 1.5f, 2.5f })
            {
                Color white = TowerLookRules.GlowKeepingHue(White, glow);
                Assert.AreEqual(White.r / White.b, white.r / white.b, 1e-4f, "white's red grows with its blue, x" + glow);
                Assert.AreEqual(White.g / White.b, white.g / white.b, 1e-4f, "white's green grows with its blue, x" + glow);
                Color cyan = TowerLookRules.GlowKeepingHue(Cyan, glow);
                Assert.AreEqual(Cyan.g / Cyan.b, cyan.g / cyan.b, 1e-4f, "cyan's green grows with its blue, x" + glow);
            }
        }

        [Test]
        public void TheBrightestChannelGlowsPastFullStrengthAndAWeakerOneLess()
        {
            Color purple = TowerLookRules.GlowKeepingHue(Purple, 1.5f);
            Assert.AreEqual(1.5f, purple.b, 1e-4f, "past 1, so the bloom picks it up");
            Assert.Greater(purple.r, Purple.r, "red grows too, or the fog would turn purple blue");
            Assert.Less(purple.r / purple.b, Purple.r / Purple.b, "but less than blue, or the light would turn it pink");
            Assert.Greater(TowerLookRules.GlowKeepingHue(White, 1.5f).maxColorComponent, 1f);
        }

        [Test]
        public void ADimColourGlowsWholeWhileItStaysUnderFullStrength()
        {
            var dim = new Color(0.3f, 0.2f, 0.4f, 1f);
            Color lit = TowerLookRules.GlowKeepingHue(dim, 2f);
            Assert.AreEqual(0.6f, lit.r, 1e-4f);
            Assert.AreEqual(0.4f, lit.g, 1e-4f);
            Assert.AreEqual(0.8f, lit.b, 1e-4f);
            Color dimmed = TowerLookRules.GlowKeepingHue(Orange, 0.5f);
            Assert.AreEqual(Orange.g * 0.5f, dimmed.g, 1e-4f, "a glow under 1 dims the whole colour");
        }

        [Test]
        public void NoGlowLeavesTheColourAsItIs()
        {
            Assert.AreEqual(Red, TowerLookRules.GlowKeepingHue(Red, 1f));
            Assert.AreEqual(0.8f, TowerLookRules.GlowKeepingHue(Red, 2f).a, "the glow never touches alpha");
            Assert.AreEqual(Color.black, TowerLookRules.GlowKeepingHue(Color.black, 2f));
        }

        // Every glow field is [Min(0)], so a designer can set one to 0 to switch that glow off; a NaN glow would spread as blocks under the Bloom.
        [Test]
        public void AGlowOfZeroSwitchesTheGlowOff()
        {
            Assert.AreEqual(new Color(0f, 0f, 0f, 0.8f), TowerLookRules.GlowKeepingHue(Red, 0f), "no glow is black, alpha kept");
            Assert.AreEqual(new Color(0f, 0f, 0f, 1f), TowerLookRules.GlowKeepingHue(Orange, 0f));
            foreach (float glow in new[] { 0f, 0.001f })
            {
                Color lit = TowerLookRules.GlowKeepingHue(Orange, glow);
                Assert.IsFalse(float.IsNaN(lit.r) || float.IsNaN(lit.g) || float.IsNaN(lit.b), $"glow {glow} gives {lit}");
            }
        }
    }

    /// <summary>A capture tower (every zone a team can take) is an eight-sided tower: a dark arena-wall body and dark columns,
    /// with the crown, the base and the column caps glowing in the colour the ring's state gives. A Dominion spawn keeps its
    /// own six-sided solid look, unchanged.</summary>
    public class TowerZoneLookTests
    {
        private const string PrefabPath = "Assets/_Project/Features/Arena/Tower Look.prefab";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Scene scene;
        private GameObject instance;
        private TowerLook look;
        private UiTheme theme;
        private const float TestInset = 0.5f;

        // The test's own theme and look with literal colours and numbers, so no retune of Tudor's assets can break the arithmetic and no
        // test value can be saved into them. Only the tower prefab and the arena-surface material are real (structure).
        [SetUp]
        public void SetUp()
        {
            Material arenaWall = AssetDatabase.LoadAssetAtPath<ArenaLook>("Assets/_Project/Design/Settings/ArenaLook.asset").towerBody;
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.teamShotColors = new[] { new Color(0.93f, 0.97f, 1f, 1f), new Color(0.68f, 0.32f, 1f, 1f), new Color(0.15f, 0.95f, 1f, 1f) };
            theme.captureRingWarningColor = new Color(1f, 0.2f, 0.15f, 1f);
            theme.captureRingPulseSpeed = 1f;
            theme.captureRingPulseDip = 0.35f;
            theme.towerNeutralColor = new Color(0.3f, 0.32f, 0.35f, 1f);
            theme.towerOwnerGlow = 2f;
            theme.arenaLook = ScriptableObject.CreateInstance<ArenaLook>();
            theme.arenaLook.towerCrownInset = TestInset;
            theme.arenaLook.towerBody = arenaWall;
            scene = EditorSceneManager.NewPreviewScene();
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            SceneManager.MoveGameObjectToScene(instance, scene);
            look = instance.GetComponent<TowerLook>();
            look.ApplyColumns(4);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(theme.arenaLook);
            Object.DestroyImmediate(theme);
            EditorSceneManager.ClosePreviewScene(scene);
        }

        private IEnumerable<Renderer> GlowPieces() =>
            new[] { look.crown, look.plinth }.Concat(look.columnCaps.Where(c => c != null && c.gameObject.activeInHierarchy));

        private IEnumerable<Renderer> BodyPieces() =>
            new[] { look.drum }.Concat(look.columnShafts.Where(s => s != null && s.gameObject.activeInHierarchy));

        private static Color PaintedOn(Renderer r)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return block.GetColor(BaseColorId);
        }

        private static bool NeverPainted(Renderer r)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return block.isEmpty;
        }

        private static void AssertColour(Color expected, Color actual, string label)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, label + " r");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, label + " g");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, label + " b");
            Assert.AreEqual(expected.a, actual.a, 1e-3f, label + " a");
        }

        private Color Lit(CaptureRingState state, float time)
        {
            OwnerPaint paint = OwnerPaint.From(state);
            Color colour = OwnerPaintColours.For(paint, theme, theme.towerNeutralColor, time);
            return paint.Base == OwnerPaintBase.Team ? TowerLookRules.GlowKeepingHue(colour, theme.towerOwnerGlow) : colour;
        }

        [Test]
        public void ACapturableTowerIsEightSidedAtItsOldSizeAndFootprint()
        {
            var pieces = new[] { look.drum, look.plinth, look.crown }.Concat(BodyPieces().Skip(1)).Concat(look.columnCaps.Where(c => c.gameObject.activeInHierarchy)).ToArray();
            var before = pieces.ToDictionary(p => p, p => (p.transform.localPosition, p.transform.localScale, SpawnLookTowerTests.HorizontalReach(p, instance.transform.position)));
            var capsule = instance.GetComponent<CapsuleCollider>();
            float radius = capsule.radius, height = capsule.height;
            Vector3 centre = capsule.center;

            look.Bind(theme);

            foreach (Renderer p in pieces)
            {
                string label = p.transform.parent.name + "/" + p.name;
                Mesh mesh = p.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(OctagonPrismMesh.MeshName, mesh.name, label);
                Assert.AreEqual(OctagonPrismMesh.VertexCount, mesh.vertexCount, label);
                Assert.AreEqual(before[p].localPosition, p.transform.localPosition, label + " moved");
                Assert.AreEqual(before[p].localScale, p.transform.localScale, label + " resized");
                Assert.LessOrEqual(SpawnLookTowerTests.HorizontalReach(p, instance.transform.position), before[p].Item3 + 1e-3f, label + " reaches further than the round piece");
            }
            Assert.AreEqual(radius, capsule.radius);
            Assert.AreEqual(height, capsule.height);
            Assert.AreEqual(centre, capsule.center);
            Assert.AreEqual(1, instance.GetComponentsInChildren<Collider>(true).Length, "the visual pieces carry no collider");
        }

        [Test]
        public void TheBodyAndColumnsWearTheArenaWallLookAndOnlyTheGlowPiecesKeepTheOwnerMaterial()
        {
            look.Bind(theme);
            Material body = theme.arenaLook.towerBody;
            Assert.IsNotNull(body, "ArenaLook names the tower body's material");
            Assert.AreEqual("Overpower/Arena Surface", body.shader.name, "an arena surface, whose stripes read the owner from the ground");
            foreach (Renderer r in BodyPieces())
                Assert.AreSame(body, r.sharedMaterial, r.transform.parent.name + "/" + r.name);
            foreach (Renderer r in GlowPieces())
                Assert.AreEqual("Tower Owner", r.sharedMaterial.name, r.transform.parent.name + "/" + r.name);
        }

        [Test]
        public void AnOwnedTowerGlowsAtItsCrownBaseAndCapsAndNeverAllOver()
        {
            look.Bind(theme);
            foreach (int team in new[] { 0, 1, 2 })
            {
                var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, team, TerritoryMap.Neutral, false);
                look.Refresh(state, 0f);
                Color lit = Lit(state, 0f);
                Assert.Greater(lit.maxColorComponent, 1f, "team " + team + "'s crown glows past the bloom threshold");
                foreach (Renderer r in GlowPieces())
                    AssertColour(lit, PaintedOn(r), "team " + team + " " + r.transform.parent.name + "/" + r.name);
                foreach (Renderer r in BodyPieces())
                    Assert.IsTrue(NeverPainted(r), r.transform.parent.name + "/" + r.name + " takes the owner's glow over the whole body");
            }
        }

        [Test]
        public void ContestedRedAndTheAttackPulseReachTheCrownAndBase()
        {
            look.Bind(theme);
            var attacked = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, 1, TerritoryMap.Neutral, true);
            var seen = new List<Color>();
            foreach (float time in new[] { 0f, 0.37f, 0.81f, 1.3f })
            {
                look.Refresh(attacked, time);
                Color expected = Lit(attacked, time);
                AssertColour(expected, PaintedOn(look.crown), "crown at " + time);
                AssertColour(expected, PaintedOn(look.plinth), "base at " + time);
                seen.Add(PaintedOn(look.crown));
            }
            Assert.Greater(seen.Max(c => c.r) - seen.Min(c => c.r), 0.05f, "the attack pulse moves the crown's colour");

            var draining = new CaptureRingState(CaptureRingPhase.Draining, 0.5f, TerritoryMap.Neutral, 1, 2, false);
            look.Refresh(draining, 0.37f);
            AssertColour(Lit(draining, 0.37f), PaintedOn(look.crown), "a drain pulses the crown toward the drainer");
            foreach (Renderer r in BodyPieces())
                Assert.IsTrue(NeverPainted(r), r.name);
        }

        [Test]
        public void ANeutralTowerStaysDarkWithAnUnlitCrown()
        {
            look.Bind(theme);
            var neutral = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, TerritoryMap.Neutral, TerritoryMap.Neutral, false);
            look.Refresh(neutral, 0f);
            foreach (Renderer r in GlowPieces())
                AssertColour(theme.towerNeutralColor, PaintedOn(r), r.name);
            foreach (Renderer r in BodyPieces())
                Assert.IsTrue(NeverPainted(r), r.name);
        }

        [Test]
        public void TheSpawnLookAfterTheZoneLookIsTheSameSpawnAsBefore()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<TowerLook>();
            Mesh roundShaft = prefab.columnShafts[0].GetComponent<MeshFilter>().sharedMesh;
            Mesh roundCap = prefab.columnCaps[0].GetComponent<MeshFilter>().sharedMesh;

            look.Bind(theme);
            look.UseSpawnLook(true);
            Assert.AreEqual("Tower Stone", look.drum.sharedMaterial.name, "the spawn body is the solid team-coloured stone");
            foreach (Renderer r in new[] { look.drum, look.plinth, look.crown })
                Assert.AreEqual(HexPrismMesh.VertexCount, r.GetComponent<MeshFilter>().sharedMesh.vertexCount, r.name);
            for (int i = 0; i < look.columnSlots.Length; i++)
            {
                if (!look.columnSlots[i].gameObject.activeSelf)
                    continue;
                Assert.AreEqual("Tower Owner", look.columnShafts[i].sharedMaterial.name, "spawn shaft " + i);
                Assert.AreSame(roundShaft, look.columnShafts[i].GetComponent<MeshFilter>().sharedMesh, "spawn shaft " + i + " stays round");
                Assert.AreSame(roundCap, look.columnCaps[i].GetComponent<MeshFilter>().sharedMesh, "spawn cap " + i + " stays round");
            }

            var owned = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, 1, TerritoryMap.Neutral, false);
            look.Refresh(owned, 0f);
            Color raw = OwnerPaintColours.For(OwnerPaint.From(owned), theme, theme.towerNeutralColor, 0f);
            Color body = raw * theme.towerBodyShade;
            body.a = raw.a;
            AssertColour(body, PaintedOn(look.drum), "spawn drum");
            Color spawnLit = raw * theme.towerOwnerGlow; // a spawn keeps the plain multiplied glow, unlike a capture tower's
            spawnLit.a = raw.a;
            AssertColour(spawnLit, PaintedOn(look.columnShafts[0]), "spawn shaft");
            AssertColour(spawnLit, PaintedOn(look.crown), "spawn lid");

            look.UseSpawnLook(false);
            Assert.AreEqual(OctagonPrismMesh.MeshName, look.drum.GetComponent<MeshFilter>().sharedMesh.name);
            Assert.AreSame(theme.arenaLook.towerBody, look.drum.sharedMaterial);
            look.Refresh(owned, 0f);
            foreach (Renderer r in BodyPieces())
                Assert.IsTrue(NeverPainted(r), r.name + " keeps the spawn's paint after the zone look returns");
        }

        [Test]
        public void TheLidIsAGlowingRimAroundADarkTop()
        {
            look.Bind(theme);
            Transform insetPiece = look.crown.transform.parent.Find(TowerLook.CrownInsetName);
            Assert.IsNotNull(insetPiece, "the lid has a dark top inside its glowing rim");
            var inset = insetPiece.GetComponent<MeshRenderer>();
            Assert.IsTrue(inset.gameObject.activeInHierarchy);
            Assert.AreSame(theme.arenaLook.towerBody, inset.sharedMaterial);
            Assert.AreEqual(OctagonPrismMesh.MeshName, insetPiece.GetComponent<MeshFilter>().sharedMesh.name);
            Assert.AreEqual(look.crown.transform.localScale.x * TestInset, insetPiece.localScale.x, 1e-4f);
            Assert.AreEqual(look.crown.transform.localScale.z * TestInset, insetPiece.localScale.z, 1e-4f);
            Bounds lid = look.crown.bounds, top = inset.bounds;
            Assert.GreaterOrEqual(top.min.y, lid.max.y - 1e-4f, "it sits on the lid, so the two never fight for depth");
            Assert.LessOrEqual(top.max.y, lid.max.y + 0.05f, "a skin on the lid, not a step");
            Assert.AreEqual(0, insetPiece.GetComponentsInChildren<Collider>(true).Length);

            look.Refresh(new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, 0, TerritoryMap.Neutral, false), 0f);
            Assert.IsTrue(NeverPainted(inset), "the dark top never takes the owner's glow");

            look.UseSpawnLook(true);
            Assert.IsFalse(inset.gameObject.activeSelf, "a spawn keeps its solid lid");
            look.UseSpawnLook(false);
            Assert.IsTrue(inset.gameObject.activeSelf);
            look.Bind(theme);
            Assert.AreEqual(1, instance.GetComponentsInChildren<Transform>(true).Count(t => t.name == TowerLook.CrownInsetName), "binding again adds no second top");
        }

        [Test]
        public void WithNoInsetTheWholeLidGlows()
        {
            theme.arenaLook.towerCrownInset = 0f;
            look.Bind(theme);
            Transform insetPiece = look.crown.transform.parent.Find(TowerLook.CrownInsetName);
            Assert.IsTrue(insetPiece == null || !insetPiece.gameObject.activeInHierarchy, "no dark top on the lid");
            var owned = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, 1, TerritoryMap.Neutral, false);
            look.Refresh(owned, 0f);
            AssertColour(Lit(owned, 0f), PaintedOn(look.crown), "the lid still glows");
        }

        [Test]
        public void ATowerWithNoBodyMaterialIsStillPaintedAllOver()
        {
            theme.arenaLook.towerBody = null;
            look.Bind(theme);
            look.Refresh(new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, 1, TerritoryMap.Neutral, false), 0f);
            foreach (Renderer r in BodyPieces())
                Assert.IsFalse(NeverPainted(r), r.transform.parent.name + "/" + r.name + " left bare without a Tower Body to show the owner");
        }

        [Test]
        public void TheGameBindsEveryTowerItBuilds()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BuildingCapture), "Start", typeof(TowerLook).GetMethod(nameof(TowerLook.Bind))));
        }
    }
}
