using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overpower.TestRange;

namespace Overpower.Tests
{
    /// <summary>Item A (arena rebuild, controller finding, Decision 20): the practice range's dummies must
    /// stand on open ground, clear of the capital they used to overlap. TestRangeSpawner.SpawnRange only ever
    /// runs in Play Mode (it needs a live RoomManager/PhotonNetwork), so this pins the same PURE position math
    /// SpawnRange calls (Baseline/StationaryPoint/StrafeCentre/Grounded, all public static for exactly this
    /// reason) against the real scene's Building and Barrier colliders - no Play Mode needed, and no way for
    /// the test to silently drift from what actually spawns.</summary>
    public class TestRangeSpawnerTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void EveryDummySpawnPointIsClearOfBuildingAndBarrierColliders()
        {
            WithGameScene(scene =>
            {
                TestRangeSpawner spawner = Find<TestRangeSpawner>(scene).FirstOrDefault();
                Assert.IsNotNull(spawner, "no TestRangeSpawner in the scene");

                var NP = BindingFlags.NonPublic | BindingFlags.Instance;
                RoomManager roomManager = (RoomManager)typeof(TestRangeSpawner).GetField("roomManager", NP).GetValue(spawner);
                GameObject dummyPrefab = (GameObject)typeof(TestRangeSpawner).GetField("dummyTargetPrefab", NP).GetValue(spawner);
                float[] stationaryDistances = (float[])typeof(TestRangeSpawner).GetField("stationaryDistances", NP).GetValue(spawner);
                float lateralSpacing = (float)typeof(TestRangeSpawner).GetField("lateralSpacing", NP).GetValue(spawner);
                float sidewaysOffset = (float)typeof(TestRangeSpawner).GetField("rangeSidewaysOffsetMetres", NP).GetValue(spawner);
                float strafeRowDistance = (float)typeof(TestRangeSpawner).GetField("strafeRowDistance", NP).GetValue(spawner);
                int strafingDummyCount = (int)typeof(TestRangeSpawner).GetField("strafingDummyCount", NP).GetValue(spawner);
                float strafeDistance = (float)typeof(TestRangeSpawner).GetField("strafeDistance", NP).GetValue(spawner);

                Assert.IsNotNull(roomManager, "TestRangeSpawner has no Room Manager assigned");
                Assert.IsNotNull(dummyPrefab, "TestRangeSpawner has no Dummy Target Prefab assigned");

                Transform spawn = roomManager.teamSpawnPoints[0];
                Vector3 forward = spawn.forward;
                Vector3 right = spawn.right;
                Vector3 baseline = TestRangeSpawner.Baseline(spawn.position, right, sidewaysOffset);

                CapsuleCollider dummyCapsule = dummyPrefab.GetComponent<CapsuleCollider>();
                Assert.IsNotNull(dummyCapsule, "the dummy prefab has no CapsuleCollider");
                float radius = dummyCapsule.radius * dummyPrefab.transform.localScale.x;
                float halfSegment = Mathf.Max(0f, dummyCapsule.height * 0.5f - dummyCapsule.radius) * dummyPrefab.transform.localScale.y;

                int mask = LayerMask.GetMask("Building", "Barrier");
                var problems = new List<string>();

                void CheckPoint(string label, Vector3 groundPoint)
                {
                    Vector3 root = TestRangeSpawner.Grounded(groundPoint, dummyPrefab);
                    Vector3 centre = root + Vector3.up * (dummyCapsule.center.y * dummyPrefab.transform.localScale.y);
                    Vector3 top = centre + Vector3.up * halfSegment;
                    Vector3 bottom = centre - Vector3.up * halfSegment;
                    bool overlaps = Physics.CheckCapsule(top, bottom, radius * 0.98f, mask, QueryTriggerInteraction.Ignore);
                    if (overlaps)
                        problems.Add($"{label}: root={root:F2} overlaps a Building/Barrier collider");
                }

                for (int i = 0; i < stationaryDistances.Length; i++)
                    CheckPoint($"stationary[{i}] ({stationaryDistances[i]} m)",
                        TestRangeSpawner.StationaryPoint(baseline, forward, right, stationaryDistances[i], lateralSpacing, i));

                for (int i = 0; i < strafingDummyCount; i++)
                {
                    Vector3 centre = TestRangeSpawner.StrafeCentre(baseline, forward, right, strafeRowDistance, lateralSpacing, i);
                    // The strafer patrols +/- strafeDistance along "right" from its centre - both extremes must
                    // also be clear, or the dummy would walk into solid geometry mid-patrol.
                    CheckPoint($"strafe[{i}] centre", centre);
                    CheckPoint($"strafe[{i}] +extreme", centre + right * strafeDistance);
                    CheckPoint($"strafe[{i}] -extreme", centre - right * strafeDistance);
                }

                Assert.IsEmpty(problems, "Dummy spawn point(s) overlapping solid geometry:\n" + string.Join("\n", problems));
            });
        }

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
