using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Guards the saved Game Scene: every team spawn has a slot point per seat of the biggest team that plays there, far enough apart
    /// that two players never appear inside each other, facing like their spawn point, inside the arena and clear of walls. Fails when
    /// the slots are deleted or a layout row puts one into a wall; OverPower > Arena > Place spawn slots (then save) fixes it.
    /// </summary>
    public class GameSceneSpawnSlotSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Game Scene.unity";

        [Test]
        public void EveryTeamSpawnHasASlotPerSeatApartFacingItsPointAndClearOfWalls()
        {
            int seats = AssetDatabase.FindAssets("t:GameModeDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<GameModeDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m != null && m.SceneName == "Game Scene")
                .Select(m => m.SeatsPerTeam).DefaultIfEmpty(0).Max();
            Assert.Greater(seats, 0, "no game mode plays on Game Scene");

            CapsuleCollider body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Multiplayer Player.prefab").GetComponent<CapsuleCollider>();
            float apart = 2f * body.radius + 0.2f; // two players side by side, plus a margin
            // From just above the floor (the floor is not a wall) to the top of the body.
            float bottom = body.radius + 0.05f, top = Mathf.Max(bottom, body.center.y + body.height * 0.5f - body.radius);

            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                ArenaBounds bounds = ArenaBounds.FromSourceOutline(arena.sourceOutline, arena.centre);
                RoomManager rooms = Find<RoomManager>(scene).Single();
                Physics.SyncTransforms();

                foreach (Transform spawn in rooms.teamSpawnPoints.Where(s => s != null))
                {
                    Assert.GreaterOrEqual(spawn.childCount, seats, $"{spawn.name} needs a slot per seat ({seats})");
                    var slots = spawn.Cast<Transform>().ToList();
                    foreach (Transform slot in slots)
                    {
                        Assert.Less(Quaternion.Angle(spawn.rotation, slot.rotation), 0.5f, $"{spawn.name}/{slot.name} does not face like its spawn point");
                        Assert.GreaterOrEqual(bounds.SignedDistance(slot.position), body.radius, $"{spawn.name}/{slot.name} is not a player's width inside the arena");
                        Collider[] hits = Physics.OverlapCapsule(slot.position + Vector3.up * bottom, slot.position + Vector3.up * top, body.radius, ~0, QueryTriggerInteraction.Ignore);
                        Assert.IsEmpty(hits.Where(h => h.gameObject.scene == scene).Select(h => h.name).ToList(), $"{spawn.name}/{slot.name} touches a collider");
                    }
                    for (int a = 0; a < slots.Count; a++)
                        for (int b = a + 1; b < slots.Count; b++)
                        {
                            Vector3 d = slots[a].position - slots[b].position;
                            Assert.GreaterOrEqual(new Vector2(d.x, d.z).magnitude, apart, $"{spawn.name}: {slots[a].name} and {slots[b].name} are too close");
                        }
                }
            });
        }

        // Uses Game Scene if it is already open, otherwise opens it additively and closes it again (never Single: a dirty scene would prompt).
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
