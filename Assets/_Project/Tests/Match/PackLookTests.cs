using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Match;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 1 Nov: the health pack's cross and the ultimate pack's icosahedron get a black edge so their shapes read on any floor; taking an
    /// ultimate pack shows rings rising round the player who took it (a health pack has its own sparks). Literal numbers are test values.
    /// </summary>
    public class PackLookTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void TheEdgeIsTheShapeGrownByItsThicknessOnEverySide()
        {
            Vector3 edge = HealthPackLook.OutlineSize(new Vector3(1.2f, 0.4f, 0.4f), 0.06f);
            Assert.AreEqual(1.32f, edge.x, 1e-5f);
            Assert.AreEqual(0.52f, edge.y, 1e-5f);
            Assert.AreEqual(0.52f, edge.z, 1e-5f);
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
            so.FindProperty("shapeOutlineMetres").floatValue = 0.05f;
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
        public void EveryPartOfTheShapeHasABlackEdgeDrawnFromTheInside(bool ultimate)
        {
            HealthPackView view = HealthPackView.Create(parent.transform, Vector3.zero, "Pack", config, null, solid, ultimate);
            Assert.AreEqual(view.CrossRenderers.Length, view.OutlineRenderers.Length, "one edge per part");
            var block = new MaterialPropertyBlock();
            foreach (Renderer edge in view.OutlineRenderers)
            {
                edge.GetPropertyBlock(block);
                Assert.AreEqual(Color.black, block.GetColor("_BaseColor"), edge.name);
                Assert.AreEqual(1f, edge.sharedMaterial.GetFloat("_Cull"), "front faces culled: only the rim behind the shape shows");
            }
        }

        [Test]
        public void TheCrossBarsEdgesAreTheBarsGrownByTheThickness()
        {
            HealthPackView view = HealthPackView.Create(parent.transform, Vector3.zero, "Pack", config, null, solid);
            Vector3 scale = view.OutlineRenderers[0].transform.localScale;
            Assert.AreEqual(1.3f, scale.x, 1e-5f, "around the 1.2 x 0.4 x 0.4 bar");
            Assert.AreEqual(0.5f, scale.y, 1e-5f);
        }

        [Test]
        public void TheIcosahedronsEdgeIsItGrownByTheThickness()
        {
            HealthPackView view = HealthPackView.Create(parent.transform, Vector3.zero, "Pack", config, null, solid, ultimate: true);
            Assert.AreEqual((0.6f + 0.05f) / 0.6f, view.OutlineRenderers[0].transform.localScale.x, 1e-4f);
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

        [TestCase(true, 1000, 1400, true, TestName = "an ultimate pack taken just now")]
        [TestCase(false, 1000, 1400, false, TestName = "a health pack has its own sparks")]
        [TestCase(true, 1000, 3000, false, TestName = "an old take (a late join reading the room) shows nothing")]
        [TestCase(true, 1000, 900, false, TestName = "a take stamped ahead of this clock shows nothing")]
        public void RingsOnlyForAFreshUltimateTake(bool ultimate, int takenAt, int now, bool expected) =>
            Assert.AreEqual(expected, HealthPackRules.ShowsPickupRings(ultimate, new[] { takenAt, 2, 1 }, now));

        [Test]
        public void NoRingsWithoutATake() =>
            Assert.IsFalse(HealthPackRules.ShowsPickupRings(true, null, 1000));

        // ---- the asset and the wiring

        [Test]
        public void TheNewLookFieldsAreInTheYamlWithATooltip()
        {
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/HealthPackConfig.asset").Replace("\r\n", "\n");
            foreach (string name in new[] { "shapeOutlineMetres", "shapeOutlineColour", "pickupRingCount", "pickupRingSeconds", "pickupRingDelaySeconds",
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
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackView), nameof(HealthPackView.Create), typeof(HealthPackLook).GetMethod(nameof(HealthPackLook.OutlineSize))), "edge");
        }
    }
}
