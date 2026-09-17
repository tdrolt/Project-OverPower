using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// The bake frames the minimap on the arena's farthest renderer from its symmetry centre. This builds a tiny arena
    /// in a PREVIEW scene: a preview scene never marks Game Scene dirty, and a dirty Game Scene hangs the Editor behind a
    /// modal save dialog (same reason as ArenaSymmetryBuilderTests).
    /// </summary>
    public class MinimapBakerTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private GameObject Make(string name, Transform parent = null)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go;
        }

        [Test]
        public void ArenaRadiusIsTheFarthestMeshCornerFromTheCentreAcrossAllThreeThirds()
        {
            var arena = Make("Arena").AddComponent<ArenaSymmetry>();
            arena.centre = new Vector3(10f, 0f, 20f);
            arena.source = Make("Source", arena.transform).transform;
            arena.generated120 = Make("Generated 120", arena.transform).transform;
            arena.generated240 = Make("Generated 240", arena.transform).transform;

            GameObject near = Make("Near crate", arena.source);
            near.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            near.AddComponent<MeshRenderer>();
            near.transform.position = new Vector3(15f, 0f, 20f);

            GameObject far = Make("Far wall", arena.generated240);
            far.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            far.AddComponent<MeshRenderer>();
            far.transform.position = new Vector3(40f, 0f, 20f); // a 1 m cube: far corners 30.5 m along X, 0.5 m along Z

            float radius = MinimapBaker.ArenaRadius(arena, out string farthest);

            Assert.AreEqual(Mathf.Sqrt(30.5f * 30.5f + 0.5f * 0.5f), radius, 1e-3f);
            Assert.AreEqual("Far wall", farthest);
        }
    }
}
