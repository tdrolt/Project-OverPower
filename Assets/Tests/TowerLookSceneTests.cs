using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// 2026-09-23 (Tudor: "the territories have the exterior collumns and the base of the territory prefab glow
    /// the same color as the top"): the scene's 10 towers are NOT prefab instances (2026-09-21 trap), so a prefab
    /// edit alone does not reach them - this guards the scene copies directly, the same way
    /// TerritoryAdjacencySceneTests guards BuildingManager's own scene copy of the territory map. Read-only: the
    /// scene is used as it is loaded, or opened additively and closed again without saving.
    /// </summary>
    public class TowerLookSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";
        private const int ExpectedTowerCount = 10;

        private static bool IsBatchingStatic(GameObject go) =>
            (GameObjectUtility.GetStaticEditorFlags(go) & StaticEditorFlags.BatchingStatic) != 0;

        [Test]
        public void EveryTowerInTheSceneHasShaftsAndPlinthOnTowerOwnerAndTheDrumOnTowerStoneNeverBatchingStatic()
        {
            WithGameScene(scene =>
            {
                List<TowerLook> looks = Find<TowerLook>(scene).ToList();
                Assert.AreEqual(ExpectedTowerCount, looks.Count, "expected 10 TowerLooks in Game Scene");

                foreach (TowerLook look in looks)
                {
                    string ownerName = look.transform.parent != null ? look.transform.parent.name : look.name;

                    Assert.IsNotNull(look.plinth, $"{ownerName}: plinth not wired");
                    Assert.AreEqual("Tower Owner", look.plinth.sharedMaterial.name, $"{ownerName} plinth material");
                    Assert.IsFalse(IsBatchingStatic(look.plinth.gameObject), $"{ownerName} plinth must not be static-batched");

                    Assert.IsNotNull(look.drum, $"{ownerName}: drum not wired");
                    Assert.AreEqual("Tower Stone", look.drum.sharedMaterial.name, $"{ownerName} drum material");
                    Assert.IsFalse(IsBatchingStatic(look.drum.gameObject), $"{ownerName} drum must not be static-batched");

                    Assert.IsNotNull(look.crown, $"{ownerName}: crown not wired");
                    Assert.IsFalse(IsBatchingStatic(look.crown.gameObject), $"{ownerName} crown must not be static-batched");

                    Assert.AreEqual(4, look.columnShafts.Length, $"{ownerName} columnShafts length");
                    for (int i = 0; i < look.columnShafts.Length; i++)
                    {
                        Renderer shaft = look.columnShafts[i];
                        if (shaft == null)
                            continue; // an unused slot on a lower-tier tower may be left unwired, same as columnCaps.
                        Assert.AreEqual("Tower Owner", shaft.sharedMaterial.name, $"{ownerName} shaft {i} material");
                        Assert.IsFalse(IsBatchingStatic(shaft.gameObject), $"{ownerName} shaft {i} must not be static-batched");
                    }

                    Assert.AreEqual(4, look.columnCaps.Length, $"{ownerName} columnCaps length");
                    for (int i = 0; i < look.columnCaps.Length; i++)
                    {
                        Renderer cap = look.columnCaps[i];
                        if (cap == null)
                            continue;
                        Assert.IsFalse(IsBatchingStatic(cap.gameObject), $"{ownerName} cap {i} must not be static-batched");
                    }
                }
            });
        }

        // Same helper as TerritoryAdjacencySceneTests: never opens the scene Single, which would prompt to save a
        // dirty scene.
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
