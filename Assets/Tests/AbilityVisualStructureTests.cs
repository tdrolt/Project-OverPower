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
        private const string SolidPath = "Assets/Gameplay/Abilities/Ability Visual Solid.mat";
        private const string GlassPath = "Assets/Gameplay/Abilities/Ability Visual Glass.mat";
        private const string LinePath = "Assets/Gameplay/UI/AimConeLine.mat";
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private const string MarkerPath = "Assets/Gameplay/Projectiles/Blast Marker.prefab";

        // A6 (Tudor 2026-09-17 evening): rocket blasts stay at their real height, so step 6 needs a short see-through
        // shell at the burst point instead of a floor BlastMarker. Built here, in the shared kit, so step 6 has it
        // ready - see this file's own extra test below the marker's.
        private const string ShellPath = "Assets/Gameplay/Projectiles/Splash Shell.prefab";

        // A3 (Tudor 2026-09-17 evening): the flamethrower's fan needs a genuine per-vertex tip-to-edge gradient, and
        // GlassPath (URP/Unlit) never reads a mesh's vertex colours at all (verified against the shader's own HLSL
        // source - its Attributes struct has no COLOR semantic). Laser Beam.mat hit exactly this bug on 2026-09-15 and
        // was moved to Particles/Unlit; the fan below gets its OWN new material for the same reason, rather than
        // reshading Ability Visual Glass.mat out from under the mine, portal and fence, which already ship with it.
        private const string FlamePath = "Assets/Gameplay/Abilities/Flame Cone.mat";

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
        public void TheSplashShellIsASeeThroughSphereWithNoCollider()
        {
            // A6: what step 6 drops on a rocket blast instead of a floor BlastMarker - a short shell at the real
            // burst point (any height), not snapped to the ground.
            GameObject prefab = Load(ShellPath);
            var shell = prefab.GetComponent<SplashShell>();
            Assert.IsNotNull(shell);
            Transform sphere = Child(prefab.transform, "Sphere");
            // "pSphere1" is the REAL name of Unity's builtin Sphere.fbx mesh on this engine version, not "Sphere" -
            // same finding as the marker's Cylinder, above.
            AssertMesh(sphere, "pSphere1", GlassPath);
            Assert.AreEqual(1f, sphere.localScale.x, 1e-5f, "unit diameter - Spawn scales it to the blast");
            Assert.AreEqual(1f, sphere.localScale.y, 1e-5f);
            Assert.AreEqual(1f, sphere.localScale.z, 1e-5f);
            Assert.AreSame(sphere, Ref(shell, "sphere"));
            AssertNoCollider(prefab);
            Assert.IsNull(prefab.GetComponent<SnapVisualToGround>(), "the shell stays at the real burst height - never snapped to the floor");
        }

        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab")]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab")]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab")]
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
        public void TheFireFieldHasNoVisualChildSnapItAlreadySpawnsOnTheFloor()
        {
            // A2 (Tudor 2026-09-17 evening, gameplay change): DetonateAtCursor now spawns this prefab's ROOT at the
            // floor point below the burst (GroundSnap.TryFindGroundY, read on the shooter's own client and
            // replicated by PhotonNetwork.Instantiate's own position argument - no new RPC), so the disc already
            // riding on that root needs no floor-snap of its own. The plan's printed Task 6 Step 1 put a
            // SnapVisualToGround on this prefab's Visual child instead; A2 supersedes that - a second, independent
            // floor probe on the disc would fight the root's own placement every frame for no reason.
            GameObject prefab = Load("Assets/Resources/Fire Field.prefab");
            Transform visual = Child(prefab.transform, "Visual");
            Assert.IsNull(visual.GetComponent<SnapVisualToGround>(),
                "the field's root is placed on the floor at spawn time - see DetonateAtCursor.OnExpired");
            AssertNoCollider(prefab);
        }

        [Test]
        public void TheFireFieldDiscHasAGlassMaterialAndABrightRimSoItReadsAsFireNotAShadow()
        {
            // Ability visuals step 6 amendment (2026-09-18, review findings): the disc shipped as an UNSTYLED
            // default-grey URP/Lit cylinder - at the mercy of scene lighting, it read as a shadow or scorch mark,
            // not fire, against this arena's own tan/orange ground (pixel-sampled within 5-15 RGB units of bare
            // ground). Moved to the shared Ability Visual Glass.mat (unlit, immune to scene lighting) plus a bright
            // Rim outline, the same fill+rim shape Blast Marker and Portal already use. Tudor's second answer
            // replaced a fixed hot-orange palette with the SHOOTER'S TEAM colour (theme.ShotColorFor) - matching
            // MineView/PortalView/FenceCageView/SplashShell, and telling a player whose fire they are standing in,
            // which no fixed colour could.
            GameObject prefab = Load("Assets/Resources/Fire Field.prefab");
            Transform visual = Child(prefab.transform, "Visual");
            Assert.AreEqual(GlassPath, AssetDatabase.GetAssetPath(visual.GetComponent<MeshRenderer>().sharedMaterial),
                "the disc must not still be Unity's own default Lit material");
            LineRenderer rim = AssertLine(Child(prefab.transform, "Rim"), flat: true);
            var field = prefab.GetComponent<FireField>();
            Assert.AreSame(rim, Ref(field, "rim"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(field, "theme")));
            AssertNoCollider(prefab);
        }

        // ---- ability visuals steps 3-7 add their tests below this line ----

        [Test]
        public void TheMineIsADarkSpikedPuckWithATeamStudAndNoCollider()
        {
            GameObject prefab = Load("Assets/Resources/Mine.prefab");
            Transform visual = Child(prefab.transform, "Visual");
            Assert.AreSame(visual, Ref(prefab.GetComponent<Mine>(), "visual"), "Mine hides this the instant it detonates");
            Assert.AreEqual(Vector3.zero, visual.localPosition);
            Assert.AreEqual(Vector3.one, visual.localScale);
            Assert.IsNotNull(visual.GetComponent<SnapVisualToGround>());
            // "pCylinder1" is the REAL name of Unity's builtin Cylinder.fbx mesh on this engine version
            // (verified live: Resources.GetBuiltinResource<Mesh>("Cylinder.fbx").name) - same finding as
            // the Blast Marker's and Splash Shell's own tests, above. Cube.fbx really is "Cube" here.
            AssertMesh(Child(visual, "Body"), "pCylinder1", SolidPath);
            AssertMesh(Child(visual, "Stud"), "Cube", SolidPath);
            for (int i = 1; i <= 4; i++)
                AssertMesh(Child(visual, "Spike " + i), "Cube", SolidPath);
            LineRenderer ring = AssertLine(Child(visual, "Trigger Ring"), flat: true);

            var view = prefab.GetComponent<MineView>();
            Assert.IsNotNull(view);
            var so = new SerializedObject(view);
            Assert.AreEqual(5, so.FindProperty("bodyParts").arraySize, "puck + 4 spikes");
            Assert.AreEqual(1, so.FindProperty("teamParts").arraySize, "the stud");
            Assert.AreSame(ring, Ref(view, "triggerRing"));
            Assert.AreSame(visual, Ref(view, "visualRoot"));
            Assert.AreEqual(MarkerPath, AssetDatabase.GetAssetPath(Ref(view, "blastMarkerPrefab")));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<PhotonView>(true).Length);
        }

        [Test]
        public void ThePortalIsALightRimmedDiscWithAnOwnerOnlyBeaconAndNoCollider()
        {
            GameObject prefab = Load("Assets/Resources/Portal.prefab");
            Assert.IsNull(prefab.transform.Find("Visual"), "the old grey cylinder is gone");
            Transform footprint = Child(prefab.transform, "Footprint");
            // "pCylinder1" - see the mine test's own comment, above.
            AssertMesh(footprint, "pCylinder1", GlassPath);
            Assert.AreEqual(1f, footprint.localScale.x, 1e-5f, "unit diameter - Portal scales it to Portal Diameter");
            Assert.AreSame(footprint, Ref(prefab.GetComponent<Portal>(), "visual"));
            LineRenderer rim = AssertLine(Child(prefab.transform, "Rim"), flat: true);
            Transform beacon = Child(prefab.transform, "Owner Beacon");
            AssertMesh(Child(beacon, "Diamond"), "Cube", SolidPath);
            AssertMesh(Child(beacon, "Stem"), "pCylinder1", GlassPath);

            var view = prefab.GetComponent<PortalView>();
            Assert.IsNotNull(view);
            Assert.AreSame(footprint.GetComponent<MeshRenderer>(), Ref(view, "footprint"));
            Assert.AreSame(rim, Ref(view, "rim"));
            Assert.AreSame(beacon.gameObject, Ref(view, "ownerBeacon"));
            Assert.AreSame(Child(beacon, "Diamond").GetComponent<MeshRenderer>(), Ref(view, "beacon"));
            Assert.AreSame(Child(beacon, "Stem").GetComponent<MeshRenderer>(), Ref(view, "stem"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<PhotonView>(true).Length);
        }

        [Test]
        public void TheElectricFenceIsACageOfPostsAndBarsWithNoCollider()
        {
            GameObject prefab = Load("Assets/Resources/Electric Fence.prefab");
            Assert.IsNull(Ref(prefab.GetComponent<ElectricFence>(), "visual"), "nothing is stretched to the radius any more");
            Assert.IsNull(prefab.transform.Find("Visual"), "the old flat disc is gone");
            Transform cage = Child(prefab.transform, "Cage");
            Assert.IsNotNull(cage.GetComponent<SnapVisualToGround>());
            Transform post = Child(cage, "Post");
            AssertMesh(post, "Cube", GlassPath);

            var view = prefab.GetComponent<FenceCageView>();
            Assert.IsNotNull(view);
            SerializedProperty bars = new SerializedObject(view).FindProperty("bars");
            string[] names = { "Bar Low", "Bar Mid", "Bar Top" };
            Assert.AreEqual(names.Length, bars.arraySize);
            float below = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                LineRenderer bar = AssertLine(Child(cage, names[i]), flat: false);
                Assert.Greater(bar.transform.localPosition.y, below, names[i] + " is above the bar below it");
                below = bar.transform.localPosition.y;
                Assert.AreSame(bar, bars.GetArrayElementAtIndex(i).objectReferenceValue);
            }
            Assert.LessOrEqual(below, post.localPosition.y * 2f, "the top bar is no higher than the posts");
            LineRenderer band = AssertLine(Child(cage, "Band"), flat: true);
            Assert.AreSame(post, Ref(view, "post"));
            Assert.AreSame(band, Ref(view, "band"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(2, prefab.GetComponentsInChildren<PhotonView>(true).Length, "unchanged - flagged for Tudor, not fixed here");
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
            GameObject module = Load("Assets/Gameplay/Abilities/Flamethrower.prefab");
            var cone = Ref(module.GetComponent<FlamethrowerAbility>(), "sprayVfxPrefab") as FlameConeVisual;
            Assert.IsNotNull(cone, "Flamethrower › Spray Vfx Prefab");
            Assert.AreEqual("Assets/Gameplay/Abilities/Flamethrower Cone.prefab", AssetDatabase.GetAssetPath(cone));
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
    }
}
