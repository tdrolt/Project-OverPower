using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overpower.Arena;
using Overpower.Match;
using Overpower.UI;

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
                // 2026-09-23 (Tudor: "the exterior collumns and the base... glow the same color as the top"): the
                // shaft moved to the Unlit "Tower Owner" material, exactly like the crown/caps, so it reads at the
                // full owner colour instead of the Lit stone's shaded one.
                Assert.AreEqual("Tower Owner", shaftRenderer.sharedMaterial.name, $"slot {i} shaft material");
                // 2026-09-21: the shaft is painted too now (Tudor: "currently its only the top"), through the same
                // Refresh call - so, like the cap, it must NOT be static-batched. Measured in Play Mode (captures
                // under captures/towers-2026-09-21/): a statically-batched renderer draws through its batch
                // root's combined mesh, so SetPropertyBlock on the ORIGINAL renderer never reaches the screen -
                // the crown/caps (never batched) repainted correctly while the batched shaft/plinth/drum silently
                // kept showing the material's own colour. Without this assertion the earlier version of this test
                // stayed green while the real render was still broken (GetPropertyBlock/SetPropertyBlock succeed
                // on a batched renderer even though nothing draws with them).
                Assert.IsFalse(IsBatchingStatic(shaft.gameObject), $"slot {i} shaft is repainted per owner, so it must not be static-batched");

                Renderer capRenderer = look.columnCaps[i];
                Assert.AreSame(cap.GetComponent<Renderer>(), capRenderer, $"slot {i}: columnCaps must point at the slot's own Cap renderer");
                Assert.AreEqual("Tower Owner", capRenderer.sharedMaterial.name, $"slot {i} cap material");
                Assert.IsFalse(IsBatchingStatic(cap.gameObject), $"slot {i} cap is repainted per owner, so it must not be static-batched");

                Assert.AreEqual(4, look.columnShafts.Length);
                Renderer wiredShaftRenderer = look.columnShafts[i];
                Assert.AreSame(shaft.GetComponent<Renderer>(), wiredShaftRenderer, $"slot {i}: columnShafts must point at the slot's own Shaft renderer");
                Assert.AreEqual("Tower Owner", wiredShaftRenderer.sharedMaterial.name, $"slot {i} shaft material (columnShafts)");
            }

            Transform plinth = instance.transform.Find("Plinth");
            Transform drum = instance.transform.Find("Drum");
            Assert.IsNotNull(plinth);
            Assert.IsNotNull(drum);
            // 2026-09-23: the plinth (the base) joins the shafts on the Unlit "Tower Owner" material at the full
            // owner colour; the drum (the body) keeps the Lit "Tower Stone" material and the shade, so the tower
            // keeps a silhouette instead of becoming one flat block (Tudor named "the exterior collumns and the
            // base", not the whole tower).
            Assert.AreEqual("Tower Owner", plinth.GetComponent<Renderer>().sharedMaterial.name);
            Assert.AreEqual("Tower Stone", drum.GetComponent<Renderer>().sharedMaterial.name);
            // 2026-09-21: the base takes the owner's colour too, so - same reasoning as the shaft above -
            // Plinth/Drum must not be static-batched, or Refresh's SetPropertyBlock calls never reach the screen.
            Assert.IsFalse(IsBatchingStatic(plinth.gameObject), "the plinth is repainted per owner, so it must not be static-batched");
            Assert.IsFalse(IsBatchingStatic(drum.gameObject), "the drum is repainted per owner, so it must not be static-batched");

            // 2026-09-21: the base takes the owner's colour too - TowerLook.plinth/drum must point at these same
            // renderers, or Refresh has nothing to paint.
            Assert.AreSame(plinth.GetComponent<Renderer>(), look.plinth, "look.plinth must point at the Plinth's own renderer");
            Assert.AreSame(drum.GetComponent<Renderer>(), look.drum, "look.drum must point at the Drum's own renderer");
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

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static void AssertBaseColor(Renderer renderer, Color expected, string label)
        {
            Assert.IsNotNull(renderer, label);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Color actual = block.GetColor(BaseColorId);
            Assert.AreEqual(expected.r, actual.r, 0.001f, $"{label} r");
            Assert.AreEqual(expected.g, actual.g, 0.001f, $"{label} g");
            Assert.AreEqual(expected.b, actual.b, 0.001f, $"{label} b");
            Assert.AreEqual(expected.a, actual.a, 0.001f, $"{label} a");
        }

        private static void AssertNeverPainted(Renderer renderer, string label)
        {
            Assert.IsNotNull(renderer, label);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, $"{label}: must never be given a property block while its slot is hidden");
        }

        /// <summary>2026-09-23 (Tudor: "the exterior collumns and the base of the territory prefab glow the same
        /// color as the top"): Refresh must paint the Plinth and every SHOWN shaft at the very same FULL colour the
        /// crown/caps get - no shade - now that they are on the Unlit "Tower Owner" material. The Drum keeps the
        /// Lit "Tower Stone" material and is still painted at colour times UiTheme.towerBodyShade, so the tower
        /// keeps a silhouette. A HIDDEN shaft (tier 2 shows only 2 of 4 slots) must never be painted at all, same
        /// rule ApplyColumns already applies to a hidden cap.</summary>
        [Test]
        public void RefreshPaintsShaftsAndPlinthAtFullColourAndTheDrumAtColourTimesBodyShade()
        {
            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.IsNotNull(theme, "Assets/Gameplay/Config/UiTheme.asset");

            look.Bind(theme);
            look.ApplyColumns(2); // TowerLookRules: tier 2 shows 2 of the 4 slots - slots 2/3 stay hidden.

            const int team = 1;
            var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, team, TerritoryMap.Neutral, false);
            look.Refresh(state, 0f);

            Color expectedFlat = theme.ShotColorFor(team);
            Color expectedBody = expectedFlat * theme.towerBodyShade;
            expectedBody.a = expectedFlat.a;

            // The crown/caps stay the flat, unshaded colour - unaffected by this change.
            AssertBaseColor(look.crown, expectedFlat, "crown");
            AssertBaseColor(look.columnCaps[0], expectedFlat, "shown cap 0");

            // The plinth and shown shafts now match the crown/caps exactly: the full colour, no shade.
            AssertBaseColor(look.plinth, expectedFlat, "plinth");
            AssertBaseColor(look.columnShafts[0], expectedFlat, "shown shaft 0");
            AssertBaseColor(look.columnShafts[1], expectedFlat, "shown shaft 1");
            AssertNeverPainted(look.columnShafts[2], "hidden shaft 2");
            AssertNeverPainted(look.columnShafts[3], "hidden shaft 3");

            // Only the drum keeps the shade.
            AssertBaseColor(look.drum, expectedBody, "drum");
        }

        /// <summary>Pins the controller's neutral choice (Rule 6, chosen by capture): the plinth follows the exact
        /// same rule as an owned tower's plinth (the full UiTheme.towerNeutralColor, no shade, since 2026-09-23),
        /// and the drum keeps the shaded formula - so a neutral tower is never left on the stone's own unpainted
        /// colour, and the ring and the whole tower can never disagree about what "nobody owns this" looks like.
        /// </summary>
        [Test]
        public void RefreshPaintsThePlinthFullNeutralGreyAndTheDrumNeutralGreyTimesShadeWhenNobodyOwnsTheZone()
        {
            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.IsNotNull(theme, "Assets/Gameplay/Config/UiTheme.asset");

            look.Bind(theme);
            look.ApplyColumns(4);

            var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, TerritoryMap.Neutral, TerritoryMap.Neutral, false);
            look.Refresh(state, 0f);

            Color expectedBody = theme.towerNeutralColor * theme.towerBodyShade;
            expectedBody.a = theme.towerNeutralColor.a;

            AssertBaseColor(look.plinth, theme.towerNeutralColor, "neutral plinth");
            AssertBaseColor(look.drum, expectedBody, "neutral drum");
        }
    }
}
