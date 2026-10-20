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
            Assert.IsTrue(block.isEmpty, $"{label}: must never be painted (a hidden slot, or a body that takes its colour from the ground)");
        }

        /// <summary>A capture tower glows only at its crown, its base and its column caps: the colour brightened by
        /// UiTheme.towerOwnerGlow (TowerLookRules.GlowKeepingHue), past 1 so it clears the scene's Bloom threshold. The body and the
        /// shafts wear the arena wall look and are never painted, so an owned tower never turns into one flat block of
        /// colour. A HIDDEN slot is never painted. The test picks its own glow value (not the asset's tuned number).
        /// </summary>
        [Test]
        public void RefreshGlowsTheCrownCapsAndPlinthAndNeverPaintsTheBodyOrTheShafts()
        {
            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.IsNotNull(theme, "Assets/Gameplay/Config/UiTheme.asset");

            float originalGlow = theme.towerOwnerGlow;
            const float testGlow = 2f; // the test's own value, not the asset's tuned 1.5 - see the class comment above.
            theme.towerOwnerGlow = testGlow;
            try
            {
                look.Bind(theme);
                look.ApplyColumns(2); // TowerLookRules: tier 2 shows 2 of the 4 slots - slots 2/3 stay hidden.

                const int team = 1;
                var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, team, TerritoryMap.Neutral, false);
                look.Refresh(state, 0f);

                Color expectedGlow = TowerLookRules.GlowKeepingHue(theme.ShotColorFor(team), testGlow);

                AssertBaseColor(look.crown, expectedGlow, "crown");
                AssertBaseColor(look.columnCaps[0], expectedGlow, "shown cap 0");
                AssertBaseColor(look.columnCaps[1], expectedGlow, "shown cap 1");
                AssertBaseColor(look.plinth, expectedGlow, "plinth");
                AssertNeverPainted(look.columnCaps[2], "hidden cap 2");
                AssertNeverPainted(look.columnCaps[3], "hidden cap 3");
                AssertNeverPainted(look.columnShafts[0], "shown shaft 0");
                AssertNeverPainted(look.columnShafts[1], "shown shaft 1");
                AssertNeverPainted(look.columnShafts[2], "hidden shaft 2");
                AssertNeverPainted(look.columnShafts[3], "hidden shaft 3");
                AssertNeverPainted(look.drum, "drum");
            }
            finally
            {
                theme.towerOwnerGlow = originalGlow;
            }
        }

        /// <summary>Task 5 (2026-09-25): the centre drops to three columns while it plays as a Tier III
        /// (PhaseTwoCutRules.EffectiveTier), through ApplyColumnsAtRuntime - the runtime twin of ApplyColumns.
        /// Refresh only repaints a piece whose colour actually changed, so a slot that was HIDDEN the last time the
        /// colour was cached (tier 3's fourth slot) was never painted at all: without ApplyColumnsAtRuntime forcing
        /// the next Refresh to repaint (clearing shownColorSet, exactly like the very first Refresh after Bind), a
        /// column shown at runtime would stay colourless.</summary>
        [Test]
        public void ShowingAColumnAtRuntimePaintsIt()
        {
            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.IsNotNull(theme, "Assets/Gameplay/Config/UiTheme.asset");

            look.ApplyColumns(3);
            look.Bind(theme);
            const int team = 1;
            var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, team, TerritoryMap.Neutral, false);
            look.Refresh(state, 0f);

            look.ApplyColumnsAtRuntime(4);
            look.Refresh(state, 0f);

            Assert.AreEqual(4, look.ShownColumns);
            AssertBaseColor(look.columnCaps[3], look.ShownColor, "the newly-shown fourth slot's cap");
        }

        /// <summary>A neutral tower's crown and plinth are the full UiTheme.towerNeutralColor, no shade, so the ring and
        /// the tower never disagree about what "nobody owns this" looks like; the body is the arena look, never painted.
        /// It never glows, even with Tower Owner Glow raised well past 1 ("glow means owned"): the raised glow catches
        /// OwnerPaintBase.Team ever being matched on Neutral too.</summary>
        [Test]
        public void RefreshNeverGlowsANeutralTowerEvenWithGlowRaised()
        {
            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.IsNotNull(theme, "Assets/Gameplay/Config/UiTheme.asset");

            float originalGlow = theme.towerOwnerGlow;
            theme.towerOwnerGlow = 2f;
            try
            {
                look.Bind(theme);
                look.ApplyColumns(4);

                var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, TerritoryMap.Neutral, TerritoryMap.Neutral, false);
                look.Refresh(state, 0f);

                AssertBaseColor(look.crown, theme.towerNeutralColor, "neutral crown");
                AssertBaseColor(look.plinth, theme.towerNeutralColor, "neutral plinth");
                AssertNeverPainted(look.drum, "neutral drum");
            }
            finally
            {
                theme.towerOwnerGlow = originalGlow;
            }
        }

        /// <summary>An out-of-play capital (the third capital, cut when a match starts with only two teams) must
        /// never glow either - OwnerPaintBase.OutOfPlay is not OwnerPaintBase.Team, so glow stays 1 exactly like
        /// the neutral case above. Raises the glow the same way, so a mis-check against the wrong enum value
        /// would be caught here too.</summary>
        [Test]
        public void RefreshNeverGlowsAnOutOfPlayTowerEvenWithGlowRaised()
        {
            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.IsNotNull(theme, "Assets/Gameplay/Config/UiTheme.asset");

            float originalGlow = theme.towerOwnerGlow;
            theme.towerOwnerGlow = 2f;
            try
            {
                look.Bind(theme);
                look.ApplyColumns(1);

                var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, TerritoryMap.Neutral,
                    TerritoryMap.Neutral, false, outOfPlay: true);
                look.Refresh(state, 0f);

                AssertBaseColor(look.crown, theme.outOfPlayZoneColor, "out-of-play crown");
                AssertBaseColor(look.plinth, theme.outOfPlayZoneColor, "out-of-play plinth");
            }
            finally
            {
                theme.towerOwnerGlow = originalGlow;
            }
        }
    }
}
