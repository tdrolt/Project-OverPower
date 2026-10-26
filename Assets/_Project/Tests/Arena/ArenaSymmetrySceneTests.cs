using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Guards the saved arena: every generated object and snapped partner must match the Source third, and every
    /// capital, T2 and T3 tower (plus its flag carpet) must be kept symmetric by the tool. It fails when someone edits a
    /// generated third by hand, forgets to press Rebuild thirds after editing Source, or adds a tower the tool doesn't
    /// know about.
    /// </summary>
    public class ArenaSymmetrySceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void TheArenaMatchesItsSourceThird()
        {
            WithGameScene(scene =>
            {
                List<string> problems = ArenaSymmetryBuilder.Validate(Find<ArenaSymmetry>(scene).Single());
                Assert.IsEmpty(problems,
                    "Open Game Scene, select Enviorment/Arena and press Rebuild thirds, then save:\n" + string.Join("\n", problems));
            });
        }

        [Test]
        public void EveryCapitalT2AndT3TowerAndItsCarpetIsKeptSymmetric()
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                var snapped = new HashSet<Transform>(arena.snappedTriplets.SelectMany(t => new[] { t.source, t.at120, t.at240 }));
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();

                foreach (int tier in new[] { 1, 2, 3 })
                {
                    List<BuildingCapture> ofTier = towers.Where(b => b.tier == tier).ToList();
                    Assert.AreEqual(3, ofTier.Count, $"Tier {tier} should have one tower per team.");
                    foreach (BuildingCapture tower in ofTier)
                    {
                        Assert.IsTrue(snapped.Contains(tower.transform), $"Tower {tower.buildingID} (tier {tier}) is not in a Snapped Triplet.");
                        Assert.IsTrue(tower.flagRenderer != null && snapped.Contains(tower.flagRenderer.transform),
                                      $"Tower {tower.buildingID}'s flag carpet is not in a Snapped Triplet.");
                    }
                }

                BuildingCapture centre = towers.Single(b => b.tier == 4);
                Assert.IsTrue(arena.centred.Contains(centre.transform), "The Tier 4 tower is not in Centred.");
            });
        }

        [Test]
        public void EverySpawnPointAndTowerIsInsideTheArenaOutline()
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                ArenaBounds bounds = ArenaBounds.FromSourceOutline(arena.sourceOutline, arena.centre);
                Assert.IsNotNull(bounds, "Source Outline is empty: set it to the boundary walls' inner faces.");

                // The real player's own capsule, so this test can't disagree with what blink and portals check against.
                float radius = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Multiplayer Player.prefab")
                    .GetComponent<CapsuleCollider>().radius;

                RoomManager rooms = Find<RoomManager>(scene).Single();
                foreach (Transform spawn in rooms.teamSpawnPoints.Concat(rooms.capitalUnderAttackSpawnPoints))
                {
                    if (spawn == null) continue;
                    Assert.GreaterOrEqual(bounds.SignedDistance(spawn.position), radius,
                        $"{spawn.name} is not a player's width inside the arena outline.");
                }

                foreach (BuildingCapture tower in Find<BuildingCapture>(scene))
                    Assert.Greater(bounds.SignedDistance(tower.transform.position), 0f,
                        $"Tower {tower.buildingID} is outside the arena outline.");
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
