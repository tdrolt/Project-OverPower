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
    /// overlaps with some buildings") and 2026-09-24 ("for the t3 ring you can go under 5.5"): both retuned
    /// BuildingCapture.captureRadius in Game Scene. Rewritten 2026-09-24 (Tudor: "remove tests that are
    /// outdated" - he keeps retuning these radii from the Inspector, so a test pinning the per-zone table went red
    /// on every pass with nothing actually broken).
    ///
    /// Guards the RULES, never a tuning number: every zone of the same tier still shares one radius (the design
    /// intent - "T2 areas", "T3 areas" and so on, not ten independently-tuned circles), every radius is positive,
    /// and the resulting capture trigger (Building capture.cs's captureRadius - PlayerBodyRadius, ~line 167) never
    /// goes to zero or negative. captureRadius drives the trigger, the ring, zone presence/regen and the shop
    /// (BuildingCapture.captureRadius's own tooltip), so these checks cover all of them. Read-only: the scene is
    /// used as it is loaded, or opened additively and closed again without saving - same pattern as
    /// TerritoryAdjacencySceneTests.
    /// </summary>
    public class CaptureRadiusSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void EveryZoneOfTheSameTierSharesOneCaptureRadius()
        {
            WithGameScene(scene =>
            {
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                Assert.IsNotEmpty(towers, "expected BuildingCapture zones in Game Scene");

                foreach (IGrouping<int, BuildingCapture> tierGroup in towers.GroupBy(t => t.tier))
                {
                    float expected = tierGroup.First().captureRadius;
                    foreach (BuildingCapture tower in tierGroup)
                        Assert.AreEqual(expected, tower.captureRadius, 0.001f,
                            $"buildingID {tower.buildingID} (tier {tower.tier}) captureRadius should match every other zone of tier {tower.tier}");
                }
            });
        }

        [Test]
        public void EveryZonesCaptureRadiusIsPositive()
        {
            WithGameScene(scene =>
            {
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                Assert.IsNotEmpty(towers, "expected BuildingCapture zones in Game Scene");

                foreach (BuildingCapture tower in towers)
                    Assert.Greater(tower.captureRadius, 0f, $"buildingID {tower.buildingID} (tier {tower.tier}) captureRadius");
            });
        }

        [Test]
        public void EveryZonesTriggerStaysPositive()
        {
            // The trigger math (Building capture.cs ~167: collider.radius = max(0, captureRadius - PlayerBodyRadius)
            // / lossyScale.x) needs BuildingManager.Instance.PlayerBodyRadius, which only exists once a
            // BuildingManager has run Awake - Play Mode only. This is an edit-mode scene test, so skip rather than
            // guess at a number nothing here can actually read.
            if (BuildingManager.Instance == null)
            {
                Assert.Ignore("BuildingManager.Instance is only set in Play Mode - the trigger radius isn't reachable from an edit-mode test.");
                return;
            }

            float bodyRadius = BuildingManager.Instance.PlayerBodyRadius;
            WithGameScene(scene =>
            {
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                Assert.IsNotEmpty(towers, "expected BuildingCapture zones in Game Scene");

                foreach (BuildingCapture tower in towers)
                    Assert.Greater(tower.captureRadius, bodyRadius,
                        $"buildingID {tower.buildingID}: captureRadius ({tower.captureRadius}) must exceed PlayerBodyRadius ({bodyRadius}) or the trigger collapses to zero/negative");
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
