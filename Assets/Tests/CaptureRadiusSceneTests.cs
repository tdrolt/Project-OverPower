using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Data;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Refactor 2026-09-26: the capture radius moved off each tower and into TerritoryConfig's per-tier rows (see
    /// BuildingCaptureRadiusTests for the wiring itself, and TierOf/ForTier for the read). Guards the RULES, never
    /// a tuning number (project rule since 91eceb4): every tier's radius must still clear the player's body
    /// radius, or the capture trigger (Building capture.cs's ConfigureCollider) collapses to zero/negative, and
    /// every tower in the real scene must actually point at a Territory Config, or it silently falls back to
    /// FallbackCaptureRadius (10 m) instead of reading its tier's real row. "Every zone of the same tier shares one
    /// radius" is deleted (not rewritten): it is true by construction now that the radius lives once per tier
    /// instead of once per tower, so there is nothing left for that test to catch. Read-only: the scene is used as
    /// it is loaded, or opened additively and closed again without saving - same pattern as
    /// TerritoryAdjacencySceneTests.
    /// </summary>
    public class CaptureRadiusSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void EveryTiersCaptureRadiusClearsThePlayersBodyRadius()
        {
            WithGameScene(scene =>
            {
                TerritoryConfig config = Find<BuildingCapture>(scene).Select(t => t.territoryConfig).FirstOrDefault(c => c != null);
                Assert.IsNotNull(config, "expected at least one BuildingCapture in Game Scene pointing at a Territory Config");

                float bodyRadius = BuildingManager.ReadPlayerBodyRadius();
                for (int tier = 1; tier <= config.TierCount; tier++)
                    Assert.Greater(config.ForTier(tier).captureRadius, bodyRadius,
                        $"tier {tier}'s captureRadius must exceed PlayerBodyRadius ({bodyRadius}) or the trigger collapses to zero/negative");
            });
        }

        [Test]
        public void EveryTowerInTheScenePointsAtATerritoryConfig()
        {
            WithGameScene(scene =>
            {
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                Assert.IsNotEmpty(towers, "expected BuildingCapture zones in Game Scene");

                foreach (BuildingCapture tower in towers)
                    Assert.IsNotNull(tower.territoryConfig,
                        $"buildingID {tower.buildingID} has no Territory Config - it would fall back to a fixed radius instead of its tier's real row");
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
