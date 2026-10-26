using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Net;
using Overpower.UI;
using Overpower.Vision;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Tests
{
    /// <summary>The flat team hexagon under every player's feet: shown only with the Arena Look switch on, a known team and a living
    /// player; in the team colour the mechs and shots use; flat on the floor, never casting a shadow, built before its fog gate, and
    /// its mesh freed with it. Literal looks and colours, never the real asset's tuning.</summary>
    public class FeetHexTests
    {
        private static readonly System.Type View = typeof(FeetHexView);
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private UiTheme theme;
        private ArenaLook look;
        private Material material;
        private GameObject body;

        private static int LiveHexMeshes() => Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m != null && m.name == FeetHexView.MeshName);

        [SetUp]
        public void SetUp()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.teamShotColors = new[] { new Color(0.9f, 0.9f, 0.95f, 1f), new Color(0.6f, 0.2f, 1f, 1f), new Color(0.1f, 0.9f, 1f, 1f) };
            look = ScriptableObject.CreateInstance<ArenaLook>();
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            look.fxAlpha = material;
            look.feetHexShown = true;
            look.feetHexRadius = 1.1f;
            look.feetHexEdgeWidth = 0.1f;
            look.feetHexEdgeAlpha = 0.8f;
            look.feetHexFillAlpha = 0.25f;
            look.feetHexLift = 0.05f;
            theme.arenaLook = look;
            body = new GameObject("FeetHexTests Body") { hideFlags = HideFlags.HideAndDontSave };
        }

        // Edit mode never sends OnDestroy to a plain MonoBehaviour, so the test sends it the way the game's Destroy does.
        private static void DestroyLikeTheGame(GameObject go)
        {
            foreach (FeetHexView view in go.GetComponentsInChildren<FeetHexView>(true))
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

        [Test]
        public void ShownOnlyWhenSwitchedOnWithATeamAndAlive()
        {
            Assert.IsTrue(FeetHexRule.Show(true, 0, true), "a living player of team 0");
            Assert.IsTrue(FeetHexRule.Show(true, 2, true), "a living player of team 2");
            Assert.IsFalse(FeetHexRule.Show(false, 1, true), "the Arena Look switch hides every hexagon");
            Assert.IsFalse(FeetHexRule.Show(true, -1, true), "a team not known yet draws nothing rather than a guessed colour");
            Assert.IsFalse(FeetHexRule.Show(true, 1, false), "nothing under a dead player");
        }

        [Test]
        public void StepShowsAndHidesTheHexByTheRule()
        {
            FeetHexView view = FeetHexView.TryCreate(body.transform, null, null, theme, 0.5f);
            Assert.NotNull(view);
            Assert.IsFalse(view.HexShown, "built hidden");
            view.Step(1, true);
            Assert.IsTrue(view.HexShown, "a living player with a team");
            view.Step(1, false);
            Assert.IsFalse(view.HexShown, "hidden while dead");
            view.Step(1, true);
            Assert.IsTrue(view.HexShown, "back after the respawn");
            view.Step(-1, true);
            Assert.IsFalse(view.HexShown, "hidden while the team is unknown");
            view.Step(0, true);
            look.feetHexShown = false;
            view.Step(0, true);
            Assert.IsFalse(view.HexShown, "the switch hides it at once");
        }

        [Test]
        public void TheHexWearsTheTeamColourOfTheShotsAndMechs()
        {
            FeetHexView view = FeetHexView.TryCreate(body.transform, null, null, theme, 0.5f);
            view.Step(1, true);
            Color[] colours = view.HexMesh.colors;
            Assert.AreEqual(AoeRingMesh.VerticesPerCell, colours.Length, "one hexagon: outline and fill");
            // FX Alpha reads vertex colours as they are, and the project renders in linear space: handed over linear, like the floor's
            // owner tiles and the mechs, or the team colour comes out washed pale.
            Color team = LookColours.For(LookFamily.Team, 1, theme).linear;
            for (int i = 0; i < colours.Length; i++)
            {
                Assert.AreEqual(team.r, colours[i].r, 1e-4f, "red, vertex " + i);
                Assert.AreEqual(team.g, colours[i].g, 1e-4f, "green, vertex " + i);
                Assert.AreEqual(team.b, colours[i].b, 1e-4f, "blue, vertex " + i);
                float alpha = i < AoeRingMesh.FillStart ? look.feetHexEdgeAlpha : look.feetHexFillAlpha;
                Assert.AreEqual(alpha, colours[i].a, 1e-4f, (i < AoeRingMesh.FillStart ? "edge" : "fill") + " alpha, vertex " + i);
            }

            view.Step(2, true);
            Assert.AreEqual(theme.ShotColorFor(2).linear.g, view.HexMesh.colors[0].g, 1e-4f, "a team change recolours it");
            look.feetHexFillAlpha = 0.5f;
            view.Step(2, true);
            Assert.AreEqual(0.5f, view.HexMesh.colors[AoeRingMesh.FillStart].a, 1e-4f, "a retuned fill shows at once");
        }

        [Test]
        public void TheHexLiesFlatAtTheFeetWithNoShadowAndNoCollider()
        {
            body.transform.SetPositionAndRotation(new Vector3(4f, 2f, -3f), Quaternion.Euler(0f, 37f, 0f));
            FeetHexView view = FeetHexView.TryCreate(body.transform, null, null, theme, 0.5f);
            view.Step(0, true);
            Transform hex = view.transform.Find(FeetHexView.HexChildName);
            Assert.NotNull(hex);
            Assert.AreEqual(2f - 0.5f + 0.05f, hex.position.y, 1e-4f, "on the floor under the feet, lifted by Feet Hex Lift");
            Assert.AreEqual(4f, hex.position.x, 1e-4f);
            Assert.AreEqual(-3f, hex.position.z, 1e-4f);
            Assert.Less(Quaternion.Angle(Quaternion.identity, hex.rotation), 0.01f, "lined up with the floor's hexagons whichever way the player turns");
            MeshRenderer renderer = hex.GetComponent<MeshRenderer>();
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
            Assert.IsFalse(renderer.receiveShadows);
            Assert.AreSame(material, renderer.sharedMaterial, "drawn with the Arena Look's FX Alpha");
            Assert.AreEqual(0, body.GetComponentsInChildren<Collider>(true).Length, "never something to stand on or shoot");

            body.transform.position = new Vector3(10f, 2f, 1f);
            view.Step(0, true);
            Assert.AreEqual(10f, hex.position.x, 1e-4f, "follows the player");
        }

        [Test]
        public void TheHexIsSizedFromTheLook()
        {
            int before = LiveHexMeshes();
            FeetHexView view = FeetHexView.TryCreate(body.transform, null, null, theme, 0.5f);
            view.Step(0, true);
            Vector3[] v = view.HexMesh.vertices;
            for (int k = 0; k < 6; k++)
                Assert.AreEqual(1.1f, new Vector2(v[k].x, v[k].z).magnitude, 1e-4f, "outer corner " + k + " at Feet Hex Radius");
            Assert.AreEqual(1.1f, v[0].z, 1e-4f, "a corner points to +Z like the floor's hexagons");
            look.feetHexRadius = 0.7f;
            view.Step(0, true);
            Assert.AreEqual(0.7f, view.HexMesh.vertices[0].z, 1e-4f, "a retuned size shows at once");
            Assert.AreEqual(before + 1, LiveHexMeshes(), "the old mesh is freed when it is rebuilt");
        }

        [Test]
        public void NoArenaLookOrMaterialBuildsNothing()
        {
            look.fxAlpha = null;
            Assert.IsNull(FeetHexView.TryCreate(body.transform, null, null, theme, 0.5f));
            Assert.AreEqual(0, body.transform.childCount);
        }

        [Test]
        public void DestroyFreesTheMesh()
        {
            int before = LiveHexMeshes();
            FeetHexView view = FeetHexView.TryCreate(body.transform, null, null, theme, 0.5f);
            view.Step(0, true);
            Assert.AreEqual(before + 1, LiveHexMeshes(), "the view builds one mesh of its own");
            DestroyLikeTheGame(view.gameObject);
            Assert.AreEqual(before, LiveHexMeshes(), "the mesh leaked past its view");
        }

        [Test]
        public void TheGameCallsTheTestedRules()
        {
            var failed = new System.Collections.Generic.List<string>();
            void Check(bool ok, string what) { if (!ok) failed.Add(what); }

            MethodInfo show = typeof(FeetHexRule).GetMethod(nameof(FeetHexRule.Show));
            MethodInfo step = View.GetMethod(nameof(FeetHexView.Step));
            MethodInfo attach = typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster));
            Check(IlWiring.ResultDecidesABranch(View, "Step", show), "the hexagon shows or hides by FeetHexRule.Show");
            Check(IlWiring.Uses(View, "LateUpdate", step), "every frame goes through Step");
            Check(IlWiring.Uses(View, "LateUpdate", typeof(PlayerLifecycle).GetProperty(nameof(PlayerLifecycle.IsAlive)).GetGetMethod()),
                  "alive is the player's own alive state");
            Check(IlWiring.Uses(View, "LateUpdate", typeof(Teams).GetMethod(nameof(Teams.TryGetTeam), new[] { typeof(Photon.Realtime.Player), typeof(int).MakeByRefType() })),
                  "the team is the team property every client has");
            Check(IlWiring.Uses(View, "Dress", typeof(LookColours).GetMethod(nameof(LookColours.For))), "the colour is the look family's team colour");
            Check(IlWiring.Uses(View, "TryCreate", attach), "the fog gate follows the player like the bubble");
            Check(IlWiring.Uses(View, "Step", attach), "a player built before the local sight existed is gated before it shows");
            MethodInfo build = typeof(PlayerTeamAppearance).GetMethod("BuildFeetHex", Any);
            Check(build != null && IlWiring.Uses(typeof(PlayerTeamAppearance), "Start", build), "every player body builds its hexagon as it starts");
            Check(IlWiring.Uses(typeof(PlayerTeamAppearance), "BuildFeetHex", View.GetMethod(nameof(FeetHexView.TryCreate))),
                  "through FeetHexView.TryCreate");
            Check(IlWiring.Uses(typeof(PlayerTeamAppearance), "BuildFeetHex", typeof(AbilityVisualGeometry).GetMethod(nameof(AbilityVisualGeometry.RootAboveFeet))),
                  "on the floor: the feet from the player's own capsule");
            Assert.IsEmpty(failed, string.Join("; ", failed));
        }

        // Bound to the player's own view, the hexagon hides and shows with the player; a spot gate (a null caster) would follow the
        // position's fog instead and could show it while the mech is hidden.
        [Test]
        public void TheFogGateFollowsThePlayerItStandsUnder()
        {
            MethodInfo attach = typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster));
            MethodBase create = View.GetMethod(nameof(FeetHexView.TryCreate));
            byte[] il = create.GetMethodBody().GetILAsByteArray();
            var calls = IlWiring.CallOffsets(create, attach);
            Assert.AreEqual(1, calls.Count);
            Assert.AreEqual(1, create.GetParameters().Where(p => p.ParameterType == typeof(Photon.Pun.PhotonView)).Select(p => p.Position).Single(),
                "TryCreate's player is its second argument");
            Assert.AreEqual(0x03, il[calls[0] - 1], "TryCreate hands the gate the player's view (ldarg.1), never a spot");

            MethodBase step = View.GetMethod(nameof(FeetHexView.Step));
            FieldInfo player = View.GetField("player", Any);
            byte[] stepIl = step.GetMethodBody().GetILAsByteArray();
            var stepCalls = IlWiring.CallOffsets(step, attach);
            Assert.AreEqual(1, stepCalls.Count);
            int at = stepCalls[0];
            Assert.IsTrue(stepIl[at - 5] == 0x7B && System.BitConverter.ToInt32(stepIl, at - 4) == player.MetadataToken,
                "a late gate in Step follows the player's view too (ldfld player)");
        }

        // The gate collects renderers once: a renderer made after it attaches would show through the fog.
        [Test]
        public void TheFogGateComesAfterTheHexIsBuilt()
        {
            MethodBase create = View.GetMethod(nameof(FeetHexView.TryCreate));
            var attach = IlWiring.CallOffsets(create, typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster)));
            var material = IlWiring.CallOffsets(create, typeof(Renderer).GetProperty(nameof(Renderer.sharedMaterial)).GetSetMethod());
            Assert.AreEqual(1, attach.Count, "attached once, in TryCreate");
            Assert.IsNotEmpty(material, "the hexagon's renderer is set up in TryCreate");
            Assert.Less(material.Max(), attach[0], "the renderer exists before the fog gate collects it");
        }
    }
}
