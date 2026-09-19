using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overpower.Arena;

namespace Overpower.Tests
{
    /// <summary>Arena rebuild step 2: pins the Tower Look prefab's shape, layers and materials, and TowerLook's
    /// ApplyColumns - each tier's column count, spacing, and (Tudor's answer, 2026-09-19) the capital's big column
    /// against every other tier's normal-sized ones. Instantiated into a preview scene (never the open Game Scene),
    /// same pattern as ArenaSymmetryBuilderTests.</summary>
    public class TowerLookPrefabTests
    {
        private const string PrefabPath = "Assets/Gameplay/Arena/Tower Look.prefab";

        private Scene scene;
        private GameObject instance;
        private TowerLook look;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"{PrefabPath} not found");
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            SceneManager.MoveGameObjectToScene(instance, scene);
            look = instance.GetComponent<TowerLook>();
            Assert.IsNotNull(look, "Tower Look.prefab has no TowerLook component");
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private static bool IsBatchingStatic(GameObject go) =>
            (GameObjectUtility.GetStaticEditorFlags(go) & StaticEditorFlags.BatchingStatic) != 0;

        [Test]
        public void TheLookHasACrownAndFourColumnSlotsEachWithAShaftAndACap()
        {
            Assert.IsNotNull(look.crown);
            Assert.AreEqual("Tower Owner", look.crown.sharedMaterial.name);
            Assert.IsFalse(IsBatchingStatic(look.crown.gameObject), "the crown is repainted per owner, so it must not be static-batched");

            Assert.AreEqual(4, look.columnSlots.Length);
            Assert.AreEqual(4, look.columnCaps.Length);
            for (int i = 0; i < 4; i++)
            {
                Transform slot = look.columnSlots[i];
                Assert.IsNotNull(slot, $"slot {i}");
                Transform shaft = slot.Find("Shaft");
                Transform cap = slot.Find("Cap");
                Assert.IsNotNull(shaft, $"slot {i} Shaft");
                Assert.IsNotNull(cap, $"slot {i} Cap");

                Renderer shaftRenderer = shaft.GetComponent<Renderer>();
                Assert.AreEqual("Tower Stone", shaftRenderer.sharedMaterial.name, $"slot {i} shaft material");
                Assert.IsTrue(IsBatchingStatic(shaft.gameObject), $"slot {i} shaft should be static (it never repaints)");

                Renderer capRenderer = look.columnCaps[i];
                Assert.AreSame(cap.GetComponent<Renderer>(), capRenderer, $"slot {i}: columnCaps must point at the slot's own Cap renderer");
                Assert.AreEqual("Tower Owner", capRenderer.sharedMaterial.name, $"slot {i} cap material");
                Assert.IsFalse(IsBatchingStatic(cap.gameObject), $"slot {i} cap is repainted per owner, so it must not be static-batched");
            }

            Transform plinth = instance.transform.Find("Plinth");
            Transform drum = instance.transform.Find("Drum");
            Assert.IsNotNull(plinth);
            Assert.IsNotNull(drum);
            Assert.AreEqual("Tower Stone", plinth.GetComponent<Renderer>().sharedMaterial.name);
            Assert.AreEqual("Tower Stone", drum.GetComponent<Renderer>().sharedMaterial.name);
            Assert.IsTrue(IsBatchingStatic(plinth.gameObject));
            Assert.IsTrue(IsBatchingStatic(drum.gameObject));
        }

        [Test]
        public void OnlyOneColliderAndItIsAnUprightCapsuleOnTheBuildingLayer()
        {
            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            Assert.AreEqual(1, colliders.Length, "exactly one collider in the whole prefab");
            Assert.IsInstanceOf<CapsuleCollider>(colliders[0]);
            Assert.AreEqual(LayerMask.NameToLayer("Building"), colliders[0].gameObject.layer);

            Assert.AreEqual(0, instance.GetComponentsInChildren<MeshCollider>(true).Length);
            Assert.AreEqual(0, instance.GetComponentsInChildren<PhotonView>(true).Length);
            Assert.AreEqual(0, instance.GetComponentsInChildren<MonoBehaviourPun>(true).Length);
        }

        [Test]
        public void TheCapsuleIsStraightFromBelowTheFloorToAboveTheCaps()
        {
            look.ApplyColumns(4); // show every column so the highest cap is included below
            var capsule = instance.GetComponentInChildren<CapsuleCollider>(true);
            float halfSegment = capsule.height * 0.5f - capsule.radius;
            float bottom = capsule.center.y - halfSegment;
            float top = capsule.center.y + halfSegment;

            Assert.LessOrEqual(bottom, -0.5f);

            float highest = float.NegativeInfinity;
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.activeInHierarchy)
                    highest = Mathf.Max(highest, r.bounds.max.y);

            Assert.GreaterOrEqual(top, highest);
        }

        [Test]
        public void ApplyColumnsShowsExactlyThatManyEvenlySpacedAndTheCapitalsColumnIsBig()
        {
            float towerRadius = instance.GetComponentInChildren<CapsuleCollider>(true).radius;

            for (int tier = 1; tier <= 4; tier++)
            {
                look.ApplyColumns(tier);

                int expectedCount = TowerLookRules.ColumnsForTier(tier);
                Assert.AreEqual(expectedCount, look.ShownColumns, $"tier {tier} column count");

                float expectedShaftRadius = tier == 1 ? look.capitalColumnRadius : look.columnRadius;
                float expectedRingRadius = TowerLookRules.ColumnRingRadius(expectedShaftRadius, towerRadius);

                for (int i = 0; i < 4; i++)
                {
                    Transform slot = look.columnSlots[i];
                    bool shouldShow = i < expectedCount;
                    Assert.AreEqual(shouldShow, slot.gameObject.activeSelf, $"tier {tier} slot {i} active");
                    if (!shouldShow)
                        continue;

                    float expectedYaw = TowerLookRules.ColumnYawDegrees(i, expectedCount);
                    Assert.AreEqual(expectedYaw, slot.localRotation.eulerAngles.y, 0.01f, $"tier {tier} slot {i} yaw");

                    float distance = new Vector2(slot.localPosition.x, slot.localPosition.z).magnitude;
                    Assert.AreEqual(expectedRingRadius, distance, 0.001f, $"tier {tier} slot {i} ring radius");

                    Transform shaft = slot.Find("Shaft");
                    Transform cap = slot.Find("Cap");
                    // The built-in cylinder mesh is 2 units wide (radius 1) at scale 1, so scale.x/z equal the
                    // radius directly (measured when the prefab was authored - see TowerLook.ApplyColumns).
                    float shaftRadius = shaft.localScale.x;
                    float capRadius = cap.localScale.x;
                    Assert.AreEqual(expectedShaftRadius, shaftRadius, 0.001f, $"tier {tier} slot {i} shaft radius");
                    Assert.Greater(capRadius, shaftRadius, $"tier {tier} slot {i}: the cap must read wider than the shaft");
                }

                // Tudor's answer, 2026-09-19: the capital's one column is BIG; every other tier's columns share the
                // one normal (small) size.
                if (tier == 1)
                    Assert.AreEqual(look.capitalColumnRadius, expectedShaftRadius);
                else
                    Assert.AreEqual(look.columnRadius, expectedShaftRadius);
            }
        }
    }
}
