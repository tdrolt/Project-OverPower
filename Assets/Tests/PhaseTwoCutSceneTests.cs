using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Match;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// The real Game Scene, read-only: for each corner that could close (Tudor, 2026-09-25), the survivors' zones
    /// stay open, the cut ones close, the centre's whole capture ring stays reachable, and no Block that stays in
    /// play is cut by the new wall. Built from the scene's own ArenaSymmetry (Source Outline and Layout) and tower
    /// positions - a rule guard, not a pinned-number test: it only fails if a real layout value breaks the map (a
    /// staying piece crossed by the wall, the centre's ring poking out), never over 6.3 / 19.54 / 3.25 themselves.
    /// </summary>
    public class PhaseTwoCutSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void TheArenaKnowsItsLayout()
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                Assert.IsNotNull(arena.layout,
                    "ArenaSymmetry.layout is not assigned - the phase-two wall has no numbers to build from.");
            });
        }

        [Test]
        public void TheSurvivorsZonesStayOpenAndTheCutOnesCloseForEveryCorner([Values(0, 1, 2)] int team)
        {
            WithGameScene(scene =>
            {
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());
                Func<int, int> baseTierOf = zone => towers.FirstOrDefault(t => t.buildingID == zone)?.tier ?? 0;

                PhaseTwoCutGeometry geometry = BuildGeometry(Find<ArenaSymmetry>(scene).Single(), map, towers, team);
                Assert.IsNotNull(geometry, $"team {team}'s cut could not be built from the scene's own outline and layout.");

                foreach (BuildingCapture tower in towers)
                {
                    bool shouldBeCut = PhaseTwoCutRules.IsZoneCut(map, tower.buildingID, team, baseTierOf);
                    if (shouldBeCut)
                        Assert.IsTrue(geometry.IsBehindWall(tower.transform.position),
                            $"team {team}: tower {tower.buildingID} should be behind the wall but isn't.");
                    else
                        Assert.Greater(geometry.Playable.SignedDistance(tower.transform.position), 0f,
                            $"team {team}: tower {tower.buildingID} should stay open but the wall crosses it.");
                }
            });
        }

        [Test]
        public void TheCentresWholeCaptureAreaStaysReachableForEveryCorner([Values(0, 1, 2)] int team)
        {
            WithGameScene(scene =>
            {
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());

                PhaseTwoCutGeometry geometry = BuildGeometry(Find<ArenaSymmetry>(scene).Single(), map, towers, team);
                Assert.IsNotNull(geometry, $"team {team}'s cut could not be built from the scene's own outline and layout.");

                BuildingCapture centre = towers.Single(t => t.tier == 4);
                Vector3 centrePosition = centre.transform.position;
                for (int i = 0; i < 72; i++)
                {
                    float radians = i * 5f * Mathf.Deg2Rad;
                    var edge = centrePosition + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * centre.CaptureRadius;
                    Assert.GreaterOrEqual(geometry.Playable.SignedDistance(edge), 0f,
                        $"team {team}: the centre's own capture ring at {i * 5} degrees is behind the wall.");
                }
            });
        }

        // Task 4 review, E1 (2026-09-25): ArenaPhaseTwoCut hides pieces from Blocks, Barriers and Scenery alike
        // (HiddenGroups) - a staying piece from any of the three must not be cut by the wall either, not just Blocks.
        private static readonly string[] StayingPieceGroupNames =
            { ArenaSymmetry.BlocksGroupName, ArenaSymmetry.BarriersGroupName, ArenaSymmetry.SceneryGroupName };

        [Test]
        public void NoStayingPieceIsCutByTheNewWallForEveryCorner([Values(0, 1, 2)] int team)
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());

                PhaseTwoCutGeometry geometry = BuildGeometry(arena, map, towers, team);
                Assert.IsNotNull(geometry, $"team {team}'s cut could not be built from the scene's own outline and layout.");

                foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
                {
                    if (third == null)
                        continue;
                    foreach (string groupName in StayingPieceGroupNames)
                    {
                        Transform group = third.Find(groupName);
                        if (group == null)
                            continue; // skip a group that doesn't exist under this third
                        foreach (Transform piece in group)
                        {
                            if (geometry.IsBehindWall(piece.position))
                                continue; // meant to disappear behind the wall - not this test's concern

                            Vector3 right = piece.right * (piece.lossyScale.x * 0.5f);
                            Vector3 forward = piece.forward * (piece.lossyScale.z * 0.5f);
                            Vector3[] corners =
                            {
                                piece.position + right + forward, piece.position + right - forward,
                                piece.position - right + forward, piece.position - right - forward,
                            };
                            foreach (Vector3 corner in corners)
                                Assert.Less(geometry.Closed.SignedDistance(corner), 0f,
                                    $"team {team}: '{third.name}/{groupName}/{piece.name}' stays in play but the new wall cuts through it.");
                        }
                    }
                }
            });
        }

        private static PhaseTwoCutGeometry BuildGeometry(ArenaSymmetry arena, TerritoryMap map, List<BuildingCapture> towers, int team)
        {
            int capital = map.CapitalOf(team);
            BuildingCapture capitalTower = towers.FirstOrDefault(t => t.buildingID == capital);
            Assert.IsNotNull(capitalTower, $"team {team}'s capital (zone {capital}) has no BuildingCapture in the scene.");

            IReadOnlyList<Vector2> outline = ArenaBounds.FromSourceOutline(arena.sourceOutline, arena.centre).Polygon;
            var centre = new Vector2(arena.centre.x, arena.centre.z);
            Vector3 capitalPosition = capitalTower.transform.position;
            ArenaLayout layout = arena.layout;
            return PhaseTwoCutGeometry.Build(outline, centre, new Vector2(capitalPosition.x, capitalPosition.z) - centre,
                layout.PhaseTwoWallDistance, layout.PhaseTwoRecessWidth, layout.PhaseTwoRecessDepth, layout.WallThickness);
        }

        // The same construction BuildingManager.BuildMap does at runtime (TerritoryAdjacencySceneTests' own helper).
        private static TerritoryMap MapOf(BuildingManager manager) =>
            new TerritoryMap(
                manager.TowerDictionary.Select(pair => (pair.Key, (IEnumerable<int>)pair.Value.Adjacents)).ToList(),
                manager.CathedralBuildingIDs.Select(pair => (pair.Key, pair.Value)).ToList());

        // Same helper as TerritoryAdjacencySceneTests/ArenaSymmetrySceneTests: never opens the scene Single, which
        // would prompt to save a dirty scene.
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
