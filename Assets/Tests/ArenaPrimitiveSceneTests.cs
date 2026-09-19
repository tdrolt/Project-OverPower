using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overpower.Arena;
using Overpower.EditorTools;

namespace Overpower.Tests
{
    /// <summary>Arena rebuild step 3: pins what ArenaPrimitiveBuilder.BuildTowerLooks stamps onto every one of the ten
    /// towers already in Game Scene - a Tower Look child sized for that tower's tier, a world-sized look with an
    /// upright collider, and the old house/carpet hidden rather than removed (Decision 5). Uses the same
    /// WithGameScene pattern as ArenaSymmetrySceneTests, so it never opens the scene Single (which would prompt to
    /// save a dirty scene).</summary>
    public class ArenaPrimitiveSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void EveryTowerShowsATowerLookWithOneColumnPerTier()
        {
            WithGameScene(scene =>
            {
                foreach (BuildingCapture tower in Find<BuildingCapture>(scene))
                {
                    TowerLook[] looks = tower.GetComponentsInChildren<TowerLook>(true);
                    Assert.AreEqual(1, looks.Length, $"tower {tower.buildingID} should have exactly one Tower Look child");

                    int expected = TowerLookRules.ColumnsForTier(tower.tier);
                    Assert.AreEqual(expected, looks[0].ShownColumns,
                        $"tower {tower.buildingID} (tier {tower.tier}) should show {expected} column(s)");
                }
            });
        }

        [Test]
        public void EveryTowerLookIsWorldSizedAndItsColliderIsUpright()
        {
            WithGameScene(scene =>
            {
                foreach (BuildingCapture tower in Find<BuildingCapture>(scene))
                {
                    TowerLook look = tower.GetComponentInChildren<TowerLook>(true);
                    Assert.IsNotNull(look, $"tower {tower.buildingID} has no Tower Look child");

                    Vector3 lossy = look.transform.lossyScale;
                    Assert.AreEqual(1f, lossy.x, 0.001f, $"tower {tower.buildingID} look lossyScale.x");
                    Assert.AreEqual(1f, lossy.y, 0.001f, $"tower {tower.buildingID} look lossyScale.y");
                    Assert.AreEqual(1f, lossy.z, 0.001f, $"tower {tower.buildingID} look lossyScale.z");

                    CapsuleCollider capsule = look.GetComponent<CapsuleCollider>();
                    Assert.IsNotNull(capsule, $"tower {tower.buildingID} look has no CapsuleCollider");

                    Vector3 up = look.transform.up;
                    Assert.Greater(Vector3.Dot(up, Vector3.up), 0.999f,
                        $"tower {tower.buildingID}: the tower look's capsule is not upright in world space");

                    float halfSegment = capsule.height * 0.5f - capsule.radius;
                    float centreWorldY = look.transform.TransformPoint(capsule.center).y;
                    float bottom = centreWorldY - halfSegment;
                    float top = centreWorldY + halfSegment;
                    Assert.LessOrEqual(bottom, 0f, $"tower {tower.buildingID} capsule bottom should be at or below the floor");
                    Assert.GreaterOrEqual(top, 5.7f, $"tower {tower.buildingID} capsule top should clear the old house's box (5.73 m)");
                }
            });
        }

        [Test]
        public void TheOldHouseAndCarpetOfEveryTowerAreHidden()
        {
            WithGameScene(scene =>
            {
                foreach (BuildingCapture tower in Find<BuildingCapture>(scene))
                {
                    MeshRenderer ownRenderer = tower.GetComponent<MeshRenderer>();
                    Assert.IsNotNull(ownRenderer, $"tower {tower.buildingID} has no MeshRenderer");
                    Assert.IsFalse(ownRenderer.enabled, $"tower {tower.buildingID}'s own house MeshRenderer should be disabled");

                    Transform look = null;
                    foreach (Transform child in tower.transform)
                    {
                        if (child.GetComponent<TowerLook>() != null) { look = child; continue; }
                        Assert.IsFalse(child.gameObject.activeSelf,
                            $"tower {tower.buildingID}'s old child '{child.name}' should be inactive");
                    }
                    Assert.IsNotNull(look, $"tower {tower.buildingID} has no Tower Look child");

                    foreach (Collider c in tower.GetComponentsInChildren<Collider>(true))
                    {
                        if (c.transform == look || c.transform.IsChildOf(look)) continue;
                        bool wouldCollide = c.enabled && c.gameObject.activeInHierarchy;
                        if (!wouldCollide) continue;
                        Assert.IsTrue(c.isTrigger,
                            $"tower {tower.buildingID}: enabled non-trigger collider '{c.name}' found outside Tower Look");
                    }

                    Assert.IsNotNull(tower.flagRenderer, $"tower {tower.buildingID} has no flagRenderer assigned");
                    Assert.IsFalse(tower.flagRenderer.enabled, $"tower {tower.buildingID}'s flagRenderer should be disabled");
                }
            });
        }

        // Uses Game Scene if it's already open (the normal case: read-only checks on what's loaded), otherwise opens it
        // additively and closes it again. Never opens it Single, which would prompt to save a dirty scene.
        private static void WithGameScene(Action<Scene> body)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try { body(scene); }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }

        private static IEnumerable<T> Find<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    }
}
