using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Combat;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The placed abilities in their owner's team colour: the Portal a flat hexagon gate with a slowly turning hexagon inside, the
    /// Mine a hex puck with a blinking light on its cap, the Cover a wall panel with glowing edges, the Electric Fence pylons with jagged
    /// arcs between them. Every colour comes from the ability's row in the look table (Gold here, so a view that ignores the row fails).</summary>
    public class PlacedLookTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private const int PortalId = 17, MinesId = 19, CoverId = 20, FenceId = 26;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // ---- rules ----

        [Test]
        public void ThePortalsInnerHexTurnsSlowlyWithTheServerClock()
        {
            Assert.AreEqual(0f, PortalGateLook.InnerYaw(0, 20f), 1e-4f);
            Assert.AreEqual(20f, PortalGateLook.InnerYaw(1000, 20f), 1e-3f, "Portal Inner Turn Speed degrees each second");
            Assert.AreEqual(10f, PortalGateLook.InnerYaw(18500, 20f), 1e-3f, "round and round");
            float early = PortalGateLook.InnerYaw(-500, 20f);
            Assert.GreaterOrEqual(early, 0f, "a server clock below zero still gives a turn in 0..360");
            Assert.AreEqual(350f, early, 1e-3f);
            Assert.AreEqual(PortalGateLook.InnerYaw(123456, 20f), PortalGateLook.InnerYaw(123456, 20f), "the same server time gives every client the same turn");
            Assert.AreEqual(0f, PortalGateLook.InnerYaw(5000, 0f), 1e-5f, "no speed: it stands still");
        }

        [Test]
        public void TheMineLightBlinksOnAtTheStartOfEveryPeriod()
        {
            Assert.AreEqual(1f, MinePuckLook.CapLight(0.05f, 1f, 0.2f, 0.3f), 1e-5f, "on right after placing");
            Assert.AreEqual(0.3f, MinePuckLook.CapLight(0.5f, 1f, 0.2f, 0.3f), 1e-5f, "dim for the rest of the period");
            Assert.AreEqual(1f, MinePuckLook.CapLight(3.1f, 1f, 0.2f, 0.3f), 1e-5f, "on again at the start of each period");
            Assert.AreEqual(0.3f, MinePuckLook.CapLight(3.25f, 1f, 0.2f, 0.3f), 1e-5f);
            Assert.AreEqual(1f, MinePuckLook.CapLight(-0.4f, 1f, 0.2f, 0.3f), 1e-5f, "an age below zero reads as just placed");
            Assert.AreEqual(1f, MinePuckLook.CapLight(0.5f, 0f, 0.2f, 0.3f), 1e-5f, "no period: a steady light");
            Color c = MinePuckLook.CapColour(new Color(0.8f, 0.4f, 0.2f, 0.6f), 0.5f);
            Assert.AreEqual(new Color(0.4f, 0.2f, 0.1f, 0.6f), c, "the team colour dimmed by the light, its opacity kept");
        }

        [Test]
        public void TheCoverEdgeIsTheTeamColourInLinearTimesTheGlowWithItsWidth()
        {
            var team = new Color(0.6f, 0.3f, 0.9f, 1f);
            Vector4 edge = CoverPanelLook.Edge(team, 1.5f, 0.08f);
            Assert.AreEqual(team.linear.r * 1.5f, edge.x, 1e-5f, "linear: the shader adds it as light");
            Assert.AreEqual(team.linear.g * 1.5f, edge.y, 1e-5f);
            Assert.AreEqual(team.linear.b * 1.5f, edge.z, 1e-5f);
            Assert.AreEqual(0.08f, edge.w, 1e-6f, "Cover Edge Width in metres");
            Assert.AreEqual(Vector4.zero, CoverPanelLook.Edge(team, 1.5f, 0f), "no width: the panel draws as a plain wall");
            Assert.AreEqual("_ArenaPanelEdge", CoverPanelLook.PropertyName);
        }

        [Test]
        public void FenceArcsPinToEveryPylonAndJumpInBetween()
        {
            const int posts = 8, perGap = 5;
            const float radius = 6f, jitter = 0.2f;
            Assert.AreEqual(40, FenceArcsRule.PointCount(posts, perGap));
            bool moved = false;
            for (int i = 0; i < FenceArcsRule.PointCount(posts, perGap); i++)
            {
                Vector3 p = FenceArcsRule.Point(77, 1, i, posts, perGap, radius, jitter);
                Assert.AreEqual(p, FenceArcsRule.Point(77, 1, i, posts, perGap, radius, jitter), "the same seed draws the same arc on every client");
                float yaw = i * 360f / (posts * perGap);
                Vector3 onRing = AbilityVisualGeometry.CirclePoint(radius, yaw);
                if (i % perGap == 0)
                {
                    Assert.AreEqual(onRing.x, p.x, 1e-4f, $"point {i} sits on its pylon");
                    Assert.AreEqual(onRing.z, p.z, 1e-4f, $"point {i} sits on its pylon");
                    Assert.AreEqual(0f, p.y, 1e-5f, $"point {i} at the arc's own height");
                    continue;
                }
                Vector2 flat = new Vector2(p.x, p.z);
                Assert.AreEqual(radius, flat.magnitude, jitter + 1e-4f, $"point {i} stays near the ring");
                Assert.LessOrEqual(Mathf.Abs(p.y), jitter + 1e-4f, $"point {i} jumps up or down by at most the jitter");
                Assert.AreEqual(yaw, Mathf.Repeat(Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg, 360f), 0.01f, $"point {i} keeps its place round the ring");
                if (FenceArcsRule.Point(78, 1, i, posts, perGap, radius, jitter) != p)
                    moved = true;
            }
            Assert.IsTrue(moved, "a new seed reshapes the arcs");
            Assert.AreNotEqual(FenceArcsRule.Point(77, 0, 2, posts, perGap, radius, jitter), FenceArcsRule.Point(77, 1, 2, posts, perGap, radius, jitter),
                "two arcs at different heights never move as one");
            Color team = new Color(0.8f, 0.4f, 0.2f, 1f);
            Assert.AreEqual(new Color(0.4f, 0.2f, 0.1f, 1f), FenceArcsRule.PylonBody(team, 0.5f), "a darker team colour, solid");
            Assert.AreEqual(new Color(1.2f, 0.6f, 0.3f, 1f), FenceArcsRule.PylonGlow(team, 1.5f));
        }

        // ---- views ----

        [Test]
        public void ThePortalIsAHexGateWithATurningInnerHexInTheLookTableColour()
        {
            using (var kit = new MovementLookTestKit(PortalId, LookShape.HexGate))
            {
                kit.Look.portalFrameWidth = 0.15f;
                kit.Look.portalInnerScale = 0.5f;
                kit.Look.portalInnerWidth = 0.06f;
                kit.Look.portalInnerTurnSpeed = 20f;
                GameObject go = kit.NewObject("portal view test");
                Portal portal = go.AddComponent<Portal>();
                MeshRenderer fill = Part<MeshRenderer>(kit, go, "Footprint");
                LineRenderer rim = Part<LineRenderer>(kit, go, "Rim");
                LineRenderer inner = Part<LineRenderer>(kit, go, "Inner Hex");
                rim.transform.localRotation = inner.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                PortalView view = go.AddComponent<PortalView>();
                Wire(view, kit, PortalId, ("footprint", fill), ("rim", rim), ("innerHex", inner));
                SetOwnerTeam(portal, 1);

                view.OnDeployablePlaced(portal);

                Assert.AreEqual(6, rim.positionCount, "the gate is a hexagon");
                Assert.IsTrue(rim.loop);
                for (int k = 0; k < 6; k++)
                    Assert.AreEqual(portal.Radius, new Vector2(rim.GetPosition(k).x, rim.GetPosition(k).y).magnitude, 1e-4f, "its corners on the portal's real edge");
                Assert.AreEqual(0.15f, rim.startWidth, 1e-5f, "Portal Frame Width");
                Assert.AreEqual(6, inner.positionCount, "the inner shape is a hexagon too");
                Assert.AreEqual(portal.Radius * 0.5f, new Vector2(inner.GetPosition(0).x, inner.GetPosition(0).y).magnitude, 1e-4f, "Portal Inner Scale of the gate");
                Assert.AreEqual(0.06f, inner.startWidth, 1e-5f, "Portal Inner Width");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, rim.startColor, "the gate wears the Portal's row in the look table");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, inner.startColor, "so does the inner hexagon");
                var block = new MaterialPropertyBlock();
                fill.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, block.GetColor(BaseColorId), "and the see-through floor of the gate");

                view.TurnInnerHex(1500);
                Assert.Less(Quaternion.Angle(Quaternion.Euler(90f, PortalGateLook.InnerYaw(1500, 20f), 0f), inner.transform.localRotation), 0.01f,
                    "turned by the server clock, still lying flat");
                Assert.Less(Quaternion.Angle(Quaternion.Euler(90f, 0f, 0f), rim.transform.localRotation), 0.01f, "the gate itself never turns");

                kit.Theme.portalCooldownColor = MovementLookTestKit.Silver;
                typeof(PortalView).GetMethod("Paint", Any).Invoke(view, new object[] { false });
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Silver, rim.startColor, "cooling down: the frame turns the cooling-down colour");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Silver, inner.startColor, "and so does the inner hexagon");
                fill.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Silver, block.GetColor(BaseColorId), "and the floor of the gate");
            }
        }

        [Test]
        public void TheMineIsAPuckWithABlinkingCapInTheLookTableColour()
        {
            using (var kit = new MovementLookTestKit(MinesId, LookShape.HexPuck))
            {
                kit.Look.mineCapGlow = 2f;
                kit.Look.mineBlinkPeriod = 1f;
                kit.Look.mineBlinkOnSeconds = 0.2f;
                kit.Look.mineCapDim = 0.25f;
                GameObject go = kit.NewObject("mine view test");
                Mine mine = go.AddComponent<Mine>();
                Transform visual = kit.NewObject("Visual").transform;
                visual.SetParent(go.transform, false);
                MeshRenderer body = Part<MeshRenderer>(kit, visual.gameObject, "Body");
                MeshRenderer cap = Part<MeshRenderer>(kit, visual.gameObject, "Cap");
                MineView view = go.AddComponent<MineView>();
                Wire(view, kit, MinesId, ("visualRoot", visual));
                var so = new SerializedObject(view);
                SetArray(so, "bodyParts", body);
                SetArray(so, "teamParts", cap);
                so.ApplyModifiedPropertiesWithoutUndo();
                SetOwnerTeam(mine, 1);

                view.OnDeployablePlaced(mine);
                var block = new MaterialPropertyBlock();

                view.PaintCap(0.05f);
                cap.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, block.GetColor(BaseColorId), "the cap wears the Mines row in the look table, light on");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold * 2f, block.GetColor(EmissionId), "and glows by Mine Cap Glow");

                view.PaintCap(0.6f);
                cap.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(MinePuckLook.CapColour(MovementLookTestKit.Gold, 0.25f), block.GetColor(BaseColorId), "dimmed between blinks");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold * 0.5f, block.GetColor(EmissionId), "its glow too");

                cap.enabled = false;
                view.PaintCap(1.05f);
                Assert.IsFalse(cap.enabled, "the blink never shows a cap the mine's own hiding switched off");
                body.GetPropertyBlock(block);
                Assert.AreNotEqual(MovementLookTestKit.Gold, block.GetColor(BaseColorId), "the puck stays dark");
            }
        }

        [Test]
        public void AGhostMineFadesItsPuckButKeepsItsCapLightBlinking()
        {
            using (var kit = new MovementLookTestKit(MinesId, LookShape.HexPuck))
            {
                kit.Look.mineCapGlow = 2f;
                kit.Look.mineBlinkPeriod = 1f;
                kit.Look.mineBlinkOnSeconds = 0.2f;
                kit.Look.mineCapDim = 0.25f;
                Material glass = kit.Make(new Material(kit.Fx) { name = "ghost glass" });
                GameObject go = kit.NewObject("ghost mine test");
                Mine mine = go.AddComponent<Mine>();
                MeshRenderer body = Part<MeshRenderer>(kit, go, "Body");
                MeshRenderer cap = Part<MeshRenderer>(kit, go, "Cap");
                Material capMaterial = cap.sharedMaterial;
                MineView view = go.AddComponent<MineView>();
                Wire(view, kit, MinesId, ("visualRoot", go.transform), ("ghostMaterial", glass));
                var so = new SerializedObject(view);
                SetArray(so, "bodyParts", body);
                SetArray(so, "teamParts", cap);
                so.FindProperty("ghostAlpha").floatValue = 0.4f;
                so.ApplyModifiedPropertiesWithoutUndo();
                SetOwnerTeam(mine, 1);
                view.OnDeployablePlaced(mine);

                view.ShowAs(MineVisibility.Ghost);
                var block = new MaterialPropertyBlock();
                Assert.AreSame(glass, body.sharedMaterial, "the puck turns to glass for its own team");
                body.GetPropertyBlock(block);
                Assert.AreEqual(0.4f, block.GetColor(BaseColorId).a, 1e-5f, "faded to Ghost Alpha");
                Assert.AreSame(capMaterial, cap.sharedMaterial, "the cap keeps the material that can glow");
                Assert.IsTrue(cap.enabled);

                view.PaintCap(0.05f);
                cap.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold * 2f, block.GetColor(EmissionId), "the Ghost's cap still lights up by Mine Cap Glow");
                Assert.AreEqual(1f, block.GetColor(BaseColorId).a, 1e-5f, "a solid light on the faded puck");
                view.PaintCap(0.6f);
                cap.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold * 0.5f, block.GetColor(EmissionId), "and dims between blinks");

                view.ShowAs(MineVisibility.Hidden);
                Assert.IsFalse(body.enabled, "an enemy sees no puck");
                Assert.IsFalse(cap.enabled, "and no light");
            }
        }

        [Test]
        public void TheCoverWearsTheWallLookWithEdgesInTheLookTableColour()
        {
            using (var kit = new MovementLookTestKit(CoverId, LookShape.GlossyPanel))
            {
                kit.Look.coverEdgeWidth = 0.09f;
                kit.Look.coverEdgeGlow = 1.4f;
                GameObject go = kit.NewObject("cover view test");
                CoverWall wall = go.AddComponent<CoverWall>();
                MeshRenderer panel = Part<MeshRenderer>(kit, go, "Visual");
                Material shared = panel.sharedMaterial;
                CoverPanelView view = go.AddComponent<CoverPanelView>();
                Wire(view, kit, CoverId, ("panel", panel));
                SetOwnerTeam(wall, 1);

                view.OnDeployablePlaced(wall);

                var block = new MaterialPropertyBlock();
                panel.GetPropertyBlock(block);
                Vector4 expected = CoverPanelLook.Edge(MovementLookTestKit.Gold, 1.4f, 0.09f);
                Vector4 edge = block.GetVector(Shader.PropertyToID(CoverPanelLook.PropertyName));
                Assert.AreEqual(expected.x, edge.x, 1e-4f, "the edge wears the Cover's row in the look table");
                Assert.AreEqual(expected.y, edge.y, 1e-4f);
                Assert.AreEqual(expected.z, edge.z, 1e-4f);
                Assert.AreEqual(0.09f, edge.w, 1e-5f, "Cover Edge Width");
                Assert.AreSame(shared, panel.sharedMaterial, "the wall material is shared, never copied");
            }
        }

        [Test]
        public void TheFenceIsPylonsWithJaggedArcsReshapedFromTheServerClock()
        {
            using (var kit = new MovementLookTestKit(FenceId, LookShape.ArcPylons))
            {
                kit.Look.fenceArcRate = 15f;
                kit.Look.fenceArcPointsPerGap = 4;
                kit.Look.fenceArcJitter = 0.2f;
                kit.Look.fenceArcWidth = 0.05f;
                kit.Look.fenceArcGlow = 1.5f;
                kit.Look.fencePylonShade = 0.5f;
                kit.Look.fencePylonGlow = 0.4f;
                GameObject go = kit.NewObject("fence view test");
                ElectricFence fence = go.AddComponent<ElectricFence>();
                GameObject cage = kit.NewObject("Cage");
                cage.transform.SetParent(go.transform, false);
                MeshRenderer post = Part<MeshRenderer>(kit, cage, "Post");
                LineRenderer low = Part<LineRenderer>(kit, cage, "Bar Low");
                LineRenderer top = Part<LineRenderer>(kit, cage, "Bar Top");
                FenceCageView view = go.AddComponent<FenceCageView>();
                Wire(view, kit, FenceId, ("post", post.transform));
                var so = new SerializedObject(view);
                SetArray(so, "bars", low, top);
                so.ApplyModifiedPropertiesWithoutUndo();
                SetOwnerTeam(fence, 1);
                SetViewId(fence, 7); // not 0, so a view that drops the fence's own salt from the seed is caught

                view.OnDeployablePlaced(fence);

                int posts = AbilityVisualGeometry.CagePostCount(fence.Radius, (float)new SerializedObject(view).FindProperty("maxPostSpacing").floatValue);
                MeshRenderer[] pylons = cage.GetComponentsInChildren<MeshRenderer>(true);
                Assert.AreEqual(posts, pylons.Length, "one pylon per post");
                var block = new MaterialPropertyBlock();
                foreach (MeshRenderer pylon in pylons)
                {
                    pylon.GetPropertyBlock(block);
                    MovementLookTestKit.AssertRgb(FenceArcsRule.PylonBody(MovementLookTestKit.Gold, 0.5f), block.GetColor(BaseColorId), "a pylon wears the Fence's row, darker");
                    MovementLookTestKit.AssertRgb(FenceArcsRule.PylonGlow(MovementLookTestKit.Gold, 0.4f), block.GetColor(EmissionId), "and glows faintly in it");
                }

                view.Reshape(2000);
                int seed = StunArcsRule.Seed(2000, 7, 15f);
                LineRenderer[] bars = { low, top };
                for (int level = 0; level < bars.Length; level++)
                {
                    LineRenderer bar = bars[level];
                    Assert.AreEqual(FenceArcsRule.PointCount(posts, 4), bar.positionCount, "a jagged loop through every pylon");
                    Assert.IsTrue(bar.loop);
                    Assert.AreEqual(0.05f, bar.startWidth, 1e-5f, "Fence Arc Width");
                    MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, bar.startColor, "the arcs wear the Fence's row");
                    for (int i = 0; i < bar.positionCount; i++)
                        Assert.AreEqual(FenceArcsRule.Point(seed, level, i, posts, 4, fence.Radius, 0.2f), bar.GetPosition(i), $"arc {level} point {i} from the server clock's seed");
                    bar.GetPropertyBlock(block);
                    Assert.AreEqual(1.5f, block.GetColor(BaseColorId).r, 1e-4f, "Fence Arc Glow");
                }
                Vector3 before = top.GetPosition(1);
                view.Reshape(2005);
                Assert.AreEqual(before, top.GetPosition(1), "within one step of Fence Arc Rate the arcs hold");
                view.Reshape(2000 + 3 * 67);
                Assert.AreNotEqual(before, top.GetPosition(1), "a later step reshapes them");
            }
        }

        [Test]
        public void PlacingTheViewsMakesNoMeshOrMaterial()
        {
            using (var kit = new MovementLookTestKit(MinesId, LookShape.HexPuck))
            {
                int meshes = Count<Mesh>(), materials = Count<Material>();
                GameObject go = kit.NewObject("leak test");
                Mine mine = go.AddComponent<Mine>();
                MeshRenderer cap = Part<MeshRenderer>(kit, go, "Cap");
                cap.sharedMaterial = kit.Fx;
                MineView view = go.AddComponent<MineView>();
                Wire(view, kit, MinesId, ("visualRoot", go.transform));
                var so = new SerializedObject(view);
                SetArray(so, "teamParts", cap);
                so.ApplyModifiedPropertiesWithoutUndo();
                view.OnDeployablePlaced(mine);
                view.PaintCap(0.1f);
                view.PaintCap(0.5f);

                GameObject other = kit.NewObject("cover leak test");
                CoverWall wall = other.AddComponent<CoverWall>();
                MeshRenderer panel = Part<MeshRenderer>(kit, other, "Visual");
                panel.sharedMaterial = kit.Fx;
                CoverPanelView cover = other.AddComponent<CoverPanelView>();
                Wire(cover, kit, CoverId, ("panel", panel));
                cover.OnDeployablePlaced(wall);

                GameObject fenceGo = kit.NewObject("fence leak test");
                ElectricFence fence = fenceGo.AddComponent<ElectricFence>();
                MeshRenderer post = Part<MeshRenderer>(kit, fenceGo, "Post");
                LineRenderer bar = Part<LineRenderer>(kit, fenceGo, "Bar");
                LineRenderer band = Part<LineRenderer>(kit, fenceGo, "Band");
                FenceCageView cage = fenceGo.AddComponent<FenceCageView>();
                Wire(cage, kit, FenceId, ("post", post.transform), ("band", band));
                var cageSo = new SerializedObject(cage);
                SetArray(cageSo, "bars", bar);
                cageSo.ApplyModifiedPropertiesWithoutUndo();
                cage.OnDeployablePlaced(fence);
                cage.Reshape(5000);

                GameObject portalGo = kit.NewObject("portal leak test");
                Portal portal = portalGo.AddComponent<Portal>();
                PortalView gate = portalGo.AddComponent<PortalView>();
                Wire(gate, kit, PortalId, ("footprint", Part<MeshRenderer>(kit, portalGo, "Footprint")), ("rim", Part<LineRenderer>(kit, portalGo, "Rim")),
                    ("innerHex", Part<LineRenderer>(kit, portalGo, "Inner Hex")));
                gate.OnDeployablePlaced(portal);
                gate.TurnInnerHex(5000);
                typeof(PortalView).GetMethod("Paint", Any).Invoke(gate, new object[] { false });

                Assert.AreEqual(meshes, Count<Mesh>(), "no runtime mesh is left to free");
                Assert.AreEqual(materials, Count<Material>(), "colours go through property blocks, never a material copy");
            }
        }

        [Test]
        public void EachViewCallsItsRuleEveryFrame()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PortalView), "Update", typeof(PortalView).GetMethod(nameof(PortalView.TurnInnerHex))), "the portal turns its inner hexagon");
            Assert.IsTrue(IlWiring.Uses(typeof(PortalView), nameof(PortalView.TurnInnerHex), typeof(PortalGateLook).GetMethod(nameof(PortalGateLook.InnerYaw))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PortalView), "Update", typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.ServerTimestamp)).GetGetMethod()),
                "from the server clock, so every client turns it alike");
            Assert.IsTrue(IlWiring.Uses(typeof(MineView), "LateUpdate", typeof(MineView).GetMethod(nameof(MineView.PaintCap))), "the mine blinks its cap");
            Assert.IsTrue(IlWiring.Uses(typeof(MineView), "LateUpdate", typeof(Mine).GetProperty(nameof(Mine.SecondsSincePlaced)).GetGetMethod()),
                "from its age since placing, which every client agrees on");
            Assert.IsTrue(IlWiring.Uses(typeof(MineView), nameof(MineView.PaintCap), typeof(MinePuckLook).GetMethod(nameof(MinePuckLook.CapLight))));
            Assert.IsTrue(IlWiring.Uses(typeof(FenceCageView), "LateUpdate", typeof(FenceCageView).GetMethod(nameof(FenceCageView.Reshape))), "the fence reshapes its arcs");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(FenceCageView), "LateUpdate", typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.ServerTimestamp)).GetGetMethod()),
                "from the server clock, like the stun arcs");
            Assert.IsTrue(IlWiring.Uses(typeof(FenceCageView), nameof(FenceCageView.Reshape), typeof(StunArcsRule).GetMethod(nameof(StunArcsRule.Seed))));
        }

        [Test]
        public void TheHexDiscIsAFlatHexagonOneMetreAcross()
        {
            Mesh disc = AssetDatabase.LoadAssetAtPath<Mesh>(HexDiscMesh.AssetPath);
            Assert.IsNotNull(disc, HexDiscMesh.AssetPath);
            Mesh built = HexDiscMesh.Create();
            try
            {
                Assert.AreEqual(built.vertices, disc.vertices, "the asset is what the builder makes");
                Assert.AreEqual(built.triangles, disc.triangles);
            }
            finally
            {
                Object.DestroyImmediate(built);
            }
            Assert.AreEqual(6, disc.triangles.Length / 3, "six triangles round the middle");
            Assert.AreEqual(0f, disc.bounds.size.y, 1e-6f, "flat");
            Assert.AreEqual(new Vector3(0f, 0f, 0.5f), disc.vertices[1], "a corner toward +Z, like the gate's frame, at half a metre");
            foreach (Vector3 n in disc.normals)
                Assert.AreEqual(Vector3.up, n, "facing up");
            Vector3 a = disc.vertices[disc.triangles[0]], b = disc.vertices[disc.triangles[1]], c = disc.vertices[disc.triangles[2]];
            Assert.Greater(Vector3.Cross(b - a, c - a).y, 0f, "wound to face up");
        }

        [Test]
        public void TheWallShaderReadsTheCoverEdgeAndNothingSetsItForTheWholeArena()
        {
            string hlsl = System.IO.File.ReadAllText("Assets/Gameplay/Arena/Shaders/ArenaHex.hlsl");
            string name = CoverPanelLook.PropertyName;
            StringAssert.Contains("float4 " + name + ";", hlsl, "the wall shader declares the edge the cover's property block fills");
            StringAssert.Contains("s.emission += " + name + ".rgb", hlsl, "and lights the band with it");
            var setGlobal = new System.Text.RegularExpressions.Regex(@"SetGlobal\w*\s*\([^;]*(" + name + @"|CoverPanelLook\.PropertyName)");
            foreach (string file in System.IO.Directory.GetFiles("Assets/scripts", "*.cs", System.IO.SearchOption.AllDirectories))
                Assert.IsFalse(setGlobal.IsMatch(System.IO.File.ReadAllText(file)), file + " sets the cover edge as a global: every static arena wall would light up");
        }

        // ---- helpers ----

        private static T Part<T>(MovementLookTestKit kit, GameObject parent, string name) where T : Component
        {
            GameObject part = kit.NewObject(name);
            part.transform.SetParent(parent.transform, false);
            T component = part.AddComponent<T>();
            if (component is MeshRenderer mesh)
            {
                part.AddComponent<MeshFilter>();
                mesh.sharedMaterial = kit.Fx;
            }
            if (component is LineRenderer line)
                line.sharedMaterial = kit.Fx;
            return component;
        }

        private static void Wire(Component view, MovementLookTestKit kit, int abilityId, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(view);
            so.FindProperty("theme").objectReferenceValue = kit.Theme;
            so.FindProperty("abilityId").intValue = abilityId;
            foreach (var (field, value) in refs)
                so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(SerializedObject so, string field, params Object[] values)
        {
            SerializedProperty array = so.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void SetOwnerTeam(NetworkedDeployable deployable, int team) =>
            typeof(NetworkedDeployable).GetProperty(nameof(NetworkedDeployable.OwnerTeam)).GetSetMethod(true).Invoke(deployable, new object[] { team });

        private static void SetViewId(Component deployable, int id) =>
            typeof(PhotonView).GetField("viewIdField", Any).SetValue(deployable.GetComponent<PhotonView>(), id);

        private static int Count<T>() where T : Object =>
            Resources.FindObjectsOfTypeAll<T>().Count(o => o != null && !EditorUtility.IsPersistent(o));
    }
}
