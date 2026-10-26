using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>Every low barrier in both saved scenes, and the one the Conquest shrink adds while playing, wears the barrier surface on a
    /// plain box (every face maps the whole texture), so the arena shader paints its top's stripe down the middle, and is wide enough to
    /// leave a dark margin either side of that stripe. The saved scenes are opened additively and closed again (Game Scene stays open).</summary>
    public class BarrierSurfaceSceneTests
    {
        private const string BarrierPath = "Assets/_Project/Features/Arena/Materials/Arena Barrier.mat";

        // A stripe wider than this share of a top leaves margins too thin to read: the whole top looks one plain grey block.
        private const float MostOfTheTop = 0.7f;

        private static float StripeWidth() =>
            AssetDatabase.LoadAssetAtPath<ArenaLook>("Assets/_Project/Shared/Config/ArenaLook.asset").barrierTopStripeWidth;

        [TestCase("Assets/_Project/Scenes/Game Scene.unity")]
        [TestCase("Assets/_Project/Scenes/Dominion 2v2.unity")]
        public void EveryLowBarrierWearsTheBarrierSurface(string path)
        {
            var barrier = AssetDatabase.LoadAssetAtPath<Material>(BarrierPath);
            Assert.IsTrue(barrier.IsKeywordEnabled("_ARENA_BARRIER"), "the barrier surface paints the barrier pattern");
            int layer = LayerMask.NameToLayer(ArenaLayers.BarrierLayerName);
            Scene existing = SceneManager.GetSceneByPath(path);
            bool wasOpen = existing.IsValid() && existing.isLoaded;
            Scene scene = wasOpen ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                int barriers = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (renderer.gameObject.layer != layer)
                            continue;
                        barriers++;
                        Assert.AreEqual(1, renderer.sharedMaterials.Length, renderer.name + " draws one surface");
                        Assert.AreSame(barrier, renderer.sharedMaterial, $"{renderer.name} in {path} wears the barrier surface");
                        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        Assert.AreSame(Resources.GetBuiltinResource<Mesh>("Cube.fbx"), mesh, renderer.name + " is a plain box, so the shader finds its top's middle");
                        Vector3 size = renderer.transform.lossyScale;
                        Assert.Less(StripeWidth(), MostOfTheTop * Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.z)),
                            $"{renderer.name} in {path} ({size.x:F2} x {size.z:F2} m) leaves a dark margin either side of its top's stripe");
                    }
                Assert.Greater(barriers, 0, path + " has low barriers");
            }
            finally
            {
                if (!wasOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TheShrinksRecessBarrierWearsTheBarrierSurfaceToo()
        {
            var layout = AssetDatabase.LoadAssetAtPath<ArenaLayout>("Assets/_Project/Shared/Config/ArenaLayout.asset");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Material>(BarrierPath), layout.BarrierMaterial);
            MethodInfo barrierMaterial = typeof(ArenaLayout).GetProperty(nameof(ArenaLayout.BarrierMaterial)).GetMethod;
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaPhaseTwoCut), "Apply", barrierMaterial), "the shrink builds its recess barrier in the layout's barrier surface");
            Assert.IsFalse(IlWiring.Uses(typeof(ArenaPhaseTwoCut), "NewBox", barrierMaterial), "the control: a method that never reads it says so");
            Vector3 size = layout.PhaseTwoRecessBarrierSize;
            Assert.Less(StripeWidth(), MostOfTheTop * Mathf.Min(size.x, size.z), "and wide enough to leave a dark margin either side of its stripe");
        }
    }
}
