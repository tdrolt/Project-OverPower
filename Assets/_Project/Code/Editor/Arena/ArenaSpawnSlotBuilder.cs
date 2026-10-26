using System.Collections.Generic;
using System.Linq;
using Overpower.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.EditorTools
{
    /// <summary>
    /// Gives each of the arena's team spawn points one child point per player (ArenaLayout's Spawn Slot Offsets), so teammates appear
    /// side by side instead of inside each other (RoomManager.SpawnPointFor picks the child). Safe to run again: it replaces only its own
    /// "Slot n" children. Rebuild thirds moves a spawn point with its children, so the slots keep their place without being rebuilt.
    /// </summary>
    public static class ArenaSpawnSlotBuilder
    {
        public const string SlotPrefix = "Slot ";
        private const string PlayerPrefabPath = "Assets/_Project/Resources/Multiplayer Player.prefab";

        /// <summary>Places the slots under every team spawn point of the scene's RoomManager and reports, one line each, any slot whose
        /// player-sized capsule touches a collider (a "PROBLEM" line). Never runs in Play Mode; never saves the scene.</summary>
        public static List<string> PlaceAll(Scene scene, ArenaLayout layout)
        {
            var report = new List<string>();
            if (EditorApplication.isPlaying) { report.Add("PROBLEM: can't place spawn slots in Play Mode."); return report; }
            if (layout == null) { report.Add("PROBLEM: no ArenaLayout given."); return report; }
            List<RoomManager> rooms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RoomManager>(true)).ToList();
            if (rooms.Count != 1) { report.Add($"PROBLEM: found {rooms.Count} RoomManagers in the scene (need exactly one)."); return report; }

            IReadOnlyList<Vector3> offsets = layout.SpawnSlotOffsets;
            CapsuleCollider body = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath)?.GetComponent<CapsuleCollider>();
            foreach (Transform home in rooms[0].teamSpawnPoints)
            {
                if (home == null) continue;
                PlaceSlots(home, offsets);
                report.Add($"{home.name}: {offsets.Count} slots.");
                if (body != null) report.AddRange(Blocked(home, body, scene));
            }
            if (!EditorSceneManager.IsPreviewScene(scene)) EditorSceneManager.MarkSceneDirty(scene);
            return report;
        }

        /// <summary>Replaces home's "Slot n" children with one per offset (local metres: x right, z forward), facing like home, placed first
        /// among its children because SpawnPointFor picks a child by its index. Other children are kept.</summary>
        public static void PlaceSlots(Transform home, IReadOnlyList<Vector3> offsets)
        {
            for (int i = home.childCount - 1; i >= 0; i--)
                if (home.GetChild(i).name.StartsWith(SlotPrefix)) Object.DestroyImmediate(home.GetChild(i).gameObject);
            for (int i = 0; i < offsets.Count; i++)
            {
                var slot = new GameObject(SlotPrefix + (i + 1)).transform;
                slot.SetParent(home, false);
                slot.localPosition = offsets[i];
                slot.localRotation = Quaternion.identity;
                slot.localScale = Vector3.one;
                slot.SetSiblingIndex(i);
            }
        }

        // The capsule starts just above the floor: the floor is not a wall, and the body's own capsule dips below Y 0.
        private static IEnumerable<string> Blocked(Transform home, CapsuleCollider body, Scene scene)
        {
            Physics.SyncTransforms();
            float bottom = body.radius + 0.05f, top = Mathf.Max(bottom, body.center.y + body.height * 0.5f - body.radius);
            for (int i = 0; i < home.childCount; i++)
            {
                Transform slot = home.GetChild(i);
                if (!slot.name.StartsWith(SlotPrefix)) continue;
                string hits = string.Join(", ", Physics.OverlapCapsule(slot.position + Vector3.up * bottom, slot.position + Vector3.up * top, body.radius, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => h.gameObject.scene == scene).Select(h => h.name));
                if (hits.Length > 0) yield return $"PROBLEM: {home.name}/{slot.name} touches {hits}: move its row in ArenaLayout's Spawn Slot Offsets.";
            }
        }

        [MenuItem("OverPower/Arena/Place spawn slots")]
        private static void MenuPlaceAll()
        {
            ArenaLayout layout = AssetDatabase.LoadAssetAtPath<ArenaLayout>("Assets/_Project/Shared/Config/ArenaLayout.asset");
            Debug.Log("[ArenaSpawnSlotBuilder] Place spawn slots:\n" + string.Join("\n", PlaceAll(SceneManager.GetActiveScene(), layout)));
        }
    }
}
