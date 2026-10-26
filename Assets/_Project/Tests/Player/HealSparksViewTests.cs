using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.UI;
using Overpower.Vision;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Tests
{
    /// <summary>The green plus signs round a player whose health is going up: built hidden on every copy of every player, before its fog
    /// gate; shown by HealRule from the health samples alone; in the healing green; freed with the player; no garbage per frame. Literal
    /// looks, never the real asset's tuning.</summary>
    public class HealSparksViewTests
    {
        private static readonly System.Type View = typeof(HealSparksView);
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const float Frame = 1f / 60f;
        private static readonly Color Green = new Color(0.31f, 0.92f, 0.33f, 1f);

        private UiTheme theme;
        private ArenaLook look;
        private Material material;
        private GameObject body;

        private static int LivePlusMeshes() => Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m != null && m.name == HealSparksView.MeshName);

        [SetUp]
        public void SetUp()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.healGreenColor = Green;
            look = ScriptableObject.CreateInstance<ArenaLook>();
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            look.fxAlpha = material;
            look.healSparkCount = 5;
            look.healSparkRiseSeconds = 1f;
            look.healSparkRadius = 0.7f;
            look.healSparkStartHeight = 0.3f;
            look.healSparkRiseMetres = 2f;
            look.healSparkSize = 0.3f;
            look.healSparkGlow = 1.5f;
            look.healSparkTailSeconds = 0.5f;
            look.healSpawnJumpMetres = 5f;
            theme.arenaLook = look;
            body = new GameObject("HealSparksViewTests Body") { hideFlags = HideFlags.HideAndDontSave };
        }

        // Edit mode never sends OnDestroy to a plain MonoBehaviour, so the test sends it the way the game's Destroy does.
        private static void DestroyLikeTheGame(GameObject go)
        {
            foreach (HealSparksView view in go.GetComponentsInChildren<HealSparksView>(true))
                View.GetMethod("OnDestroy", Any).Invoke(view, null);
            Object.DestroyImmediate(go);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyLikeTheGame(body);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        private HealSparksView Make() => HealSparksView.TryCreate(body.transform, null, null, theme, 0.9f);

        [Test]
        public void BuiltHiddenWithOneRendererPerSparkAndNoCollider()
        {
            HealSparksView view = Make();
            Assert.NotNull(view);
            Assert.AreSame(body.transform, view.transform.parent);
            Assert.IsFalse(view.SparksShown, "nobody is healing yet");
            Transform sparks = view.transform.Find(HealSparksView.SparksChildName);
            Assert.NotNull(sparks);
            MeshRenderer[] renderers = sparks.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(5, renderers.Length, "Heal Spark Count plus signs");
            foreach (MeshRenderer r in renderers)
            {
                Assert.AreSame(material, r.sharedMaterial, "the Arena Look's FX Alpha, so the green stays green over any floor");
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode);
                Assert.AreSame(view.PlusMesh, r.GetComponent<MeshFilter>().sharedMesh, "every spark is the plus sign");
            }
            Assert.AreEqual(0, body.GetComponentsInChildren<Collider>(true).Length, "never something to shoot or stand on");
        }

        [Test]
        public void TheSparkIsAPlusSign()
        {
            HealSparksView view = Make();
            Mesh plus = view.PlusMesh;
            Assert.NotNull(plus);
            Vector3[] v = plus.vertices;
            Assert.AreEqual(0.5f, v.Max(p => p.x), 1e-4f, "one unit tip to tip, scaled by Heal Spark Size");
            Assert.AreEqual(0.5f, v.Max(p => p.y), 1e-4f);
            Assert.IsFalse(v.Any(p => Mathf.Abs(p.x) > 0.17f && Mathf.Abs(p.y) > 0.17f), "no corner is filled: the arms make a plus, not a square");
            Assert.IsTrue(v.All(p => Mathf.Abs(p.z) < 1e-5f), "flat, facing the camera");
        }

        [Test]
        public void ShownWhileHealthRisesAndForTheTailAfter()
        {
            HealSparksView view = Make();
            Vector3 at = new Vector3(3f, 0f, 4f);
            view.Step(100f, true, at, Frame, 0f);
            Assert.IsFalse(view.SparksShown, "the first sample is only a start");
            view.Step(60f, true, at, Frame, 0.1f);
            Assert.IsFalse(view.SparksShown, "taking damage");
            view.Step(60.2f, true, at, Frame, 0.2f);
            Assert.IsTrue(view.SparksShown, "regenerating");
            view.Step(60.2f, true, at, 0.3f, 0.5f);
            Assert.IsTrue(view.SparksShown, "a gap between two health updates");
            view.Step(60.2f, true, at, 0.3f, 0.8f);
            Assert.IsFalse(view.SparksShown, "the tail is over");
            view.Step(95f, true, at, Frame, 0.9f);
            Assert.IsTrue(view.SparksShown, "a health pack");
            view.Step(95f, false, at, Frame, 1f);
            Assert.IsFalse(view.SparksShown, "dead: hidden at once");
        }

        [Test]
        public void NoSparksOnARespawnOrAFreshRound()
        {
            HealSparksView view = Make();
            Vector3 deathSpot = new Vector3(30f, 0f, 30f), spawn = new Vector3(-40f, 0f, 2f);
            view.Step(20f, true, deathSpot, Frame, 0f);
            view.Step(0f, false, deathSpot, Frame, 0.1f);
            view.Step(100f, true, spawn, Frame, 0.2f);
            Assert.IsFalse(view.SparksShown, "the respawn's jump back to full health");

            view.Step(35f, true, deathSpot, Frame, 3f);
            view.Step(35f, true, deathSpot, Frame, 3.1f);
            view.Step(100f, true, spawn, Frame, 3.2f);
            Assert.IsFalse(view.SparksShown, "a new round puts a hurt player back at the spawn at full health");
        }

        [Test]
        public void TheSparksWearTheHealingGreenAndRiseRoundTheBody()
        {
            HealSparksView view = Make();
            body.transform.position = new Vector3(2f, 1f, -3f);
            view.Step(50f, true, body.transform.position, Frame, 0f);
            view.Step(51f, true, body.transform.position, Frame, 0.35f);
            Assert.IsTrue(view.SparksShown);
            var block = new MaterialPropertyBlock();
            Color green = LookColours.For(LookFamily.Heal, -1, theme);
            Assert.AreEqual(Green, green, "the heal family is the UI Theme's healing green");
            Transform sparks = view.transform.Find(HealSparksView.SparksChildName);
            foreach (MeshRenderer r in sparks.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.GetPropertyBlock(block);
                Color c = block.GetColor("_BaseColor");
                Assert.Greater(c.g, c.r * 2f, "green, not white or yellow");
                Assert.Greater(c.g, c.b * 2f, "green, not cyan or blue");
                Vector3 local = r.transform.position - body.transform.position;
                Assert.AreEqual(0.7f, new Vector2(local.x, local.z).magnitude, 1e-3f, "on the ring round the body");
                Assert.That(local.y + 0.9f, Is.InRange(0.3f - 1e-3f, 2.3f + 1e-3f), "between the feet's start height and the top");
            }
        }

        [Test]
        public void NoGarbagePerFrame()
        {
            // The game always has a camera to turn the signs to, so this frame takes that path too (the open scene's, or this one).
            var viewer = new GameObject("HealSparksViewTests Camera") { hideFlags = HideFlags.HideAndDontSave, tag = "MainCamera" };
            viewer.AddComponent<Camera>();
            try
            {
                Assert.NotNull(Camera.main, "the signs turn to a camera every frame");
                HealSparksView view = Make();
                view.Step(50f, true, Vector3.zero, Frame, 0f);
                view.Step(50.1f, true, Vector3.zero, Frame, 0.02f); // warm up: the first show may set things up once
                int allocations = AllocationProbe.Count(() =>
                {
                    for (int i = 0; i < 120; i++)
                        view.Step(50.1f + i * 0.1f, true, Vector3.zero, Frame, 0.04f + i * Frame);
                });
                Assert.AreEqual(0, allocations, "no allocation while the sparks rise");
            }
            finally
            {
                Object.DestroyImmediate(viewer);
            }
        }

        [Test]
        public void NoArenaLookOrMaterialBuildsNothing()
        {
            look.fxAlpha = null;
            Assert.IsNull(Make());
            Assert.AreEqual(0, body.transform.childCount);
        }

        [Test]
        public void DestroyFreesTheMesh()
        {
            int before = LivePlusMeshes();
            HealSparksView view = Make();
            Assert.AreEqual(before + 1, LivePlusMeshes(), "the view builds one mesh of its own");
            DestroyLikeTheGame(view.gameObject);
            Assert.AreEqual(before, LivePlusMeshes(), "the mesh leaked past its view");
        }

        [Test]
        public void TheGameCallsTheTestedRules()
        {
            var failed = new System.Collections.Generic.List<string>();
            void Check(bool ok, string what) { if (!ok) failed.Add(what); }

            MethodInfo rise = typeof(HealRule).GetMethod(nameof(HealRule.IsHealRise));
            MethodInfo healing = typeof(HealRule).GetMethod(nameof(HealRule.IsHealing));
            MethodInfo step = View.GetMethod(nameof(HealSparksView.Step));
            Check(IlWiring.ResultDecidesABranch(View, "Step", rise), "a rise restarts the tail only on HealRule.IsHealRise");
            Check(IlWiring.ResultDecidesABranch(View, "Step", healing), "the sparks show or hide on HealRule.IsHealing");
            Check(IlWiring.Uses(View, "Animate", typeof(HealRule).GetMethod(nameof(HealRule.PlaceSpark))), "the sparks are placed by the rule");
            Check(IlWiring.Uses(View, "LateUpdate", step), "every frame goes through Step");
            Check(IlWiring.Uses(View, "LateUpdate", typeof(PlayerHealth).GetProperty(nameof(PlayerHealth.Health)).GetGetMethod()),
                  "the health is the one every client shows in the overhead bar");
            Check(IlWiring.Uses(View, "LateUpdate", typeof(PlayerHealth).GetProperty(nameof(PlayerHealth.IsAlive)).GetGetMethod()),
                  "alive is the replicated alive state");
            Check(IlWiring.Uses(View, "BodySample", typeof(PlayerNetSync).GetProperty(nameof(PlayerNetSync.NetworkPosition)).GetGetMethod()),
                  "another player's body is where its last update put it, the update that carried its health");
            Check(IlWiring.Uses(View, "Dress", typeof(LookColours).GetMethod(nameof(LookColours.For))), "the colour is the heal family's");
            Check(IlWiring.Uses(View, "Step", typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster))),
                  "a player built before the local sight existed is gated before it shows");
            Check(IlWiring.Uses(typeof(PlayerHealth), "Start", View.GetMethod(nameof(HealSparksView.TryCreate))),
                  "every copy of every player builds its sparks, so every client can show them");
            Assert.IsEmpty(failed, string.Join("; ", failed));
        }

        // The gate collects renderers once: a renderer made after it attaches would show through the fog. Bound to the player's own view,
        // the sparks hide and show with the player.
        [Test]
        public void TheFogGateFollowsThePlayerAndComesAfterTheSparksAreBuilt()
        {
            MethodBase create = View.GetMethod(nameof(HealSparksView.TryCreate));
            var attach = IlWiring.CallOffsets(create, typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster)));
            var material = IlWiring.CallOffsets(create, typeof(Renderer).GetProperty(nameof(Renderer.sharedMaterial)).GetSetMethod());
            Assert.AreEqual(1, attach.Count, "attached once, in TryCreate");
            Assert.IsNotEmpty(material, "the sparks' renderers are set up in TryCreate");
            Assert.Less(material.Max(), attach[0], "every renderer exists before the fog gate collects them");
            Assert.AreEqual(1, create.GetParameters().Where(p => p.ParameterType == typeof(Photon.Pun.PhotonView)).Select(p => p.Position).Single(),
                "TryCreate's player is its second argument");
            Assert.AreEqual(0x03, create.GetMethodBody().GetILAsByteArray()[attach[0] - 1], "the gate follows the player's view (ldarg.1), never a spot");
        }
    }
}
