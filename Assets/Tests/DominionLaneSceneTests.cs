using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Tasks 11 and 12: the lane scene's per-scene data, read from the saved scene (opened additively and closed again - Game Scene stays the
    /// open, clean scene): the zones, the capitals, the links, the arena outline flag, the scene view ids, the H as blocks, the tower bodies' size,
    /// the two spawn points per team and the rectangular minimap. Game Scene is read for the flags that must stay as they were.
    /// </summary>
    public class DominionLaneSceneTests
    {
        private const string LanePath = "Assets/Scenes/Dominion 2v2.unity";
        private const string GamePath = "Assets/Scenes/Game Scene.unity";

        private static void WithScene(string path, System.Action<Scene> check)
        {
            Scene existing = SceneManager.GetSceneByPath(path);
            bool wasOpen = existing.IsValid() && existing.isLoaded;
            Scene scene = wasOpen ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try { check(scene); }
            finally
            {
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

        private static DominionLaneLayout Layout() => AssetDatabase.LoadAssetAtPath<DominionLaneLayout>(DominionLaneBuilder.LayoutPath);

        [Test] public void TheChatManagerStartsSwitchedOffInEveryScene()
        {
            // NameScreen switches the chat on when a room is joined; no scene may override the shared prefab's "off" (an idle title-screen client holds no chat connection).
            foreach (string path in new[] { LanePath, GamePath })
                WithScene(path, scene =>
                {
                    Transform chat = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "chat manager");
                    Assert.IsNotNull(chat, path);
                    Assert.IsFalse(chat.gameObject.activeSelf, path);
                });
        }

        [Test] public void TheLaneSceneHasZonesZeroToThreeInARow()
        {
            WithScene(LanePath, scene =>
            {
                int[] ids = All<BuildingCapture>(scene).Select(t => t.buildingID).OrderBy(i => i).ToArray();
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, ids, "zone ids are contiguous from 0 (the zone count is the highest id + 1)");
            });
        }

        [Test] public void TheLaneSceneCapitalsAreTheTwoSpawnTowersOfTeamsZeroAndOne()
        {
            WithScene(LanePath, scene =>
            {
                BuildingManager manager = All<BuildingManager>(scene).Single();
                var capitals = manager.CathedralBuildingIDs;
                Assert.AreEqual(2, capitals.Count);
                Assert.AreEqual(0, capitals[2], "zone 2 is White's");
                Assert.AreEqual(1, capitals[3], "zone 3 is Purple's");
            });
        }

        [Test] public void TheLaneSceneLinksJoinEachSpawnToBothZonesAndTheZonesToEachOther()
        {
            WithScene(LanePath, scene =>
            {
                BuildingManager manager = All<BuildingManager>(scene).Single();
                SerializedProperty list = new SerializedObject(manager).FindProperty("TowerDictionary.dictionaryList");
                var links = new Dictionary<int, int[]>();
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty e = list.GetArrayElementAtIndex(i);
                    SerializedProperty adj = e.FindPropertyRelative("Value.Adjacents");
                    links[e.FindPropertyRelative("Key").intValue] = Enumerable.Range(0, adj.arraySize).Select(k => adj.GetArrayElementAtIndex(k).intValue).OrderBy(x => x).ToArray();
                }
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, links[0]);
                CollectionAssert.AreEqual(new[] { 0, 2, 3 }, links[1]);
                CollectionAssert.AreEqual(new[] { 0, 1 }, links[2]);
                CollectionAssert.AreEqual(new[] { 0, 1 }, links[3]);
            });
        }

        [Test] public void TheArenaOutlineIsTheWholeMapOnTheLaneAndAThirdInGameScene()
        {
            WithScene(LanePath, scene => Assert.IsTrue(All<ArenaSymmetry>(scene).Single().outlineIsWholeArena));
            WithScene(GamePath, scene => Assert.IsFalse(All<ArenaSymmetry>(scene).Single().outlineIsWholeArena, "the triangle arena's outline is one third"));
        }

        [Test] public void NoSceneViewIdIsZeroOrUsedTwiceInEitherScene()
        {
            foreach (string path in new[] { LanePath, GamePath })
                WithScene(path, scene =>
                {
                    List<string> report = DominionLaneBuilder.CheckSceneViewIds(scene);
                    Assert.IsFalse(report.Any(l => l.StartsWith("PROBLEM")), path + ": " + string.Join(" | ", report));
                });
        }

        [Test] public void TheHIsMadeOfBlocksAndTheOuterWallsAreBoundaryWalls()
        {
            WithScene(LanePath, scene =>
            {
                ArenaSymmetry arena = All<ArenaSymmetry>(scene).Single();
                Transform boundry = arena.source.Find(ArenaSymmetry.BoundaryGroupName);
                Transform blocks = arena.source.Find(ArenaSymmetry.BlocksGroupName);
                foreach (LaneRect wall in Layout().Walls)
                {
                    Transform piece = (DominionLaneLayout.IsBlockWall(wall.name) ? blocks : boundry).Find(wall.name);
                    Assert.IsNotNull(piece, $"{wall.name} is in the wrong group");
                }
                Assert.IsNull(boundry.Find("H Wall Left"), "a boundary wall would stop a Portal");
            });
        }

        [Test] public void TheTowerBodiesAreTheBoardsSizeNotTheTrianglesFivePointTwo()
        {
            WithScene(LanePath, scene =>
            {
                DominionLaneLayout layout = Layout();
                foreach (BuildingCapture tower in All<BuildingCapture>(scene))
                {
                    CapsuleCollider body = tower.transform.Find("Tower Look").GetComponent<CapsuleCollider>();
                    float width = body.radius * 2f * Mathf.Max(body.transform.lossyScale.x, body.transform.lossyScale.z);
                    float wanted = tower.buildingID < 2 ? layout.ZoneTowerSizeMetres : layout.SpawnTowerSizeMetres;
                    Assert.AreEqual(wanted, width, 0.05f, $"{tower.name}'s body");
                }
            });
        }

        [Test] public void EachTeamHasTwoSpawnPointsToEitherSideOfItsSpawnPoint()
        {
            WithScene(LanePath, scene =>
            {
                RoomManager rooms = All<RoomManager>(scene).Single();
                float offset = Layout().SpawnSideOffsetMetres;
                for (int team = 0; team < 2; team++)
                {
                    Transform home = rooms.teamSpawnPoints[team];
                    Assert.AreEqual(2, home.childCount, $"team {team}");
                    Vector3 a = home.GetChild(0).position, b = home.GetChild(1).position;
                    Assert.AreEqual(2f * offset, Vector3.Distance(a, b), 0.01f);
                    Assert.AreEqual(0f, Vector3.Distance((a + b) * 0.5f, home.position), 0.01f, "the pair is centred on the spawn point");
                }
            });
            WithScene(GamePath, scene =>
            {
                foreach (Transform home in All<RoomManager>(scene).Single().teamSpawnPoints)
                    Assert.AreEqual(0, home.childCount, "the triangle arena keeps one spawn point per team");
            });
        }

        [Test] public void TheLaneMinimapIsARectangleAndTheArenaKeepsItsTriangle()
        {
            WithScene(LanePath, scene =>
            {
                MinimapConfig config = All<SceneMinimapConfig>(scene).Single().Config;
                Assert.IsTrue(config.RectangularFrame);
                Assert.Greater(config.WorldSizeMetres, config.WorldDepthMetres * 1.5f, "the lane is much longer than it is wide");
            });
            MinimapConfig arena = AssetDatabase.LoadAssetAtPath<MinimapConfig>("Assets/Gameplay/Config/MinimapConfig.asset");
            Assert.IsFalse(arena.RectangularFrame);
            Assert.AreEqual(arena.WorldSizeMetres, arena.WorldDepthMetres, "a triangle arena's picture is a square");
        }
    }
}
