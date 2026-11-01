using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Match;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 1 Nov: the health pack's cross and the ultimate pack's icosahedron get black lines along their edges so their shapes read (unlit,
    /// their faces are one flat colour); taking an ultimate pack shows rings rising round the player who took it (a health pack has its own
    /// sparks). Literal numbers are test values.
    /// </summary>
    public class PackLookTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void ThePlusIsTracedRoundItsOutlineNotThroughItsMiddle()
        {
            var edges = ShapeEdges.Plus(1.2f, 0.4f);
            Assert.AreEqual(36, edges.Count, "twelve round the front, twelve round the back, twelve between them");
            foreach ((Vector3 a, Vector3 b) in edges)
            {
                Vector3 mid = (a + b) * 0.5f;
                bool insideBothBars = Mathf.Abs(mid.x) < 0.2f - 1e-4f && Mathf.Abs(mid.y) < 0.2f - 1e-4f;
                Assert.IsFalse(insideBothBars && Mathf.Abs(mid.z) < 0.2f - 1e-4f, "no line runs inside the plus");
                Assert.IsFalse(Mathf.Abs(mid.x) < 0.2f - 1e-4f && Mathf.Abs(mid.y) < 0.2f - 1e-4f, "none crosses the middle square where the bars overlap");
            }
        }

        [Test]
        public void TheIcosahedronHasThirtyEqualEdgesOnItsSphere()
        {
            var edges = ShapeEdges.Icosahedron(0.6f);
            Assert.AreEqual(30, edges.Count);
            float length = Vector3.Distance(edges[0].Item1, edges[0].Item2);
            foreach ((Vector3 a, Vector3 b) in edges)
            {
                Assert.AreEqual(length, Vector3.Distance(a, b), 1e-4f, "all the same length");
                Assert.AreEqual(0.6f, a.magnitude, 1e-4f);
            }
        }

        [Test]
        public void EachEdgeIsDrawnAsAThinBarAsWideAsTheLine()
        {
            Mesh mesh = ShapeEdges.LinesMesh(new System.Collections.Generic.List<(Vector3, Vector3)> { (new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f)) }, 0.05f);
            try
            {
                Assert.AreEqual(8, mesh.vertexCount, "a square bar of eight corners per edge");
                Assert.AreEqual(36, mesh.triangles.Length);
                Assert.AreEqual(1.05f, mesh.bounds.size.x, 1e-4f, "it runs half a line's width past both ends, so corners close");
                Assert.AreEqual(0.05f, mesh.bounds.size.y, 1e-4f, "and straddles the edge: half outside, half in");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        // ---- the view

        private HealthPackConfig config;
        private Material solid;
        private GameObject parent;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<HealthPackConfig>();
            var so = new SerializedObject(config);
            so.FindProperty("crossSizeMetres").floatValue = 1.2f;
            so.FindProperty("shapeEdgeMetres").floatValue = 0.05f;
            so.ApplyModifiedPropertiesWithoutUndo();
            solid = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            parent = new GameObject("PackLookTests") { hideFlags = HideFlags.HideAndDontSave };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (HealthPackView view in parent.GetComponentsInChildren<HealthPackView>(true))
                typeof(HealthPackView).GetMethod("OnDestroy", Any).Invoke(view, null);
            Object.DestroyImmediate(parent);
            Object.DestroyImmediate(solid);
            Object.DestroyImmediate(config);
        }

        [TestCase(false, TestName = "the health pack's cross")]
        [TestCase(true, TestName = "the ultimate pack's icosahedron")]
        public void EveryPartOfTheShapeHasBlackLinesOnItsEdges(bool ultimate)
        {
            HealthPackView view = HealthPackView.Create(parent.transform, Vector3.zero, "Pack", config, null, solid, ultimate);
            Assert.AreEqual(1, view.EdgeRenderers.Length, "one set of lines for the whole shape");
            var block = new MaterialPropertyBlock();
            Renderer edge = view.EdgeRenderers[0];
            edge.GetPropertyBlock(block);
            Assert.AreEqual(Color.black, block.GetColor("_BaseColor"), edge.name);
            Assert.AreEqual((ultimate ? 30 : 36) * 8, edge.GetComponent<MeshFilter>().sharedMesh.vertexCount, edge.name);
        }

        [Test]
        public void TheLinesFollowTheCross()
        {
            HealthPackView view = HealthPackView.Create(parent.transform, Vector3.zero, "Pack", config, null, solid);
            Bounds lines = view.EdgeRenderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(1.25f, lines.size.x, 1e-4f, "the 1.2 m cross, plus the line's width");
            Assert.AreEqual(1.25f, lines.size.y, 1e-4f);
            Assert.AreEqual(0.45f, lines.size.z, 1e-4f, "its 0.4 m depth");
        }

        [Test]
        public void DestroyFreesTheLineMeshes()
        {
            HealthPackView view = HealthPackView.Create(parent.transform, Vector3.zero, "Pack", config, null, solid, ultimate: true);
            Mesh lines = view.EdgeRenderers[0].GetComponent<MeshFilter>().sharedMesh;
            typeof(HealthPackView).GetMethod("OnDestroy", Any).Invoke(view, null);
            Assert.IsTrue(lines == null, "the lines' mesh is freed with the pack");
        }

        // ---- the rings on an ultimate pack's take

        [Test]
        public void EachRingStartsADelayAfterTheOneBefore()
        {
            Assert.AreEqual(0f, HealthPackLook.PickupRingProgress(0f, 0, 0.15f, 0.8f), 1e-5f);
            Assert.Less(HealthPackLook.PickupRingProgress(0.1f, 1, 0.15f, 0.8f), 0f, "the second ring has not started yet");
            Assert.AreEqual(0.5f, HealthPackLook.PickupRingProgress(0.55f, 1, 0.15f, 0.8f), 1e-5f);
            Assert.AreEqual(1f, HealthPackLook.PickupRingProgress(0.8f, 0, 0.15f, 0.8f), 1e-5f);
        }

        [Test]
        public void ARingRisesFastThenSlowsAndFadesOut()
        {
            Assert.AreEqual(0f, HealthPackLook.PickupRingHeight(0f, 2f), 1e-5f);
            Assert.AreEqual(1.5f, HealthPackLook.PickupRingHeight(0.5f, 2f), 1e-5f, "three quarters of the rise at half the time");
            Assert.AreEqual(2f, HealthPackLook.PickupRingHeight(1f, 2f), 1e-5f);
            Assert.AreEqual(1f, HealthPackLook.PickupRingAlpha(0f), 1e-5f);
            Assert.AreEqual(0f, HealthPackLook.PickupRingAlpha(1f), 1e-5f);
        }

        // A pack's room value starts with when it comes BACK (the take's time plus the pack's respawn), as HealthPackRules.Decide writes it.
        [TestCase(true, 1000, 1400, true, TestName = "an ultimate pack taken just now")]
        [TestCase(false, 1000, 1400, false, TestName = "a health pack has its own sparks")]
        [TestCase(true, 1000, 3000, false, TestName = "an old take (a late join reading the room) shows nothing")]
        [TestCase(true, 1000, 900, false, TestName = "a take stamped ahead of this clock shows nothing")]
        public void RingsOnlyForAFreshUltimateTake(bool ultimate, int takenAt, int now, bool expected) =>
            Assert.AreEqual(expected, HealthPackRules.ShowsPickupRings(ultimate, new[] { takenAt + 30000, 2, 1 }, now, 30000));

        [Test]
        public void RingsAcrossTheServerClocksWrap() =>
            Assert.IsTrue(HealthPackRules.ShowsPickupRings(true, new[] { unchecked(int.MaxValue - 100 + 30000), 2, 1 }, unchecked(int.MaxValue + 200), 30000),
                "the server clock is an int that wraps round");

        [Test]
        public void NoRingsWithoutATake() =>
            Assert.IsFalse(HealthPackRules.ShowsPickupRings(true, null, 1000, 30000));

        // ---- the asset and the wiring

        [Test]
        public void TheNewLookFieldsAreInTheYamlWithATooltip()
        {
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/HealthPackConfig.asset").Replace("\r\n", "\n");
            foreach (string name in new[] { "shapeEdgeMetres", "shapeEdgeColour", "pickupRingCount", "pickupRingSeconds", "pickupRingDelaySeconds",
                                            "pickupRingStartRadius", "pickupRingEndRadius", "pickupRingRiseMetres", "pickupRingWidthMetres" })
            {
                FieldInfo field = typeof(HealthPackConfig).GetField(name, Any);
                Assert.NotNull(field, name);
                Assert.NotNull(System.Attribute.GetCustomAttribute(field, typeof(TooltipAttribute)), name + " has no tooltip");
                StringAssert.Contains("\n  " + name + ":", yaml, name + " is not in HealthPackConfig.asset");
            }
        }

        [Test]
        public void EveryClientAsksTheRuleAndPlaysTheRings()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(HealthPackManager), nameof(HealthPackManager.OnRoomPropertiesUpdate),
                typeof(HealthPackRules).GetMethod(nameof(HealthPackRules.ShowsPickupRings))), "the rule decides");
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackManager), nameof(HealthPackManager.OnRoomPropertiesUpdate),
                typeof(PickupRingsView).GetMethod(nameof(PickupRingsView.Play))), "the rings play");
            Assert.IsTrue(IlWiring.Uses(typeof(PickupRingsView), nameof(PickupRingsView.Play),
                typeof(Overpower.Vision.VisibleWhenSeen).GetMethod(nameof(Overpower.Vision.VisibleWhenSeen.AttachToCaster))),
                "hidden with a player the fog hides");
            Assert.IsTrue(IlWiring.Uses(typeof(PickupRingsView), "LateUpdate", typeof(HealthPackLook).GetMethod(nameof(HealthPackLook.PickupRingHeight))), "height");
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackView), nameof(HealthPackView.Create), typeof(ShapeEdges).GetMethod(nameof(ShapeEdges.Plus))), "the cross's outline");
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackView), nameof(HealthPackView.Create), typeof(ShapeEdges).GetMethod(nameof(ShapeEdges.Icosahedron))), "the icosahedron's");
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackView), nameof(HealthPackView.Create), typeof(ShapeEdges).GetMethod(nameof(ShapeEdges.LinesMesh))), "drawn as lines");
        }
    }
}
