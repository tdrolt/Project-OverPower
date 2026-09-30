using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>GroundSnap against real colliders in a preview scene - never the open Game Scene, which must not get dirty.</summary>
    public class GroundSnapTests
    {
        /// <summary>Something with health standing on the floor: the probe must look past it.</summary>
        private class FakeBody : MonoBehaviour, IDamageable
        {
            public DamageResult ApplyDamage(in DamageInfo info) => default;
            public bool IsAlive => true;
            public int TeamId => 0;
            public int ActorNumber => 1;
            public bool HasLocalAuthority => true;
        }

        private const int Everything = ~0;
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private GameObject Box(string name, Vector3 centre, Vector3 size)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
            return go;
        }

        private bool Probe(Vector3 from, out float groundY) =>
            GroundSnap.TryFindGroundY(scene.GetPhysicsScene(), from, GroundSnap.ProbeUp, GroundSnap.ProbeDown, Everything, out groundY);

        [Test]
        public void FindsTheTopOfTheFloorBelowAPoint()
        {
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f)); // top at y = 0
            Assert.IsTrue(Probe(new Vector3(1f, 2f, 1f), out float y));
            Assert.AreEqual(0f, y, 1e-3f);
        }

        [Test]
        public void LooksPastABodyOnTheSpot()
        {
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            Box("Body", new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f)).AddComponent<FakeBody>(); // 0..2 m tall
            Assert.IsTrue(Probe(new Vector3(0f, 2.1f, 0f), out float fromAbove), "a blast just above someone's head");
            Assert.AreEqual(0f, fromAbove, 1e-3f);
            Assert.IsTrue(Probe(new Vector3(0f, 0.5f, 0f), out float fromInside), "a mine placed at the caster's root, inside their body");
            Assert.AreEqual(0f, fromInside, 1e-3f);
        }

        [Test]
        public void AFloorFurtherDownThanTheProbeIsNotFound()
        {
            Box("Floor", new Vector3(0f, -10.5f, 0f), new Vector3(20f, 1f, 20f)); // top at y = -10
            Assert.IsFalse(Probe(new Vector3(0f, 0.5f, 0f), out float y));
            Assert.AreEqual(0.5f, y, 1e-5f, "falls back to the point's own height");
        }

        [Test]
        public void ARoofAboveThePointIsNotTakenForTheFloor()
        {
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            Box("Roof", new Vector3(0f, 3.5f, 0f), new Vector3(20f, 1f, 20f)); // underside at 3 m, above the 0.25 m start
            Assert.IsTrue(Probe(new Vector3(0f, 2f, 0f), out float y));
            Assert.AreEqual(0f, y, 1e-3f);
        }
    }
}
