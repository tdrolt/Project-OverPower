using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Vision;
using Overpower.Weapons;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The two danger ultimates in red: the AoE Zone a flat ring of flickering hexagon cells inside a hard edge at its real radius,
    /// the Raybeam one hard beam per hit line at its real width with a white-hot core, and a scorch where a beam stops on a surface. Every
    /// colour comes from the ability's row in the look table (Gold here, so a view that hard-codes red is caught).</summary>
    public class DangerLookTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private const int RaybeamId = 24, AoeId = 27;
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private const string FxAlphaPath = "Assets/Gameplay/FX/Materials/FX Alpha.mat";
        private const string ZonePath = "Assets/Resources/AoE Zone.prefab";
        private const string RaybeamPath = "Assets/Gameplay/Abilities/Raybeam.prefab";
        private const string BeamPath = "Assets/Gameplay/FX/Raybeam Beam.prefab";
        private const string ScorchPath = "Assets/Gameplay/FX/Impacts/FX Raybeam Scorch.prefab";
        private const string LaserBeamPath = "Assets/Gameplay/Projectiles/Laser Beam VFX.prefab";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color White = new Color(0.93f, 0.96f, 0.99f, 1f);

        // ---- AoE Zone rules ----

        [Test]
        public void AoeCellsLieInsideTheRealRadiusInABandAtItsEdge()
        {
            var cells = new List<Vector2>();
            AoeRingLook.CellCentres(5f, 1.6f, 0.7f, cells);
            Assert.Greater(cells.Count, 20, "a full ring of cells round a 5 m zone");
            float whole = 0.7f / Mathf.Sqrt(3f);
            foreach (Vector2 c in cells)
            {
                Assert.LessOrEqual(c.magnitude + whole, 5f + 1e-4f, $"cell {c} stays inside the real radius, so the ring never draws danger past it");
                Assert.GreaterOrEqual(c.magnitude, 5f - 1.6f - 1e-4f, $"cell {c} lies in the band at the edge, the middle stays clear");
            }
            var again = new List<Vector2>();
            AoeRingLook.CellCentres(5f, 1.6f, 0.7f, again);
            Assert.AreEqual(cells, again, "the same cells in the same order on every client, so a cell's flicker index means the same cell");
            var wider = new List<Vector2>();
            AoeRingLook.CellCentres(8f, 1.6f, 0.7f, wider);
            Assert.Greater(wider.Count, cells.Count, "a bigger zone draws a longer ring");
            for (int i = 1; i < cells.Count; i++)
                for (int j = 0; j < i; j++)
                    Assert.GreaterOrEqual((cells[i] - cells[j]).magnitude, 0.7f - 1e-3f, "cells sit on one hexagon grid, never overlapping");
        }

        [Test]
        public void AoeCellCornersMakeAPointedHexagonInsideItsGap()
        {
            float outer = AoeRingLook.Circumradius(0.7f, 0.1f);
            Assert.AreEqual(0.6f / Mathf.Sqrt(3f), outer, 1e-5f, "the gap between cells comes off the cell's own width");
            Assert.AreEqual(0f, AoeRingLook.Circumradius(0.7f, 2f), 1e-6f, "never a negative size");
            Vector2 c = new Vector2(1f, 2f);
            Assert.AreEqual(1f, AoeRingLook.Corner(c, 0.5f, 0).x, 1e-5f, "a corner points to +Z, as the floor's hexagons");
            Assert.AreEqual(2.5f, AoeRingLook.Corner(c, 0.5f, 0).y, 1e-5f);
            for (int k = 0; k < 6; k++)
                Assert.AreEqual(0.5f, (AoeRingLook.Corner(c, 0.5f, k) - c).magnitude, 1e-5f);
            Assert.Less((AoeRingLook.Corner(c, 0.5f, 0) - AoeRingLook.Corner(c, 0.5f, 6)).magnitude, 1e-5f, "six corners round");
        }

        [Test]
        public void AoeCellsFlickerFromTheSeedAlone()
        {
            Assert.IsFalse(Enumerable.Range(0, 200).Any(i => AoeRingLook.Lit(99, i, 0f)), "a share of 0 never lights a cell");
            Assert.IsTrue(Enumerable.Range(0, 200).All(i => AoeRingLook.Lit(99, i, 1f)), "a share of 1 lights every cell");
            int lit = Enumerable.Range(0, 400).Count(i => AoeRingLook.Lit(1234, i, 0.35f));
            Assert.That(lit, Is.InRange(100, 180), "about Aoe Lit Share of the cells are lit at once");
            for (int i = 0; i < 50; i++)
                Assert.AreEqual(AoeRingLook.Lit(1234, i, 0.35f), AoeRingLook.Lit(1234, i, 0.35f), "the same seed lights the same cells on every client");
            Assert.IsTrue(Enumerable.Range(0, 50).Any(i => AoeRingLook.Lit(1234, i, 0.35f) != AoeRingLook.Lit(1235, i, 0.35f)), "a new seed lights other cells");

            var danger = new Color(1f, 0.2f, 0.1f, 1f);
            Assert.AreEqual(new Color(1f, 0.2f, 0.1f, 0.8f), AoeRingLook.Line(danger, true, 0.8f, 0.3f), "a lit cell's outline");
            Assert.AreEqual(new Color(1f, 0.2f, 0.1f, 0.3f), AoeRingLook.Line(danger, false, 0.8f, 0.3f), "a dim cell's outline");
            Assert.AreEqual(new Color(1f, 0.2f, 0.1f, 0.25f), AoeRingLook.Fill(danger, true, 0.25f), "a lit cell fills faintly");
            Assert.AreEqual(0f, AoeRingLook.Fill(danger, false, 0.25f).a, 1e-6f, "a dim cell is empty");
        }

        // ---- AoE Zone view ----

        [Test]
        public void TheAoeZoneIsAHexCellRingWithAHardEdgeAtItsRealRadius()
        {
            using (var kit = new MovementLookTestKit(AoeId, LookShape.HexGridRing))
            {
                SetAoeLook(kit.Look);
                var parts = NewZone(kit, 3.7f, owner: 7, out AoeZone zone, out AoeZoneView view);
                int renderers = zone.GetComponentsInChildren<Renderer>(true).Length;

                view.OnDeployablePlaced(zone);

                Assert.AreEqual(renderers, zone.GetComponentsInChildren<Renderer>(true).Length,
                    "every renderer is on the prefab before placing, so the zone's fog gate (collected in Awake) hides all of it");
                LineRenderer edge = parts.edge;
                Assert.IsTrue(edge.loop, "the edge is a closed ring");
                Assert.GreaterOrEqual(edge.positionCount, 48, "round enough to read as the circle the damage really reaches");
                for (int i = 0; i < edge.positionCount; i++)
                    Assert.AreEqual(3.7f, new Vector2(edge.GetPosition(i).x, edge.GetPosition(i).y).magnitude, 1e-4f, "the edge sits on the zone's real Radius");
                Assert.AreEqual(0.11f, edge.startWidth, 1e-5f, "Aoe Edge Width");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, edge.startColor, "the edge wears the AoE Zone's row in the look table");
                Assert.AreEqual(0.95f, edge.startColor.a, 1f / 255f, "Aoe Edge Alpha");

                Mesh mesh = parts.cells.sharedMesh;
                Assert.IsNotNull(mesh, "the cells are one mesh");
                Assert.AreEqual(AoeRingMesh.Name, mesh.name);
                var centres = new List<Vector2>();
                AoeRingLook.CellCentres(3.7f, 1.2f, 0.6f, centres);
                Assert.AreEqual(centres.Count * AoeRingMesh.VerticesPerCell, mesh.vertexCount, "one hexagon per cell of the rule");
                foreach (Vector3 v in mesh.vertices)
                {
                    Assert.LessOrEqual(new Vector2(v.x, v.z).magnitude, 3.7f + 1e-3f, "no cell reaches past the real radius");
                    Assert.AreEqual(0f, v.y, 1e-6f, "flat on the floor");
                }
                foreach (Color c in mesh.colors)
                    MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold.linear, c,
                        "the cells wear the AoE Zone's row, handed over linear like the feet hex (a mesh's vertex colours are not converted)");

                Vector3[] vertices = mesh.vertices;
                Vector3 centre = new Vector3(centres[0].x, 0f, centres[0].y);
                float outer = (vertices[0] - centre).magnitude, inner = (vertices[6] - centre).magnitude;
                float expectedOuter = AoeRingLook.Circumradius(0.6f, 0.08f), expectedLine = 0.04f * 2f / Mathf.Sqrt(3f);
                AssertAll(
                    (Mathf.Abs(expectedOuter - outer) < 1e-4f, $"a cell is drawn Aoe Cell Gap smaller than its grid cell: expected {expectedOuter} but was {outer}"),
                    (Mathf.Abs(expectedLine - (outer - inner)) < 1e-4f, $"its outline is Aoe Cell Line Width thick: expected {expectedLine} but was {outer - inner}"));
            }
        }

        [Test]
        public void TheAoeCellsFlickerFromTheServerClockAndTheCaster()
        {
            using (var kit = new MovementLookTestKit(AoeId, LookShape.HexGridRing))
            {
                SetAoeLook(kit.Look);
                var parts = NewZone(kit, 4f, owner: 7, out AoeZone zone, out AoeZoneView view);
                view.OnDeployablePlaced(zone);
                var centres = new List<Vector2>();
                AoeRingLook.CellCentres(4f, 1.2f, 0.6f, centres);

                view.Flicker(2000);
                int seed = StunArcsRule.Seed(2000, 7, 8f);
                Color[] colours = parts.cells.sharedMesh.colors;
                for (int i = 0; i < centres.Count; i++)
                {
                    bool lit = AoeRingLook.Lit(seed, i, 0.4f);
                    Assert.AreEqual(lit ? 0.9f : 0.2f, colours[i * AoeRingMesh.VerticesPerCell].a, 1e-3f, $"cell {i} outline from the server clock's seed and the caster");
                    Assert.AreEqual(lit ? 0.3f : 0f, colours[i * AoeRingMesh.VerticesPerCell + AoeRingMesh.FillStart].a, 1e-3f, $"cell {i} fill");
                }
                var sentinel = new Color[colours.Length];
                parts.cells.sharedMesh.colors = sentinel;
                view.Flicker(2010);
                Assert.AreEqual(sentinel, parts.cells.sharedMesh.colors, "within one step of Aoe Flicker Rate the colours are not written again");
                parts.cells.sharedMesh.colors = colours;
                view.Flicker(2000 + 3 * 125);
                Assert.AreNotEqual(colours, parts.cells.sharedMesh.colors, "a later step lights other cells");
            }
        }

        [Test]
        public void TheAoeViewMakesOneMeshAndFreesIt()
        {
            using (var kit = new MovementLookTestKit(AoeId, LookShape.HexGridRing))
            {
                SetAoeLook(kit.Look);
                int meshes = LiveMeshes(), materials = Count<Material>();
                NewZone(kit, 5f, owner: 3, out AoeZone zone, out AoeZoneView view);
                view.OnDeployablePlaced(zone);
                view.Flicker(4000);
                view.Flicker(9000);
                Assert.AreEqual(meshes + 1, LiveMeshes(), "the cells are one runtime mesh");
                Assert.AreEqual(materials, Count<Material>(), "colours go through vertex colours, never a material copy");
                MovementLookTestKit.DestroyLikeTheGame(zone.gameObject);
                Assert.AreEqual(meshes, LiveMeshes(), "the mesh leaked past its zone");
            }
        }

        [Test]
        public void TheReloadSweepFreesOnlyRuntimeRings()
        {
            MethodInfo sweep = typeof(AoeZoneView).GetMethod("FreeLeftovers", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(sweep);
            Mesh leftover = AoeRingMesh.Create(new List<Vector2> { Vector2.zero }, 0.3f, 0.04f); // its zone is gone, as after a domain reload
            var saved = new Mesh { name = AoeRingMesh.Name }; // not DontSave: someone else's, never swept
            try
            {
                sweep.Invoke(null, null);
                Assert.IsTrue(leftover == null, "the leftover runtime ring is freed by name");
                Assert.IsTrue(saved != null, "a mesh that is not a runtime ring stays");
            }
            finally
            {
                if (leftover != null) Object.DestroyImmediate(leftover);
                Object.DestroyImmediate(saved);
            }
        }

        [Test]
        public void TheAoeRingLiesOnTheFloorAtAoeLift()
        {
            using (var kit = new MovementLookTestKit(AoeId, LookShape.HexGridRing))
            {
                SetAoeLook(kit.Look);
                NewZone(kit, 4f, owner: 2, out AoeZone zone, out AoeZoneView view);
                Floor(kit, FarAway, 0.2f);
                zone.transform.position = FarAway + new Vector3(0f, 0.7f, 0f); // on the caster's root, half a metre over his feet
                view.OnDeployablePlaced(zone);

                LateUpdate(view);

                Assert.AreEqual(0.2f + 0.05f, Ring(view).position.y, 1e-4f, "Aoe Lift over the floor under the zone, not at the zone's own height");
            }
        }

        [Test]
        public void TheAoeRingFindsTheFloorAgainWhenTheZoneMoves()
        {
            using (var kit = new MovementLookTestKit(AoeId, LookShape.HexGridRing))
            {
                SetAoeLook(kit.Look);
                NewZone(kit, 4f, owner: 2, out AoeZone zone, out AoeZoneView view);
                Floor(kit, FarAway, 0.2f);
                Vector3 step = FarAway + new Vector3(30f, 0f, 0f);
                Floor(kit, step, -0.4f);
                zone.transform.position = FarAway + new Vector3(0f, 0.7f, 0f);
                view.OnDeployablePlaced(zone);
                LateUpdate(view);
                float lift = Ring(view).position.y - 0.2f;

                zone.transform.position = step + new Vector3(0f, 0.7f, 0f); // over a lower floor at the same height: a caster off a ledge
                LateUpdate(view);

                Assert.AreEqual(-0.4f + lift, Ring(view).position.y, 1e-4f, "the ring follows the floor under a zone that follows its caster");
            }
        }

        [Test]
        public void TheAoeViewFlickersGroundsAndReadsTheRealRadius()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneView), nameof(AoeZoneView.OnDeployablePlaced), typeof(AoeZone).GetProperty(nameof(AoeZone.Radius)).GetGetMethod()),
                "the ring is drawn from the radius the damage reads");
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneView), nameof(AoeZoneView.OnDeployablePlaced), typeof(AoeRingLook).GetMethod(nameof(AoeRingLook.CellCentres))));
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneView), "LateUpdate", typeof(AoeZoneView).GetMethod(nameof(AoeZoneView.Flicker))), "the cells flicker every frame");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(AoeZoneView), "LateUpdate", typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.ServerTimestamp)).GetGetMethod()),
                "from the server clock, so every client flickers alike");
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneView), nameof(AoeZoneView.Flicker), typeof(StunArcsRule).GetMethod(nameof(StunArcsRule.Seed))));
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneView), nameof(AoeZoneView.Flicker), typeof(AoeRingLook).GetMethod(nameof(AoeRingLook.Lit))));
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneView), "LateUpdate",
                    typeof(GroundSnap).GetMethod(nameof(GroundSnap.TryFindGroundY), new[] { typeof(Vector3), typeof(float).MakeByRefType() })),
                "the ring lies on the floor under the zone, which follows the caster's root half a metre up");
        }

        [Test]
        public void TheAoeZonePrefabDrawsTheRingUnderItsFogGateWithNoCollider()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZonePath);
            Assert.IsNotNull(prefab, ZonePath);
            Assert.IsNull(prefab.transform.Find("Visual"), "the old flat grey disc is gone");
            Assert.IsNull(Ref(prefab.GetComponent<AoeZone>(), "visual"), "nothing is stretched to the radius: the view draws at it");
            var gate = prefab.GetComponent<VisibleWhenSeen>();
            Assert.IsNotNull(gate, "the zone's own fog gate stays on the root");
            Assert.IsTrue(new SerializedObject(gate).FindProperty("ownersTeamAlwaysSees").boolValue, "my team always sees its own zone, as before");

            var view = prefab.GetComponent<AoeZoneView>();
            Assert.IsNotNull(view);
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            Assert.AreEqual(AoeId, new SerializedObject(view).FindProperty("abilityId").intValue);
            var ring = (Transform)Ref(view, "ring");
            var cells = (MeshFilter)Ref(view, "cells");
            var edge = (LineRenderer)Ref(view, "edge");
            Assert.IsNotNull(ring);
            Assert.IsTrue(cells.transform.IsChildOf(ring) && edge.transform.IsChildOf(ring), "both parts ride on the grounded ring");
            Assert.IsTrue(ring.IsChildOf(prefab.transform));
            Assert.IsNull(cells.sharedMesh, "the cells are built at the real radius when the zone is placed");
            var cellRenderer = cells.GetComponent<MeshRenderer>();
            Assert.AreEqual(FxAlphaPath, AssetDatabase.GetAssetPath(cellRenderer.sharedMaterial), "see-through, vertex-coloured");
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, cellRenderer.shadowCastingMode);
            Assert.AreEqual(FxAlphaPath, AssetDatabase.GetAssetPath(edge.sharedMaterial));
            Assert.AreEqual(LineAlignment.TransformZ, edge.alignment, "the edge lies flat");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(90f, 0f, 0f), edge.transform.localRotation), 0.01f);
            Assert.AreEqual(0, edge.numCapVertices, "hard ends, no round caps");
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, edge.shadowCastingMode);
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), "a look never adds a collider");
        }

        // ---- Raybeam ----

        [Test]
        public void TheRaybeamDrawsOneHardBeamAtItsRealWidthWithAWhiteHotCore()
        {
            using (var kit = new MovementLookTestKit(RaybeamId, LookShape.Beam))
            {
                SetBeamLook(kit);
                GameObject abilityGo = kit.NewObject("raybeam test");
                RaybeamAbility ability = abilityGo.AddComponent<RaybeamAbility>();
                var so = new SerializedObject(ability);
                so.FindProperty("beamWidth").floatValue = 0.5f;
                so.ApplyModifiedPropertiesWithoutUndo();
                RaybeamBeamView view = NewBeam(kit, out LineRenderer sheath, out LineRenderer core);
                var from = new Vector3(1f, 1.2f, 2f);
                var to = new Vector3(9f, 1.2f, 14f);

                ability.ShowBeam(view.gameObject, from, to, 1);

                Assert.AreEqual(2, sheath.positionCount);
                Assert.AreEqual(from, sheath.GetPosition(0), "from the beam's own origin");
                Assert.AreEqual(to, sheath.GetPosition(1), "to where the beam really ended");
                Assert.IsTrue(sheath.useWorldSpace);
                Assert.AreEqual(0.5f, sheath.startWidth, 1e-5f, "the sheath is exactly as wide as the beam that hits (Beam Width)");
                Assert.AreEqual(0.5f, sheath.endWidth, 1e-5f, "one width all along: a hard beam, not a taper");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, sheath.startColor, "the sheath wears the Raybeam's row in the look table");
                Assert.AreEqual(0.85f, sheath.startColor.a, 1f / 255f, "Raybeam Sheath Alpha");
                Assert.AreEqual(from, core.GetPosition(0));
                Assert.AreEqual(to, core.GetPosition(1));
                Assert.AreEqual(0.5f * 0.3f, core.startWidth, 1e-5f, "Raybeam Core Share of the beam");
                MovementLookTestKit.AssertRgb(White, core.startColor, "a white-hot core (Flash White)");
                var block = new MaterialPropertyBlock();
                core.GetPropertyBlock(block);
                Assert.AreEqual(1.7f, block.GetColor(BaseColorId).r, 1e-4f, "Raybeam Core Glow");
                Assert.Greater(core.sortingOrder, sheath.sortingOrder, "the core draws over the sheath");
            }
        }

        [Test]
        public void TheRaybeamViewMakesNoMeshOrMaterial()
        {
            using (var kit = new MovementLookTestKit(RaybeamId, LookShape.Beam))
            {
                SetBeamLook(kit);
                int meshes = Count<Mesh>(), materials = Count<Material>();
                RaybeamBeamView view = NewBeam(kit, out _, out _);
                view.Draw(Vector3.zero, Vector3.forward * 10f, 0.6f, 0);
                view.Draw(Vector3.zero, Vector3.right * 10f, 0.6f, 2);
                Assert.AreEqual(meshes, Count<Mesh>());
                Assert.AreEqual(materials, Count<Material>(), "colours go through line colours and a property block, never a material copy");
            }
        }

        [Test]
        public void WithoutItsLookTheBeamInventsNoCore()
        {
            using (var kit = new MovementLookTestKit(RaybeamId, LookShape.Beam))
            {
                RaybeamBeamView view = NewBeam(kit, out LineRenderer sheath, out LineRenderer core);
                MovementLookTestKit.SetTheme(view, null);
                view.Draw(Vector3.zero, Vector3.forward * 10f, 0.6f, 0);
                Assert.AreEqual(0.6f, sheath.startWidth, 1e-5f, "the beam itself still shows at its real width");
                Assert.AreEqual(0f, core.startWidth, 1e-6f, "no copy of a tuning number stands in for Raybeam Core Share");
            }
        }

        [Test]
        public void AScorchFlashesOnlyWhereABeamStopsOnASurfaceMyTeamSees()
        {
            var none = new List<BeamContact>();
            Assert.IsTrue(RaybeamLook.EndsOnSurface(new BeamResult(none, 6f, true)), "a wall stopped it");
            var cover = new List<BeamContact> { new BeamContact(2f, null, Vector3.zero), new BeamContact(4f, null, Vector3.zero, isStructure: true) };
            Assert.IsTrue(RaybeamLook.EndsOnSurface(new BeamResult(cover, 4f, false)), "a placed cover took the beam and stopped it");
            var players = new List<BeamContact> { new BeamContact(2f, null, Vector3.zero), new BeamContact(4f, null, Vector3.zero) };
            Assert.IsFalse(RaybeamLook.EndsOnSurface(new BeamResult(players, 28.8f, false)), "it pierced the players and ran out its range in the open");
            Assert.IsFalse(RaybeamLook.EndsOnSurface(new BeamResult(none, 28.8f, false)), "nothing at all in the way");

            Assert.IsTrue(RaybeamLook.ShowsScorch(true, true));
            Assert.IsFalse(RaybeamLook.ShowsScorch(true, false), "an enemy beam's scorch in the fog is not shown, as a gun's impact");
            Assert.IsFalse(RaybeamLook.ShowsScorch(false, true), "no surface, no scorch");
            Assert.AreEqual(0.21f, RaybeamLook.CoreWidth(0.7f, 0.3f), 1e-5f);
            Assert.AreEqual(0.7f, RaybeamLook.CoreWidth(0.7f, 3f), 1e-5f, "never wider than the beam");
        }

        [Test]
        public void TheRaybeamDrawsThroughTheViewAndScorchesThroughTheRule()
        {
            MethodInfo show = typeof(RaybeamAbility).GetMethod(nameof(RaybeamAbility.ShowBeam), Any);
            AssertAll(
                (IlWiring.ResultDecidesABranch(typeof(RaybeamAbility), "DrawBeam", typeof(TeamSight).GetMethod(nameof(TeamSight.ShotShownAlong))),
                    "an enemy's beam is made only where its line crosses my team's sight"),
                (IlWiring.Uses(typeof(RaybeamAbility), "DrawBeam", show),
                    "every drawn beam goes through the view hand-over"),
                (IlWiring.Uses(typeof(RaybeamAbility), nameof(RaybeamAbility.ShowBeam), typeof(RaybeamBeamView).GetMethod(nameof(RaybeamBeamView.Draw))),
                    "RaybeamBeamView.Draw"),
                (IlWiring.Uses(typeof(RaybeamAbility), nameof(RaybeamAbility.ShowBeam), typeof(RaybeamAbility).GetField("beamWidth", Any)),
                    "the drawn width is Beam Width, the number the hit reads"),
                (IlWiring.Uses(typeof(RaybeamBeamView), nameof(RaybeamBeamView.Draw), typeof(RaybeamLook).GetMethod(nameof(RaybeamLook.CoreWidth))),
                    "RaybeamLook.CoreWidth"),
                (IlWiring.ResultDecidesABranch(typeof(RaybeamAbility), "FireOneBeam", typeof(RaybeamLook).GetMethod(nameof(RaybeamLook.ShowsScorch))),
                    "the scorch plays only when the rule says so"),
                (IlWiring.Uses(typeof(RaybeamAbility), "FireOneBeam", typeof(RaybeamLook).GetMethod(nameof(RaybeamLook.EndsOnSurface))),
                    "RaybeamLook.EndsOnSurface"),
                (IlWiring.Uses(typeof(RaybeamAbility), "FireOneBeam", typeof(TeamSight).GetMethod(nameof(TeamSight.ShotShownAt))),
                    "gated like a gun's impact"),
                (IlWiring.Uses(typeof(RaybeamAbility), "FireOneBeam", typeof(RaybeamLook).GetMethod(nameof(RaybeamLook.ScorchSpot))),
                    "the scorch lies where the rule puts it"),
                (IlWiring.Uses(typeof(RaybeamAbility), "FireOneBeam", typeof(VFXManager).GetMethod(nameof(VFXManager.PlayVFX), new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) })),
                    "pooled like the gun impacts, turned to lie on the surface"));
        }

        [Test]
        public void TheScorchLiesOnTheSurfaceTheBeamStoppedAgainst()
        {
            Vector3 aim = Vector3.forward, end = new Vector3(0f, 1f, 5.66f);
            var wallPoint = new Vector3(0.2f, 1.1f, 6f);
            Vector3 wallNormal = new Vector3(-0.6f, 0f, -0.8f);
            var contacts = new List<BeamContact> { new BeamContact(9f, null, new Vector3(0f, 1f, 9.3f)), new BeamContact(5.66f, null, wallPoint) };
            var normals = new List<Vector3> { Vector3.back, wallNormal };

            RaybeamLook.ScorchSpot(contacts, normals, new BeamResult(new List<BeamContact>(), 5.66f, true), end, aim, out Vector3 at, out Quaternion facing);
            Assert.AreEqual(wallPoint, at, "on the wall where the beam touched it, not where the swept sphere's centre stopped half a Beam Width short");
            Assert.Less(Vector3.Angle(wallNormal, facing * Vector3.forward), 0.01f, "lying flat on that wall, facing out of it");

            var cover = new BeamContact(4f, null, new Vector3(0.1f, 1.2f, 4.3f), isStructure: true);
            RaybeamLook.ScorchSpot(new List<BeamContact> { cover }, new List<Vector3> { Vector3.left },
                new BeamResult(new List<BeamContact> { cover }, 4f, false), end, aim, out at, out facing);
            Assert.AreEqual(cover.Point, at, "on the placed cover that stopped it");
            Assert.Less(Vector3.Angle(Vector3.left, facing * Vector3.forward), 0.01f);

            RaybeamLook.ScorchSpot(new List<BeamContact> { new BeamContact(0f, null, Vector3.zero) }, new List<Vector3> { Vector3.back },
                new BeamResult(new List<BeamContact>(), 0f, true), end, aim, out at, out facing);
            Assert.AreEqual(end, at, "a wall the gun was already inside has no touch point: the beam's end");
            Assert.Less(Vector3.Angle(-aim, facing * Vector3.forward), 0.01f, "facing back along the beam");
        }

        [Test]
        public void TheRaybeamPrefabDrawsTheNewBeamAndScorchAndLeavesTheLasersAlone()
        {
            GameObject raybeam = AssetDatabase.LoadAssetAtPath<GameObject>(RaybeamPath);
            var ability = raybeam.GetComponent<RaybeamAbility>();
            Assert.AreEqual(BeamPath, AssetDatabase.GetAssetPath(Ref(ability, "beamVfx")), "the Raybeam has its own beam");
            Assert.AreEqual(ScorchPath, AssetDatabase.GetAssetPath(Ref(ability, "scorchVfx")));

            GameObject beam = AssetDatabase.LoadAssetAtPath<GameObject>(BeamPath);
            var view = beam.GetComponent<RaybeamBeamView>();
            Assert.IsNotNull(view);
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            Assert.AreEqual(RaybeamId, new SerializedObject(view).FindProperty("abilityId").intValue);
            foreach (string part in new[] { "sheath", "core" })
            {
                var line = (LineRenderer)Ref(view, part);
                Assert.IsNotNull(line, part);
                Assert.IsTrue(line.transform.IsChildOf(beam.transform));
                Assert.AreEqual(FxAlphaPath, AssetDatabase.GetAssetPath(line.sharedMaterial), part + " colour must read over any floor");
                Assert.AreEqual(0, line.numCapVertices, part + ": hard ends");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, line.shadowCastingMode, part);
                Assert.IsNull(line.sharedMaterial.GetTexture("_BaseMap"), part + ": no soft texture, a hard edge");
            }
            Assert.IsEmpty(beam.GetComponentsInChildren<Collider>(true));

            GameObject scorch = AssetDatabase.LoadAssetAtPath<GameObject>(ScorchPath);
            Assert.IsNotNull(scorch, ScorchPath);
            Assert.IsNotEmpty(scorch.GetComponentsInChildren<ParticleSystem>(true), "VFXManager times a flash from its particle systems");
            foreach (ParticleSystem system in scorch.GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.IsFalse(system.main.loop, system.name + " ends on its own");
                Assert.IsTrue(system.main.playOnAwake, system.name + " plays when the pool switches it on");
            }
            Assert.IsEmpty(scorch.GetComponentsInChildren<Collider>(true));
            Assert.IsEmpty(scorch.GetComponentsInChildren<Light>(true), "the few shot lights are the only ones");

            Transform flash = scorch.transform.Find("Flash");
            Assert.IsNotNull(flash, "a flash where the beam touched");
            Assert.AreEqual(ParticleSystemRenderSpace.Local, flash.GetComponent<ParticleSystemRenderer>().alignment,
                "it lies on the surface the scorch is turned to, never a card facing the camera");
            var white = flash.GetComponent<FlashTint>();
            Assert.IsNotNull(white, "a hit is white: the flash takes Flash White, as a gun's impact");
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(white, "theme")));
            Assert.IsNull(flash.GetComponentInParent<LookTint>(true), "nothing paints the flash red");
            Transform sparks = scorch.transform.Find("Sparks");
            Assert.IsNotNull(sparks);
            var tint = sparks.GetComponent<LookTint>();
            Assert.IsNotNull(tint, "the sparks take the Raybeam's row in the look table");
            Assert.AreEqual(RaybeamId, new SerializedObject(tint).FindProperty("abilityId").intValue);
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(tint, "theme")));
            Assert.IsNull(sparks.GetComponentInParent<FlashTint>(true), "nothing paints the sparks white");

            GameObject laser = AssetDatabase.LoadAssetAtPath<GameObject>(LaserBeamPath);
            Assert.IsNull(laser.GetComponent<RaybeamBeamView>(), "the laser guns' shared beam is untouched");
        }

        // ---- helpers ----

        private static void SetAoeLook(ArenaLook look)
        {
            look.aoeRingBand = 1.2f;
            look.aoeCellSize = 0.6f;
            look.aoeCellGap = 0.08f;
            look.aoeCellLineWidth = 0.04f;
            look.aoeEdgeWidth = 0.11f;
            look.aoeEdgeAlpha = 0.95f;
            look.aoeLitShare = 0.4f;
            look.aoeLitLineAlpha = 0.9f;
            look.aoeDimLineAlpha = 0.2f;
            look.aoeLitFillAlpha = 0.3f;
            look.aoeFlickerRate = 8f;
            look.aoeLift = 0.05f;
        }

        private static void SetBeamLook(MovementLookTestKit kit)
        {
            kit.Theme.flashWhiteColor = White;
            kit.Look.raybeamSheathAlpha = 0.85f;
            kit.Look.raybeamCoreShare = 0.3f;
            kit.Look.raybeamCoreGlow = 1.7f;
        }

        private static (MeshFilter cells, LineRenderer edge) NewZone(MovementLookTestKit kit, float radius, int owner, out AoeZone zone, out AoeZoneView view)
        {
            GameObject go = kit.NewObject("aoe view test");
            zone = go.AddComponent<AoeZone>();
            var zoneSo = new SerializedObject(zone);
            zoneSo.FindProperty("radius").floatValue = radius;
            zoneSo.ApplyModifiedPropertiesWithoutUndo();
            GameObject ring = kit.NewObject("Ring");
            ring.transform.SetParent(go.transform, false);
            GameObject cellsGo = kit.NewObject("Cells");
            cellsGo.transform.SetParent(ring.transform, false);
            MeshFilter cells = cellsGo.AddComponent<MeshFilter>();
            cellsGo.AddComponent<MeshRenderer>().sharedMaterial = kit.Fx;
            GameObject edgeGo = kit.NewObject("Edge");
            edgeGo.transform.SetParent(ring.transform, false);
            edgeGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            LineRenderer edge = edgeGo.AddComponent<LineRenderer>();
            edge.sharedMaterial = kit.Fx;
            view = go.AddComponent<AoeZoneView>();
            var so = new SerializedObject(view);
            so.FindProperty("theme").objectReferenceValue = kit.Theme;
            so.FindProperty("abilityId").intValue = AoeId;
            so.FindProperty("ring").objectReferenceValue = ring.transform;
            so.FindProperty("cells").objectReferenceValue = cells;
            so.FindProperty("edge").objectReferenceValue = edge;
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(NetworkedDeployable).GetProperty(nameof(NetworkedDeployable.OwnerTeam)).GetSetMethod(true).Invoke(zone, new object[] { 1 });
            typeof(NetworkedDeployable).GetProperty(nameof(NetworkedDeployable.OwnerActor)).GetSetMethod(true).Invoke(zone, new object[] { owner });
            return (cells, edge);
        }

        // Far from the open scene's own floors, so the ground probe finds only the test's.
        private static readonly Vector3 FarAway = new Vector3(5000f, 0f, 5000f);

        private static void Floor(MovementLookTestKit kit, Vector3 at, float topY)
        {
            GameObject floor = kit.NewObject("aoe test floor");
            floor.transform.position = new Vector3(at.x, topY - 0.5f, at.z);
            floor.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);
            Physics.SyncTransforms();
        }

        private static void LateUpdate(AoeZoneView view) => typeof(AoeZoneView).GetMethod("LateUpdate", Any).Invoke(view, null);

        private static Transform Ring(AoeZoneView view) => (Transform)Ref(view, "ring");

        private static RaybeamBeamView NewBeam(MovementLookTestKit kit, out LineRenderer sheath, out LineRenderer core)
        {
            GameObject go = kit.NewObject("beam view test");
            sheath = Line(kit, go, "Sheath");
            core = Line(kit, go, "Core");
            RaybeamBeamView view = go.AddComponent<RaybeamBeamView>();
            var so = new SerializedObject(view);
            so.FindProperty("theme").objectReferenceValue = kit.Theme;
            so.FindProperty("abilityId").intValue = RaybeamId;
            so.FindProperty("sheath").objectReferenceValue = sheath;
            so.FindProperty("core").objectReferenceValue = core;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        private static LineRenderer Line(MovementLookTestKit kit, GameObject parent, string name)
        {
            GameObject part = kit.NewObject(name);
            part.transform.SetParent(parent.transform, false);
            LineRenderer line = part.AddComponent<LineRenderer>();
            line.sharedMaterial = kit.Fx;
            return line;
        }

        // Every failing check named in one run, so each mutant of a test with several wiring points is seen.
        private static void AssertAll(params (bool ok, string what)[] checks)
        {
            string[] failed = checks.Where(c => !c.ok).Select(c => c.what).ToArray();
            Assert.IsEmpty(failed, "failed: " + string.Join(" | ", failed));
        }

        [Test]
        public void EveryMeshPaintedOnFxAlphaGetsItsColoursLinear()
        {
            var red = new Color(1f, 0.18f, 0.15f, 0.4f);
            Color v = VisualTint.VertexColour(red);
            Assert.AreEqual(red.linear.r, v.r, 1e-5f);
            Assert.AreEqual(red.linear.g, v.g, 1e-5f, "a mesh's vertex colours reach the shader unconverted: hand them over linear");
            Assert.AreEqual(red.linear.b, v.b, 1e-5f);
            Assert.AreEqual(0.4f, v.a, 1e-5f, "opacity is not a colour: it stays as it is");
            MethodInfo vertex = typeof(VisualTint).GetMethod(nameof(VisualTint.VertexColour));
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneView), nameof(AoeZoneView.OnDeployablePlaced), vertex), "the AoE Zone's cells");
            Assert.IsTrue(IlWiring.Uses(typeof(FeetHexView), "Dress", vertex), "the team hex under the feet");
        }

        private static Object Ref(Component component, string field) =>
            new SerializedObject(component).FindProperty(field).objectReferenceValue;

        private static int LiveMeshes() =>
            Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m != null && m.name == AoeRingMesh.Name && !EditorUtility.IsPersistent(m));

        private static int Count<T>() where T : Object =>
            Resources.FindObjectsOfTypeAll<T>().Count(o => o != null && !EditorUtility.IsPersistent(o));
    }
}
