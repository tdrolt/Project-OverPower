using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Match;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>A health pack is a flat hexagon pad on the floor, exactly as wide as the pickup reach, with a green cross floating over it:
    /// bright while ready, dim while it waits to come back, in the healing green either way. Nothing of it is solid, and its meshes go
    /// with it. Literal numbers, never the real asset's tuning.</summary>
    public class HealthPackLookTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Color Green = new Color(0.31f, 0.92f, 0.33f, 1f);

        private UiTheme theme;
        private ArenaLook look;
        private HealthPackConfig config;
        private Material fx, solid;
        private GameObject parent;

        private static int LivePackMeshes() => Resources.FindObjectsOfTypeAll<Mesh>()
            .Count(m => m != null && (m.name == HealthPackView.PadMeshName || m.name == HealthPackView.EdgeMeshName));

        [SetUp]
        public void SetUp()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.healGreenColor = Green;
            look = ScriptableObject.CreateInstance<ArenaLook>();
            fx = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            solid = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            look.fxAlpha = fx;
            theme.arenaLook = look;
            config = ScriptableObject.CreateInstance<HealthPackConfig>();
            var so = new SerializedObject(config);
            so.FindProperty("pickupRadius").floatValue = 1.3f;
            so.FindProperty("padThicknessMetres").floatValue = 0.1f;
            so.FindProperty("padEdgeWidthMetres").floatValue = 0.1f;
            so.FindProperty("readyGlow").floatValue = 1.5f;
            so.FindProperty("respawningBrightness").floatValue = 0.25f;
            so.FindProperty("floatHeightMetres").floatValue = 1f;
            so.FindProperty("bobHeightMetres").floatValue = 0.2f;
            so.FindProperty("bobCyclesPerSecond").floatValue = 0.5f; // 0.4 s is a fifth of a bob, never a whole one
            so.FindProperty("spinDegreesPerSecond").floatValue = 50f;
            so.ApplyModifiedPropertiesWithoutUndo();
            parent = new GameObject("HealthPackLookTests") { hideFlags = HideFlags.HideAndDontSave };
        }

        private static void DestroyLikeTheGame(GameObject go)
        {
            foreach (HealthPackView view in go.GetComponentsInChildren<HealthPackView>(true))
                typeof(HealthPackView).GetMethod("OnDestroy", Any).Invoke(view, null);
            Object.DestroyImmediate(go);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyLikeTheGame(parent);
            Object.DestroyImmediate(fx);
            Object.DestroyImmediate(solid);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        private HealthPackView Make() => HealthPackView.Create(parent.transform, new Vector3(5f, 0.5f, -2f), "Pack", config, theme, solid);

        [Test]
        public void ReadyGlowsGreenAndWaitingIsTheSameGreenDimmed()
        {
            Color ready = HealthPackLook.CrossColour(true, Green, 1.5f, 0.25f);
            Color waiting = HealthPackLook.CrossColour(false, Green, 1.5f, 0.25f);
            Assert.AreEqual(Green.g * 1.5f, ready.g, 1e-4f, "ready: the healing green times Ready Glow");
            Assert.AreEqual(Green.r * 1.5f, ready.r, 1e-4f);
            Assert.AreEqual(Green.g * 0.25f, waiting.g, 1e-4f, "waiting: the same green at Respawning Brightness");
            Assert.AreEqual(Green.b * 0.25f, waiting.b, 1e-4f);
            Assert.AreEqual(1f, ready.a, 1e-4f, "solid either way");
            Assert.AreEqual(1f, waiting.a, 1e-4f);
            Assert.Greater(ready.g, waiting.g, "ready is the bright one");
            Assert.AreEqual(1.5f, HealthPackLook.EdgeGlow(true, 1.5f, 0.25f), 1e-4f, "the pad's edge glows with the cross");
            Assert.AreEqual(0.25f, HealthPackLook.EdgeGlow(false, 1.5f, 0.25f), 1e-4f, "and dims with it");
        }

        [Test]
        public void ThePadIsAHexagonExactlyAsWideAsThePickupReach()
        {
            HealthPackView view = Make();
            Assert.NotNull(view);
            Assert.AreEqual(new Vector3(5f, 0.5f, -2f), view.transform.position, "on the pack's ground point");
            MeshFilter edge = view.GetComponentsInChildren<MeshFilter>(true).Single(f => f.sharedMesh != null && f.sharedMesh.name == HealthPackView.EdgeMeshName);
            Vector3[] v = edge.sharedMesh.vertices;
            for (int k = 0; k < 6; k++)
            {
                Vector3 corner = edge.transform.TransformPoint(v[k]) - view.transform.position;
                Assert.AreEqual(1.3f, new Vector2(corner.x, corner.z).magnitude, 1e-3f, $"corner {k} at the pickup radius: standing anywhere on the pad takes it");
                Assert.AreEqual(0.1f, corner.y, 0.02f, "the glowing edge lies on top of the pad");
            }

            MeshFilter pad = view.GetComponentsInChildren<MeshFilter>(true).Single(f => f.sharedMesh != null && f.sharedMesh.name == HealthPackView.PadMeshName);
            float reach = pad.sharedMesh.vertices.Max(p =>
            {
                Vector3 w = pad.transform.TransformPoint(p) - view.transform.position;
                return new Vector2(w.x, w.z).magnitude;
            });
            Assert.AreEqual(1.3f, reach, 1e-3f, "the pad's own corners reach exactly the pickup radius, like its edge");
            Bounds b = pad.GetComponent<Renderer>().bounds;
            Assert.AreEqual(0.5f, b.min.y, 1e-3f, "the pad stands on the ground");
            Assert.AreEqual(0.6f, b.max.y, 1e-3f, "Pad Thickness high");
            Assert.AreEqual(0, parent.GetComponentsInChildren<Collider>(true).Length, "nothing of a pack is solid");
        }

        [Test]
        public void ReadyAndWaitingRecolourTheCrossAndTheEdge()
        {
            HealthPackView view = Make();
            var block = new MaterialPropertyBlock();
            Renderer[] cross = view.CrossRenderers;
            Assert.AreEqual(2, cross.Length, "a plus of two bars");
            view.SetReady(true);
            Assert.IsTrue(view.ShownReady);
            cross[0].GetPropertyBlock(block);
            float readyG = block.GetColor("_BaseColor").g;
            view.EdgeRenderer.GetPropertyBlock(block);
            float readyEdge = block.GetColor("_BaseColor").g;
            view.SetReady(false);
            Assert.IsFalse(view.ShownReady);
            cross[1].GetPropertyBlock(block);
            Color waiting = block.GetColor("_BaseColor");
            view.EdgeRenderer.GetPropertyBlock(block);
            float waitingEdge = block.GetColor("_BaseColor").g;
            Assert.Greater(readyG, waiting.g * 2f, "the cross is clearly dimmer while it waits");
            Assert.Greater(waiting.g, 0f, "but still there");
            Assert.Greater(waiting.g, waiting.r * 2f, "still green");
            Assert.Greater(readyEdge, waitingEdge, "the edge dims with it");
        }

        [Test]
        public void TheCrossFloatsTurnsAndBobs()
        {
            HealthPackView view = Make();
            view.Animate(0f);
            Transform cross = view.Cross;
            Vector3 a = cross.position;
            Quaternion ra = cross.rotation;
            view.Animate(0.4f);
            Assert.Greater(cross.position.y, view.transform.position.y + 0.5f, "floating above the pad");
            Assert.AreNotEqual(a.y, cross.position.y, "bobbing");
            Assert.Greater(Quaternion.Angle(ra, cross.rotation), 1f, "turning");
        }

        [Test]
        public void WithoutAThemeThePackIsStillThere()
        {
            HealthPackView view = HealthPackView.Create(parent.transform, Vector3.zero, "Pack", config, null, solid);
            Assert.NotNull(view, "a pack hidden by a missing theme would hide a rule from the players");
            Assert.AreEqual(2, view.CrossRenderers.Length, "the cross is still drawn");
            Assert.IsNull(view.EdgeRenderer, "only the green edge needs the theme's Arena Look");
            var block = new MaterialPropertyBlock();
            view.CrossRenderers[0].GetPropertyBlock(block);
            Color c = block.GetColor("_BaseColor");
            Assert.Greater(c.r + c.g + c.b, 0.5f, "not a black cross");
        }

        [Test]
        public void TheLookFieldsAreInTheYamlWithATooltip()
        {
            const string path = "Assets/_Project/Design/Settings/HealthPackConfig.asset";
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<HealthPackConfig>(path), path);
            string yaml = System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
            foreach (string name in new[] { "takenColour", "readyGlow", "respawningBrightness", "padColour", "padThicknessMetres",
                         "padEdgeWidthMetres", "padFillAlpha" })
            {
                FieldInfo f = typeof(HealthPackConfig).GetField(name, Any);
                Assert.NotNull(f, name);
                var tip = f.GetCustomAttribute<TooltipAttribute>();
                Assert.NotNull(tip, name + " has no tooltip");
                Assert.Greater(tip.tooltip.Length, 40, name + " tooltip is not a full sentence");
                StringAssert.EndsWith(".", tip.tooltip.TrimEnd(), name + " tooltip does not end like a sentence");
                StringAssert.Contains("\n  " + name + ":", yaml, name + " is not in HealthPackConfig.asset");
            }
        }

        [Test]
        public void TheMatchPacksTakeTheirGreenFromTheTheme()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Match/Prefabs/Match Systems.prefab");
            HealthPackManager manager = prefab.GetComponentInChildren<HealthPackManager>(true);
            Assert.NotNull(manager, "MatchSystems has the health pack manager");
            Object assigned = new SerializedObject(manager).FindProperty("theme").objectReferenceValue;
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Project/Design/Settings/UiTheme.asset"), assigned,
                           "without the theme the packs lose their green and their glowing edge");
        }

        [Test]
        public void DestroyFreesTheMeshes()
        {
            int before = LivePackMeshes();
            HealthPackView view = Make();
            Assert.AreEqual(before + 2, LivePackMeshes(), "the pad and its edge");
            DestroyLikeTheGame(view.gameObject);
            Assert.AreEqual(before, LivePackMeshes(), "a pack's meshes leaked past it");
        }

        [Test]
        public void TheGameDrawsPacksThroughTheView()
        {
            var failed = new System.Collections.Generic.List<string>();
            void Check(bool ok, string what) { if (!ok) failed.Add(what); }
            System.Type manager = typeof(HealthPackManager);
            Check(IlWiring.Uses(manager, "Build", typeof(HealthPackView).GetMethod(nameof(HealthPackView.Create))), "every pack is built as a HealthPackView");
            Check(IlWiring.Uses(manager, "Show", typeof(HealthPackView).GetMethod(nameof(HealthPackView.SetReady))), "ready or waiting is handed to the view");
            Check(IlWiring.Uses(manager, "Update", typeof(HealthPackView).GetMethod(nameof(HealthPackView.Animate))), "the cross turns and bobs every frame");
            Check(IlWiring.Uses(typeof(HealthPackView), "Paint", typeof(HealthPackLook).GetMethod(nameof(HealthPackLook.CrossColour))), "the cross colour is the rule's");
            Check(IlWiring.Uses(typeof(HealthPackView), "Paint", typeof(HealthPackLook).GetMethod(nameof(HealthPackLook.EdgeGlow))), "and so is the edge's glow");
            Check(IlWiring.Uses(typeof(HealthPackView), "Create", typeof(LookColours).GetMethod(nameof(LookColours.For))), "the green is the heal family's");
            Check(IlWiring.Uses(typeof(MinimapView), "ShowPackBadge", typeof(LookColours).GetMethod(nameof(LookColours.For))),
                  "a ready pack is the same healing green on the minimap");
            MethodInfo zoneBadge = typeof(MinimapView).GetMethod("UpdatePackBadge", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo showBadge = typeof(MinimapView).GetMethod("ShowPackBadge", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(zoneBadge != null && IlWiring.Uses(typeof(MinimapView), "UpdateZones", zoneBadge), "every zone's pack gets a badge");
            Check(showBadge != null && IlWiring.Uses(typeof(MinimapView), "UpdatePackBadge", showBadge), "drawn the one way every pack's badge is");
            Assert.IsEmpty(failed, string.Join("; ", failed));
        }
    }
}
