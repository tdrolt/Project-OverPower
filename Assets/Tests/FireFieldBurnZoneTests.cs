using NUnit.Framework;
using Overpower.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// A2 (Tudor 2026-09-17 evening, gameplay change): the cursor rocket's Fire Field burns an upright CYLINDER
    /// standing on its own floor position, not a sphere at its old floating centre - "standing in the drawn circle"
    /// must be exactly "being burned". FireField.OverlapBurnZone is the one seam A2 actually changed
    /// (BurnEveryoneInside's per-tick damage and its team/self filter are untouched - see that method's own
    /// comment), pinned here against real colliders in a preview scene, the same pattern GroundSnapTests uses,
    /// because a capsule overlap needs an actual PhysicsScene, not hand-rolled maths.
    /// </summary>
    public class FireFieldBurnZoneTests
    {
        private const int Everything = ~0;
        private const float Radius = 2.5f; // Fire Field's own pinned Radius - AbilityVisualPrefabGuardTests.

        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        /// <summary>A standing player's collider at chest height, horizontalDistance out from the field's centre -
        /// inside the 2 m burn band either way, so only the horizontal distance is under test here.</summary>
        private void Standee(Vector3 floorCentre, float horizontalDistance)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags("Standee", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = floorCentre + new Vector3(horizontalDistance, 1f, 0f);
            CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            Physics.SyncTransforms();
        }

        private int OverlapCount(Vector3 floorCentre, float horizontalDistance)
        {
            Standee(floorCentre, horizontalDistance);
            Collider[] buffer = new Collider[4];
            return FireField.OverlapBurnZone(scene.GetPhysicsScene(), floorCentre, Radius, buffer, Everything);
        }

        [Test]
        public void AnEnemyAt0Point9TimesRadiusIsCaughtEveryTick()
        {
            Assert.AreEqual(1, OverlapCount(Vector3.zero, Radius * 0.9f));
        }

        [Test]
        public void AnEnemyAt1Point2TimesRadiusIsNeverCaught()
        {
            Assert.AreEqual(0, OverlapCount(Vector3.zero, Radius * 1.2f));
        }

        [Test]
        public void AnEnemyStandingAtTheBurstPointsFloorPositionIsCaught()
        {
            // A7's regression contract: burn must not drop for an enemy standing at the burst point's own floor
            // position (the field's centre) - the whole reason A2 exists (the old sphere floated 2 m up).
            Assert.AreEqual(1, OverlapCount(Vector3.zero, 0f));
        }
    }
}
