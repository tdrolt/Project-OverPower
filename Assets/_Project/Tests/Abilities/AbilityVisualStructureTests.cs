using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Weapons;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Ability visuals (Tudor, 2026-09-17): each ability's look is built from primitives on its prefab. These pin that
    /// structure - parts, materials, wiring - and that no part carries a collider (a collider would change gameplay).
    /// Read-only asset loads.
    /// </summary>
    public class AbilityVisualStructureTests
    {
        private const string SolidPath = "Assets/_Project/Features/Abilities/Materials/Ability Visual Solid.mat";
        private const string GlassPath = "Assets/_Project/Features/Abilities/Materials/Ability Visual Glass.mat";
        private const string LinePath = "Assets/_Project/Features/UI/AimConeLine.mat";
        private const string ThemePath = "Assets/_Project/Design/Settings/UiTheme.asset";
        private const string MarkerPath = "Assets/_Project/Design/Weapons/Projectiles/Blast Marker.prefab";
        private const string PuckPath = "Assets/_Project/Shared/Art/MinePuck.fbx";
        private const string PylonPath = "Assets/_Project/Shared/Art/FencePylon.fbx";
        private const string PlacedBodyPath = "Assets/_Project/Shared/FX/Materials/Placed Body.mat";
        private const string WallPath = "Assets/_Project/Features/Arena/Materials/Arena Wall.mat";
        private const string CoverPath = "Assets/_Project/Design/Resources/Cover Wall.prefab";

        // A6 (Tudor 2026-09-17 evening): rocket blasts stay at their real height, so step 6 needs a short see-through
        // shell at the burst point instead of a floor BlastMarker. Built here, in the shared kit, so step 6 has it
        // ready - see this file's own extra test below the marker's.
        private const string ShellPath = "Assets/_Project/Design/Weapons/Projectiles/Splash Shell.prefab";

        // A3 (Tudor 2026-09-17 evening): the flamethrower's fan needs a genuine per-vertex tip-to-edge gradient, and
        // GlassPath (URP/Unlit) never reads a mesh's vertex colours at all (verified against the shader's own HLSL
        // source - its Attributes struct has no COLOR semantic). Laser Beam.mat hit exactly this bug on 2026-09-15 and
        // was moved to Particles/Unlit; the fan below gets its OWN new material for the same reason, rather than
        // reshading Ability Visual Glass.mat out from under the mine, portal and fence, which already ship with it.
        private const string FlamePath = "Assets/_Project/Features/Abilities/Materials/Flame Cone.mat";

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        private static Transform Child(Transform parent, string path)
        {
            Transform child = parent.Find(path);
            Assert.IsNotNull(child, $"{parent.name}/{path}");
            return child;
        }

        /// <summary>meshName null = generated at runtime; only the renderer and material are checked.</summary>
        private static void AssertMesh(Transform part, string meshName, string materialPath)
        {
            var filter = part.GetComponent<MeshFilter>();
            var renderer = part.GetComponent<MeshRenderer>();
            Assert.IsNotNull(filter, part.name + " MeshFilter");
            Assert.IsNotNull(renderer, part.name + " MeshRenderer");
            if (meshName != null)
            {
                Assert.IsNotNull(filter.sharedMesh, part.name + " mesh");
                Assert.AreEqual(meshName, filter.sharedMesh.name, part.name + " mesh");
            }
            Assert.AreEqual(materialPath, AssetDatabase.GetAssetPath(renderer.sharedMaterial), part.name + " material");
        }

        private static void AssertMeshAsset(Transform part, string meshPath, string materialPath)
        {
            var filter = part.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, part.name + " MeshFilter");
            Assert.IsNotNull(filter.sharedMesh, part.name + " mesh");
            Assert.AreEqual(meshPath, AssetDatabase.GetAssetPath(filter.sharedMesh), part.name + " mesh");
            var renderer = part.GetComponent<MeshRenderer>();
            Assert.IsNotNull(renderer, part.name + " MeshRenderer");
            Assert.AreEqual(materialPath, AssetDatabase.GetAssetPath(renderer.sharedMaterial), part.name + " material");
        }

        private static LineRenderer AssertLine(Transform part, bool flat)
        {
            var line = part.GetComponent<LineRenderer>();
            Assert.IsNotNull(line, part.name + " LineRenderer");
            Assert.AreEqual(LinePath, AssetDatabase.GetAssetPath(line.sharedMaterial), part.name + " material");
            Assert.AreEqual(flat ? LineAlignment.TransformZ : LineAlignment.View, line.alignment, part.name + " alignment");
            if (flat)
                Assert.Less(Quaternion.Angle(Quaternion.Euler(90f, 0f, 0f), part.localRotation), 0.01f, part.name + " lies flat");
            return line;
        }

        private static void AssertNoCollider(GameObject prefab) =>
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), prefab.name + " must have no collider");

        private static UnityEngine.Object Ref(Component component, string field)
        {
            SerializedProperty property = new SerializedObject(component).FindProperty(field);
            Assert.IsNotNull(property, $"{component.GetType().Name}.{field}");
            return property.objectReferenceValue;
        }

        [Test]
        public void TheVisualMaterialsAreUnlitOneSolidOneSeeThrough()
        {
            var solid = AssetDatabase.LoadAssetAtPath<Material>(SolidPath);
            var glass = AssetDatabase.LoadAssetAtPath<Material>(GlassPath);
            Assert.IsNotNull(solid, SolidPath);
            Assert.IsNotNull(glass, GlassPath);
            Assert.AreEqual("Universal Render Pipeline/Unlit", solid.shader.name);
            Assert.AreEqual("Universal Render Pipeline/Unlit", glass.shader.name);
            Assert.AreEqual(0f, solid.GetFloat("_Surface"), "solid is opaque");
            Assert.AreEqual(1f, glass.GetFloat("_Surface"), "glass is transparent");
            Assert.AreEqual(0f, glass.GetFloat("_ZWrite"), "glass writes no depth, so players stay visible through it");
            Assert.AreEqual(0f, glass.GetFloat("_Cull"), "glass is double-sided");
            Assert.AreEqual(3000, glass.renderQueue);
            Assert.IsTrue(glass.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
        }

        [Test]
        public void TheBlastMarkerIsAFlatDiscAndRimWithNoCollider()
        {
            GameObject prefab = Load(MarkerPath);
            var marker = prefab.GetComponent<BlastMarker>();
            Assert.IsNotNull(marker);
            Transform disc = Child(prefab.transform, "Disc");
            // "pCylinder1" is the REAL name of Unity's builtin Cylinder.fbx mesh on this engine version
            // (verified live: Resources.GetBuiltinResource<Mesh>("Cylinder.fbx").name), not "Cylinder".
            AssertMesh(disc, "pCylinder1", GlassPath);
            Assert.AreEqual(1f, disc.localScale.x, 1e-5f, "unit diameter - Spawn scales it to the blast");
            Assert.AreEqual(1f, disc.localScale.z, 1e-5f);
            LineRenderer rim = AssertLine(Child(prefab.transform, "Rim"), flat: true);
            Assert.AreSame(disc, Ref(marker, "disc"));
            Assert.AreSame(rim, Ref(marker, "rim"));
            AssertNoCollider(prefab);
        }

        [Test]
        public void TheSplashShellIsAFlatShockwaveRingWithNoCollider()
        {
            // A rocket blast is drawn at the real burst point (any height), never snapped to the ground; Spawn sizes the ring to the blast's radius.
            GameObject prefab = Load(ShellPath);
            var shell = prefab.GetComponent<SplashShell>();
            Assert.IsNotNull(shell);
            Assert.IsNull(prefab.transform.Find("Sphere"), "the soft sphere is gone");
            LineRenderer ring = AssertLine(Child(prefab.transform, "Ring"), flat: true);
            Assert.IsTrue(ring.loop, "a closed ring");
            Assert.IsFalse(ring.useWorldSpace, "drawn round the burst point");
            Assert.AreSame(ring, Ref(shell, "ring"));
            AssertNoCollider(prefab);
            Assert.IsNull(prefab.GetComponent<SnapVisualToGround>(), "the ring stays at the real burst height - never snapped to the floor");
        }

        [TestCase("Assets/_Project/Design/Weapons/Projectiles/Rocket.prefab")]
        [TestCase("Assets/_Project/Design/Weapons/Projectiles/Rocket Distance.prefab")]
        [TestCase("Assets/_Project/Design/Weapons/Projectiles/Rocket Cursor.prefab")]
        public void EveryRocketShowsASplashShellAtItsRealBurstPoint(string path)
        {
            // A6: replaces the plan's own printed RocketBlastView (a floor BlastMarker) - the shell stays at the
            // real, off-the-floor burst height instead, so this checks the shell wiring, not a floor marker.
            GameObject prefab = Load(path);
            var view = prefab.GetComponent<RocketBlastView>();
            Assert.IsNotNull(view, path);
            Assert.AreEqual(ShellPath, AssetDatabase.GetAssetPath(Ref(view, "splashShellPrefab")));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
        }

        [Test]
        public void TheFireFieldBurnsWithFloorFlamesNotADisc()
        {
            // The root is placed on the floor at spawn (DetonateAtCursor.OnExpired, GroundSnap on the shooter's client, replicated by
            // PhotonNetwork.Instantiate's position), so nothing in the field snaps to the ground on its own. The
            // burning ground is floor flames in the fire colours, sized to the real Radius when it lights (FireFieldLookTests), and the rim
            // still marks the true edge.
            GameObject prefab = Load("Assets/_Project/Design/Resources/Fire Field.prefab");
            Assert.IsNull(prefab.transform.Find("Visual"), "no flat disc: the burning ground is drawn by its flames");
            Assert.IsEmpty(prefab.GetComponentsInChildren<SnapVisualToGround>(true), "the field's root is placed on the floor at spawn time");
            var field = prefab.GetComponent<FireField>();
            Transform flames = Child(prefab.transform, FireField.FlamesChildName);
            var system = flames.GetComponent<ParticleSystem>();
            Assert.IsNotNull(system, "the floor flames are particles");
            Assert.AreSame(system, Ref(field, "flames"));
            Assert.GreaterOrEqual(flames.GetComponentsInChildren<ParticleSystem>(true).Length, 2, "flames and embers");
            Assert.IsEmpty(prefab.GetComponentsInChildren<MeshRenderer>(true), "no solid disc or mesh left in the field");
            Assert.IsNotNull(prefab.GetComponent<Overpower.Vision.VisibleWhenSeen>(), "the fog gate on the root hides every renderer under it, the flames included");
            LineRenderer rim = AssertLine(Child(prefab.transform, "Rim"), flat: true);
            Assert.AreSame(rim, Ref(field, "rim"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(field, "theme")));
            AssertNoCollider(prefab);
        }

        // ---- ability visuals steps 3-7 add their tests below this line ----

        [Test]
        public void TheMineIsAHexPuckWithATeamCapAndNoCollider()
        {
            GameObject prefab = Load("Assets/_Project/Design/Resources/Mine.prefab");
            Transform visual = Child(prefab.transform, "Visual");
            Assert.AreSame(visual, Ref(prefab.GetComponent<Mine>(), "visual"), "Mine hides this the instant it detonates");
            Assert.AreEqual(Vector3.zero, visual.localPosition);
            Assert.AreEqual(Vector3.one, visual.localScale);
            Assert.IsNotNull(visual.GetComponent<SnapVisualToGround>());
            Transform body = Child(visual, "Body");
            AssertMeshAsset(body, PuckPath, PlacedBodyPath);
            Assert.AreEqual(body.localScale.x, body.localScale.y, 1e-5f, "scaled evenly, so the puck keeps its 45-degree chamfer");
            Assert.AreEqual(body.localScale.x, body.localScale.z, 1e-5f);
            Transform cap = Child(visual, "Cap");
            AssertMeshAsset(cap, HexDiscMesh.AssetPath, PlacedBodyPath);
            float puckTop = body.localPosition.y + body.GetComponent<MeshFilter>().sharedMesh.bounds.max.y * body.localScale.y;
            Assert.Greater(cap.localPosition.y, puckTop, "the light lies on the puck's cap, never inside it");
            Assert.Less(cap.localPosition.y, puckTop + 0.01f, "and right on it");
            Assert.IsNull(visual.Find("Stud"), "the old cube stud is gone");
            Assert.IsNull(visual.Find("Spike 1"), "the old spikes are gone");
            LineRenderer ring = AssertLine(Child(visual, "Trigger Ring"), flat: true);

            var view = prefab.GetComponent<MineView>();
            Assert.IsNotNull(view);
            var so = new SerializedObject(view);
            Assert.AreEqual(1, so.FindProperty("bodyParts").arraySize, "the puck");
            Assert.AreSame(body.GetComponent<MeshRenderer>(), so.FindProperty("bodyParts").GetArrayElementAtIndex(0).objectReferenceValue);
            Assert.AreEqual(1, so.FindProperty("teamParts").arraySize, "the cap, so it hides from enemies with the puck");
            Assert.AreSame(cap.GetComponent<MeshRenderer>(), so.FindProperty("teamParts").GetArrayElementAtIndex(0).objectReferenceValue);
            Assert.AreSame(ring, Ref(view, "triggerRing"));
            Assert.AreSame(visual, Ref(view, "visualRoot"));
            Assert.AreEqual(MarkerPath, AssetDatabase.GetAssetPath(Ref(view, "blastMarkerPrefab")));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<PhotonView>(true).Length);
        }

        [Test]
        public void ThePortalIsAHexGateWithATurningInnerHexAndNoCollider()
        {
            GameObject prefab = Load("Assets/_Project/Design/Resources/Portal.prefab");
            Assert.IsNull(prefab.transform.Find("Visual"), "the old grey cylinder is gone");
            Transform footprint = Child(prefab.transform, "Footprint");
            AssertMeshAsset(footprint, HexDiscMesh.AssetPath, GlassPath);
            Assert.AreEqual(1f, footprint.localScale.x, 1e-5f, "unit diameter - Portal scales it to Portal Diameter");
            Assert.Less(Quaternion.Angle(Quaternion.identity, footprint.localRotation), 0.01f, "a corner toward +Z, under the frame's corners");
            Assert.AreSame(footprint, Ref(prefab.GetComponent<Portal>(), "visual"));
            LineRenderer rim = AssertLine(Child(prefab.transform, "Rim"), flat: true);
            LineRenderer inner = AssertLine(Child(prefab.transform, "Inner Hex"), flat: true);
            Assert.GreaterOrEqual(inner.transform.localPosition.y, footprint.localPosition.y, "the inner hexagon is never under the floor of the gate");
            Transform beacon = Child(prefab.transform, "Owner Beacon");
            AssertMesh(Child(beacon, "Diamond"), "Cube", SolidPath);
            AssertMesh(Child(beacon, "Stem"), "pCylinder1", GlassPath);

            var view = prefab.GetComponent<PortalView>();
            Assert.IsNotNull(view);
            Assert.AreSame(footprint.GetComponent<MeshRenderer>(), Ref(view, "footprint"));
            Assert.AreSame(rim, Ref(view, "rim"));
            Assert.AreSame(inner, Ref(view, "innerHex"));
            Assert.AreSame(beacon.gameObject, Ref(view, "ownerBeacon"));
            Assert.AreSame(Child(beacon, "Diamond").GetComponent<MeshRenderer>(), Ref(view, "beacon"));
            Assert.AreSame(Child(beacon, "Stem").GetComponent<MeshRenderer>(), Ref(view, "stem"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<PhotonView>(true).Length);
        }

        [Test]
        public void TheElectricFenceIsAngularPylonsWithArcsAndNoCollider()
        {
            GameObject prefab = Load("Assets/_Project/Design/Resources/Electric Fence.prefab");
            Assert.IsNull(Ref(prefab.GetComponent<ElectricFence>(), "visual"), "nothing is stretched to the radius any more");
            Assert.IsNull(prefab.transform.Find("Visual"), "the old flat disc is gone");
            Transform cage = Child(prefab.transform, "Cage");
            Assert.IsNotNull(cage.GetComponent<SnapVisualToGround>());
            Transform post = Child(cage, "Post");
            AssertMeshAsset(post, PylonPath, PlacedBodyPath);
            Assert.AreEqual(0f, post.localPosition.y, 1e-5f, "the pylon's foot on the floor (its origin is at its foot)");
            Assert.AreEqual(post.localScale.x, post.localScale.y, 1e-5f, "scaled evenly, so its chamfers stay sharp");
            float pylonTop = post.GetComponent<MeshFilter>().sharedMesh.bounds.max.y * post.localScale.y;

            var view = prefab.GetComponent<FenceCageView>();
            Assert.IsNotNull(view);
            SerializedProperty bars = new SerializedObject(view).FindProperty("bars");
            string[] names = { "Bar Low", "Bar Mid", "Bar Top" };
            Assert.AreEqual(names.Length, bars.arraySize);
            float below = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                LineRenderer bar = AssertLine(Child(cage, names[i]), flat: false);
                Assert.Greater(bar.transform.localPosition.y, below, names[i] + " is above the arc below it");
                below = bar.transform.localPosition.y;
                Assert.AreSame(bar, bars.GetArrayElementAtIndex(i).objectReferenceValue);
            }
            Assert.LessOrEqual(below, pylonTop, "the top arc is no higher than the pylons");
            LineRenderer band = AssertLine(Child(cage, "Band"), flat: true);
            Assert.AreSame(post, Ref(view, "post"));
            Assert.AreSame(band, Ref(view, "band"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(2, prefab.GetComponentsInChildren<PhotonView>(true).Length, "unchanged - flagged for Tudor, not fixed here");
        }

        [Test]
        public void ThePlacedBodyCanGlowSoTheMineCapAndFencePylonsLightUp()
        {
            var body = AssetDatabase.LoadAssetAtPath<Material>(PlacedBodyPath);
            Assert.IsNotNull(body, PlacedBodyPath);
            Assert.IsTrue(body.IsKeywordEnabled("_EMISSION"), "without it the glow each cap and pylon gets through its property block never draws");
            Assert.AreEqual(MaterialGlobalIlluminationFlags.None, body.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack,
                "URP switches the keyword off again whenever the material's own emission counts as black");

            // What the Lit inspector does as soon as someone clicks the material, run on a copy so the asset is never touched.
            var copy = new Material(body);
            try
            {
                System.Type litGui = System.Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader, Unity.RenderPipelines.Universal.Editor");
                Assert.IsNotNull(litGui, "URP's Lit inspector");
                litGui.GetMethod("ValidateMaterial").Invoke(System.Activator.CreateInstance(litGui), new object[] { copy });
                Assert.IsTrue(copy.IsKeywordEnabled("_EMISSION"), "opening the material in the Inspector keeps its glow on");
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        [Test]
        public void TheCoverIsAWallPanelWithTeamEdgesAndOnlyItsBlockingBox()
        {
            GameObject prefab = Load(CoverPath);
            Transform visual = Child(prefab.transform, "Visual");
            AssertMesh(visual, "Cube", WallPath);
            Material wall = visual.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreEqual("Overpower/Arena Surface", wall.shader.name, "the arena's wall look");
            Assert.IsTrue(wall.IsKeywordEnabled("_ARENA_WALL"), "with the wall's circuit stripes");
            var view = prefab.GetComponent<CoverPanelView>();
            Assert.IsNotNull(view);
            Assert.AreSame(visual.GetComponent<MeshRenderer>(), Ref(view, "panel"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
            Assert.AreEqual(1, colliders.Length, "only the box that blocks shots and players");
            Assert.AreSame(prefab.GetComponent<BoxCollider>(), colliders[0]);
        }

        [Test]
        public void EachPlacedObjectWearsTheRowOfTheAbilityThatPlacesIt()
        {
            (string definition, string field, System.Type view)[] placed =
            {
                ("Assets/_Project/Design/Abilities/17 Teleport M.asset", "portalPrefab", typeof(PortalView)),
                ("Assets/_Project/Design/Abilities/19 Mines A.asset", "minePrefab", typeof(MineView)),
                ("Assets/_Project/Design/Abilities/20 Deployable Cover A.asset", "coverPrefab", typeof(CoverPanelView)),
                ("Assets/_Project/Design/Abilities/26 Electric Fence U.asset", "fencePrefab", typeof(FenceCageView)),
            };
            foreach (var (definitionPath, field, viewType) in placed)
            {
                var definition = AssetDatabase.LoadAssetAtPath<Overpower.Data.AbilityDefinition>(definitionPath);
                Assert.IsNotNull(definition, definitionPath);
                var module = (GameObject)new SerializedObject(definition).FindProperty("modulePrefab").objectReferenceValue;
                Assert.IsNotNull(module, definitionPath + " module");
                var placedPrefab = (GameObject)Ref(module.GetComponent<AbilityModule>(), field);
                Component view = placedPrefab.GetComponent(viewType);
                Assert.IsNotNull(view, placedPrefab.name + " " + viewType.Name);
                Assert.AreEqual(definition.Id, new SerializedObject(view).FindProperty("abilityId").intValue,
                    placedPrefab.name + " reads its colour from its own ability's row in the look table");
            }
        }

        [Test]
        public void TheFlameConeMaterialIsUnlitParticlesSoItActuallyReadsVertexColours()
        {
            // A3: this is the guard against the vertex-colour trap - if someone later "simplifies" the fan back onto
            // Ability Visual Glass.mat, this fails loudly instead of quietly flattening the tip-to-edge gradient.
            var flame = AssetDatabase.LoadAssetAtPath<Material>(FlamePath);
            Assert.IsNotNull(flame, FlamePath);
            Assert.AreEqual("Universal Render Pipeline/Particles/Unlit", flame.shader.name,
                "plain URP/Unlit never reads a mesh's vertex colours (Laser Beam.mat's own 2026-09-15 fix) - the " +
                "fan's tip-to-edge gradient needs this shader, not Ability Visual Glass");
            Assert.AreEqual(1f, flame.GetFloat("_Surface"), "transparent");
            Assert.AreEqual(0f, flame.GetFloat("_ZWrite"), "no depth write, so players stay visible through the flame");
            Assert.AreEqual(0f, flame.GetFloat("_Cull"), "double-sided");
        }

        [Test]
        public void TheFlamethrowerDrawsASoftFlameConePrefabWithNoCollider()
        {
            GameObject module = Load("Assets/_Project/Design/Abilities/Prefabs/Flamethrower.prefab");
            var cone = Ref(module.GetComponent<FlamethrowerAbility>(), "sprayVfxPrefab") as FlameConeVisual;
            Assert.IsNotNull(cone, "Flamethrower › Spray Vfx Prefab");
            Assert.AreEqual("Assets/_Project/Design/Abilities/Prefabs/Flamethrower Cone.prefab", AssetDatabase.GetAssetPath(cone));
            Transform fan = Child(cone.transform, "Fan");
            // A3: the fan's own material is FlamePath, not GlassPath - see TheFlameConeMaterialIsUnlitParticles... and
            // this file's own FlamePath comment, above.
            AssertMesh(fan, null, FlamePath); // the fan mesh is generated from Cone Range/Angle at runtime
            LineRenderer edge = AssertLine(Child(cone.transform, "Edge"), flat: true);
            Assert.AreSame(fan.GetComponent<MeshFilter>(), Ref(cone, "fan"));
            Assert.AreSame(edge, Ref(cone, "edge"));
            AssertNoCollider(cone.gameObject);
            AssertNoCollider(module); // AbilityDefinition forbids colliders on a module prefab
        }

        [Test]
        public void TheZipHookIsAHarpoonHeadOnACableWithNoCollider()
        {
            GameObject prefab = Load("Assets/_Project/Design/Weapons/Projectiles/Zip Gun Bullet.prefab");
            Assert.AreEqual(Vector3.one, prefab.transform.localScale, "the old 0.2 sphere scale is gone");
            Assert.IsNull(prefab.GetComponent<MeshRenderer>(), "the head is a child now");
            Assert.IsNotNull(prefab.GetComponent<ProjectileMotor>());
            Assert.IsNotNull(prefab.GetComponent<AbilityHitRelay>());
            Transform head = Child(prefab.transform, "Head");
            // Its drawn size and place along the flight are set at spawn (ZipCableLookTests: ZipBoltView.PlaceHarpoon).
            AssertMesh(head, "HarpoonHead", "Assets/_Project/Shared/FX/Materials/Zip Harpoon.mat");
            var rope = Child(prefab.transform, "Rope").GetComponent<LineRenderer>();
            Assert.IsNotNull(rope);
            Assert.IsTrue(rope.useWorldSpace);
            Assert.AreEqual(2, rope.positionCount);
            Assert.AreEqual("Assets/_Project/Shared/FX/Materials/Zip Cable.mat", AssetDatabase.GetAssetPath(rope.sharedMaterial));
            var view = prefab.GetComponent<ZipBoltView>();
            Assert.IsNotNull(view);
            Assert.AreSame(head.GetComponent<MeshRenderer>(), Ref(view, "head"));
            Assert.AreSame(rope, Ref(view, "rope"));
            AssertNoCollider(prefab);

            var ability = Load("Assets/_Project/Design/Abilities/Prefabs/Zip Gun.prefab").GetComponent<ZipGunAbility>();
            Assert.AreEqual(SolidPath, AssetDatabase.GetAssetPath(Ref(ability, "anchorMaterial")));
        }
    }
}
