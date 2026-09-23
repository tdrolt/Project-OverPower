using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// 2026-09-23 (Tudor: "decrease the capture area of the T3 and T2 territory to make them the same as T4,
    /// because in the T3 territory a player can capture it while standing behind cover... and in the T2 it
    /// overlaps with some buildings"): pins BuildingCapture.captureRadius per tier in Game Scene - tier 1
    /// (the capitals, buildingIDs 6-8) stays 10, tiers 2/3/4 (every other zone) are 8. One field drives the
    /// trigger, the ring, zone presence/regen and the shop (BuildingCapture.captureRadius's own tooltip), so
    /// pinning this one number is enough. Read-only: the scene is used as it is loaded, or opened additively
    /// and closed again without saving - same pattern as TerritoryAdjacencySceneTests.
    /// </summary>
    public class CaptureRadiusSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        // buildingID -> expected captureRadius, all 10 zones (Tudor named T2/T3/T4 only; the capitals T1 stay put).
        private static readonly Dictionary<int, float> ExpectedRadiusByBuildingID = new Dictionary<int, float>
        {
            { 0, 8f },  // T2
            { 1, 8f },  // T2
            { 2, 8f },  // T2
            { 3, 8f },  // T3
            { 4, 8f },  // T3
            { 5, 8f },  // T3
            { 6, 10f }, // T1 capital
            { 7, 10f }, // T1 capital
            { 8, 10f }, // T1 capital
            { 9, 8f },  // T4 centre (already 8 before this change)
        };

        [Test]
        public void EveryZonesCaptureRadiusMatchesItsTier()
        {
            WithGameScene(scene =>
            {
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                Assert.AreEqual(ExpectedRadiusByBuildingID.Count, towers.Count, "expected 10 BuildingCapture zones in Game Scene");

                foreach (BuildingCapture tower in towers)
                {
                    Assert.IsTrue(ExpectedRadiusByBuildingID.TryGetValue(tower.buildingID, out float expected),
                        $"buildingID {tower.buildingID} is not one of the 10 known zones");
                    Assert.AreEqual(expected, tower.captureRadius, 0.001f,
                        $"buildingID {tower.buildingID} (tier {tower.tier}) captureRadius");
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
