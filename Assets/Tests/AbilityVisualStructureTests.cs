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

        // ---- ability visuals steps 3-7 add their tests below this line ----
    }
}
