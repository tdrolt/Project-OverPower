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
                // Cut-rule-followups, 2026-09-26: measured at cutActive: true, the Tier III radius the wall's recess
                // was actually sized around - centre.CaptureRadius reads the Tier IV radius here (edit mode has no
                // MatchDirector, so BuildingCapture.CaptureRadius's own live cut check never finds one active), which
                // would test the wrong, larger circle against the wall this test builds.
                float captureRadius = BuildingCapture.CaptureRadiusFor(centre.territoryConfig, centre.tier, cutActive: true);
                for (int i = 0; i < 72; i++)
                {
                    float radians = i * 5f * Mathf.Deg2Rad;
                    var edge = centrePosition + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * captureRadius;
                    Assert.GreaterOrEqual(geometry.Playable.SignedDistance(edge), 0f,
                        $"team {team}: the centre's own capture ring at {i * 5} degrees is behind the wall.");
                }
            });
        }

        // Task 4 review, E1 (2026-09-25): ArenaPhaseTwoCut hides pieces from Blocks, Barriers and Scenery alike
        // (HiddenGroups) - a staying piece from any of the three must not be cut by the wall either, not just Blocks.
        private static readonly string[] StayingPieceGroupNames =
            { ArenaSymmetry.BlocksGroupName, ArenaSymmetry.BarriersGroupName, ArenaSymmetry.SceneryGroupName };

        // Centre-Tier-III-walls, 2026-09-26: the old test above (a piece that stays never reaches behind the wall)
        // is true by construction now that ArenaPhaseTwoCut hides a piece with any footprint corner behind the wall
        // - so it was replaced with this, which pins the actual point of the three new walls: for each cut, exactly
        // one of the three copies of "Centre - Tier III wall (zone 4)" (one per third, keeping the Source name) has
        // every corner in front - and it must be the one nearest the Tier III PhaseTwoCutRules.CutZones does NOT cut
        // - while the other two (facing the closed corner) have a corner behind.
        private const string CentreWallName = "Centre - Tier III wall (zone 4)";

        [Test]
        public void TheCentreWallFacingTheSurvivingTierIIIStaysAndTheOtherTwoGoForEveryCorner([Values(0, 1, 2)] int team)
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());
                Func<int, int> baseTierOf = zone => towers.FirstOrDefault(t => t.buildingID == zone)?.tier ?? 0;
                List<int> cutZones = PhaseTwoCutRules.CutZones(map, team, baseTierOf);

                PhaseTwoCutGeometry geometry = BuildGeometry(arena, map, towers, team);
                Assert.IsNotNull(geometry, $"team {team}'s cut could not be built from the scene's own outline and layout.");

                var wallCopies = new List<Transform>();
                foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
                {
                    Transform group = third != null ? third.Find(ArenaSymmetry.BlocksGroupName) : null;
                    if (group == null)
                        continue;
                    foreach (Transform piece in group)
                        if (piece.name == CentreWallName)
                            wallCopies.Add(piece);
                }
                Assert.AreEqual(3, wallCopies.Count, "one copy per third - Build primitive arena should have made exactly three.");

                int stayingCount = 0;
                foreach (Transform wall in wallCopies)
                {
                    BuildingCapture nearestTierThree = towers.Where(t => t.tier == 3)
                        .OrderBy(t => Vector3.Distance(t.transform.position, wall.position)).First();
                    bool shouldStay = !cutZones.Contains(nearestTierThree.buildingID);
                    bool everyCornerInFront = !AnyFootprintCornerBehind(geometry, wall);
                    Assert.AreEqual(shouldStay, everyCornerInFront,
                        $"team {team}: '{wall.name}' nearest tower {nearestTierThree.buildingID} " +
                        (shouldStay ? "should have every corner in front but doesn't." : "should have a corner behind but doesn't."));
                    if (everyCornerInFront)
                        stayingCount++;
                }
                Assert.AreEqual(1, stayingCount, $"team {team}: exactly one of the three wall copies should stay (every corner in front).");
            });
        }

        private static bool AnyFootprintCornerBehind(PhaseTwoCutGeometry geometry, Transform piece)
        {
            Vector2[] corners = ArenaPieceShapes.FootprintCorners(piece.position, piece.rotation, piece.lossyScale);
            foreach (Vector2 corner in corners)
                if (geometry.IsBehindWall(new Vector3(corner.x, piece.position.y, corner.y)))
                    return true;
            return false;
        }

        // Centre-Tier-III-walls Part 2, 2026-09-26 ("the zone is too empty"): the two recess planks' footprints must
        // not overlap any staying Block/Barrier/Scenery piece or any tower, and must stay wholly inside Playable.
        [Test]
        public void ThePlanksOverlapNoStayingPieceOrTowerAndStayInsidePlayableForEveryCorner([Values(0, 1, 2)] int team)
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());

                PhaseTwoCutGeometry geometry = BuildGeometry(arena, map, towers, team);
                Assert.IsNotNull(geometry, $"team {team}'s cut could not be built from the scene's own outline and layout.");

                ArenaLayout layout = arena.layout;
                Vector3 plankSize = layout.PhaseTwoRecessPlankSize;
                if (plankSize.x <= 0f)
                    return; // no planks configured in this layout - nothing to check

                (Vector2 plankA, Vector2 plankB) = geometry.PlankCentres(layout.PhaseTwoRecessPlankSpacing, layout.PhaseTwoRecessPlankInFront);
                foreach (Vector2 plankCentre in new[] { plankA, plankB })
                {
                    Vector2[] plankFootprint = ArenaPieceShapes.FootprintCorners(plankCentre, geometry.BarrierYawDegrees,
                        new Vector2(plankSize.x, plankSize.z));

                    foreach (Vector2 corner in plankFootprint)
                        Assert.GreaterOrEqual(geometry.Playable.SignedDistance(corner), 0f,
                            $"team {team}: a recess plank at {plankCentre} has a corner outside Playable.");

                    foreach (BuildingCapture tower in towers)
                    {
                        if (geometry.IsBehindWall(tower.transform.position))
                            continue; // out of play anyway - not this test's concern
                        Vector2[] towerFootprint = ArenaPieceShapes.FootprintCorners(tower.transform.position,
                            tower.transform.rotation, tower.transform.lossyScale);
                        Assert.IsFalse(ArenaPieceShapes.FootprintsOverlap(plankFootprint, towerFootprint),
                            $"team {team}: a recess plank at {plankCentre} overlaps tower {tower.buildingID}.");
                    }

                    foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
                    {
                        if (third == null)
                            continue;
                        foreach (string groupName in StayingPieceGroupNames)
                        {
                            Transform group = third.Find(groupName);
                            if (group == null)
                                continue;
                            foreach (Transform piece in group)
                            {
                                if (geometry.IsBehindWall(piece.position))
                                    continue; // meant to disappear behind the wall - not this test's concern
                                Vector2[] pieceFootprint = ArenaPieceShapes.FootprintCorners(piece.position, piece.rotation, piece.lossyScale);
                                Assert.IsFalse(ArenaPieceShapes.FootprintsOverlap(plankFootprint, pieceFootprint),
                                    $"team {team}: a recess plank at {plankCentre} overlaps '{third.name}/{groupName}/{piece.name}'.");
                            }
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
