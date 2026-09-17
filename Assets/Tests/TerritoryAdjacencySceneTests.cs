using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Match;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Guards Game Scene's own territory links (GDD p.27, Tudor 2026-09-16). TerritoryMapTests test the capture rule on
    /// a copy of these links; this test fails if the scene's BuildingManager.TowerDictionary drifts from that copy.
    /// Read-only: the scene is used as it is loaded, or opened additively and closed again without saving.
    /// </summary>
    public class TerritoryAdjacencySceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void TheSceneLinksTheCentreToEveryTier2AndTier3()
        {
            WithGameScene(scene =>
            {
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, map.AdjacentTo(9));
            });
        }

        [Test]
        public void TheScenesCapitalsStillLinkOnlyToTheirOwnTier2()
        {
            WithGameScene(scene =>
            {
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());
                CollectionAssert.AreEqual(new[] { 0 }, map.AdjacentTo(6));
                CollectionAssert.AreEqual(new[] { 1 }, map.AdjacentTo(7));
                CollectionAssert.AreEqual(new[] { 2 }, map.AdjacentTo(8));
            });
        }

        // The same construction BuildingManager.BuildMap does at runtime.
        private static TerritoryMap MapOf(BuildingManager manager) =>
            new TerritoryMap(
                manager.TowerDictionary.Select(pair => (pair.Key, (IEnumerable<int>)pair.Value.Adjacents)).ToList(),
                manager.CathedralBuildingIDs.Select(pair => (pair.Key, pair.Value)).ToList());

        // Same helper as ArenaSymmetrySceneTests: never opens the scene Single, which would prompt to save a dirty scene.
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
