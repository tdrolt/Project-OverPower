using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Overpower.Data;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Task 11: the 2v2 lane as Tudor drew it (DominionLaneLayout.asset). These guard the structure of the map, never his numbers: every piece
    /// has its mirror image, the spawns are the 56 m of the board's title apart, no two things a player must walk between are closer than the board's
    /// 2.75 m, the zones fit the Tier 3 circle of the Territory Config, and each team has a spawn and a healing ground. The real asset is read.
    /// </summary>
    public class DominionLaneLayoutTests
    {
        private const float Tolerance = 0.02f;

        private static DominionLaneLayout Layout()
        {
            var layout = AssetDatabase.LoadAssetAtPath<DominionLaneLayout>("Assets/Gameplay/Config/DominionLaneLayout.asset");
            Assert.IsNotNull(layout, "Assets/Gameplay/Config/DominionLaneLayout.asset is missing: run OverPower > Dominion > Write lane layout from the board");
            return layout;
        }

        private static bool HasPoint(IEnumerable<Vector2> points, Vector2 wanted)
        {
            foreach (Vector2 p in points) if (Vector2.Distance(p, wanted) <= Tolerance) return true;
            return false;
        }

        private static bool HasRect(IEnumerable<LaneRect> rects, LaneRect wanted)
        {
            foreach (LaneRect r in rects)
                if (Vector2.Distance(r.centre, wanted.centre) <= Tolerance && Vector2.Distance(r.size, wanted.size) <= Tolerance) return true;
            return false;
        }

        // ---------------------------------------------------------------- mirror symmetry

        [Test] public void TheLayoutIsNotEmpty()
        {
            DominionLaneLayout layout = Layout();
            Assert.Greater(layout.Walls.Count, 0);
            Assert.Greater(layout.Barriers.Count, 0);
            Assert.Greater(layout.Boxes.Count, 0);
            Assert.AreEqual(2, layout.ZoneCentres.Count, "a zone at the top of the hall and one at the bottom");
            Assert.AreEqual(2, layout.Spawns.Count);
            Assert.GreaterOrEqual(layout.Outline.Count, 3);
        }

        [Test] public void EveryWallBarrierAndBoxHasItsMirrorImageOnTheOtherSideOfBothAxes()
        {
            DominionLaneLayout layout = Layout();
            foreach (LaneRect wall in layout.Walls)
            {
                Assert.IsTrue(HasRect(layout.Walls, new LaneRect("", LaneGeometry.FlipX(wall.centre), wall.size)), $"{wall.name} has no mirror across the short axis");
                Assert.IsTrue(HasRect(layout.Walls, new LaneRect("", LaneGeometry.FlipZ(wall.centre), wall.size)), $"{wall.name} has no mirror across the long axis");
            }
            foreach (LaneRect barrier in layout.Barriers)
                Assert.IsTrue(HasRect(layout.Barriers, new LaneRect("", LaneGeometry.Opposite(barrier.centre), barrier.size)), $"{barrier.name} has no opposite");
            foreach (Vector2 box in layout.Boxes)
            {
                Assert.IsTrue(HasPoint(layout.Boxes, LaneGeometry.FlipX(box)), $"box at {box} has no mirror across the short axis");
                Assert.IsTrue(HasPoint(layout.Boxes, LaneGeometry.FlipZ(box)), $"box at {box} has no mirror across the long axis");
            }
        }

        [Test] public void TheZonesSitOneAboveTheOtherAndTheSpawnsFaceEachOther()
        {
            DominionLaneLayout layout = Layout();
            foreach (Vector2 zone in layout.ZoneCentres)
                Assert.IsTrue(HasPoint(layout.ZoneCentres, LaneGeometry.FlipZ(zone)), $"zone at {zone} has no mirror across the long axis");
            LaneSpawn a = layout.Spawns[0], b = layout.Spawns[1];
            Assert.AreEqual(0f, a.towerCentre.x + b.towerCentre.x, Tolerance, "the spawn towers are mirror images");
            Assert.AreEqual(a.towerCentre.y, b.towerCentre.y, Tolerance);
            Assert.AreEqual(0f, a.spawnPoint.x + b.spawnPoint.x, Tolerance);
            Assert.AreEqual(a.healSize.x, b.healSize.x, Tolerance);
            Assert.AreEqual(a.healSize.y, b.healSize.y, Tolerance);
        }

        [Test] public void TheOutlineHasItsMirrorImagesToo()
        {
            DominionLaneLayout layout = Layout();
            foreach (Vector2 corner in layout.Outline)
            {
                Assert.IsTrue(HasPoint(layout.Outline, LaneGeometry.FlipX(corner)), $"outline corner {corner} has no mirror across the short axis");
                Assert.IsTrue(HasPoint(layout.Outline, LaneGeometry.FlipZ(corner)), $"outline corner {corner} has no mirror across the long axis");
            }
        }

        // ---------------------------------------------------------------- the spawns

        [Test] public void TheTwoSpawnPointsAreTheBoardsFiftySixMetresApart()
        {
            DominionLaneLayout layout = Layout();
            float distance = Vector2.Distance(layout.Spawns[0].spawnPoint, layout.Spawns[1].spawnPoint);
            Assert.AreEqual(layout.SpawnToSpawnMetres, distance, 0.5f);
        }

        [Test] public void EachTeamHasASpawnPointAHealingGroundAndATowerInTheMiddleOfItsPocket()
        {
            DominionLaneLayout layout = Layout();
            foreach (int team in new[] { 0, 1 })
            {
                Assert.IsTrue(layout.TryGetSpawn(team, out LaneSpawn spawn), $"team {team} has no spawn row");
                Assert.Greater(spawn.healSize.x, 1f, $"team {team}'s healing ground has no width");
                Assert.Greater(spawn.healSize.y, 1f, $"team {team}'s healing ground has no depth");
                float half = 0.5f;
                Assert.IsTrue(Mathf.Abs(spawn.spawnPoint.x - spawn.healCentre.x) <= spawn.healSize.x * half && Mathf.Abs(spawn.spawnPoint.y - spawn.healCentre.y) <= spawn.healSize.y * half,
                    $"team {team}'s players appear on their own healing ground");
                Assert.IsTrue(Mathf.Abs(spawn.towerCentre.x - spawn.healCentre.x) <= spawn.healSize.x * half && Mathf.Abs(spawn.towerCentre.y - spawn.healCentre.y) <= spawn.healSize.y * half,
                    $"team {team}'s tower stands on its healing ground");
            }
            Assert.AreNotEqual(layout.Spawns[0].team, layout.Spawns[1].team);
        }

        [Test] public void TheHealingGroundsOfTheTwoTeamsNeverOverlap()
        {
            DominionLaneLayout layout = Layout();
            LaneRect a = new LaneRect("", layout.Spawns[0].healCentre, layout.Spawns[0].healSize);
            LaneRect b = new LaneRect("", layout.Spawns[1].healCentre, layout.Spawns[1].healSize);
            Assert.Greater(LaneGeometry.Gap(a, b), 0f);
        }

        // ---------------------------------------------------------------- walking gaps

        [Test] public void EveryGapBetweenTwoBoxesIsAtLeastTheBoardsMinimum()
        {
            DominionLaneLayout layout = Layout();
            for (int i = 0; i < layout.Boxes.Count; i++)
                for (int j = i + 1; j < layout.Boxes.Count; j++)
                    Assert.GreaterOrEqual(LaneGeometry.Gap(layout.BoxRect(i), layout.BoxRect(j)), layout.MinimumGapMetres - 1e-3f, $"boxes {i + 1} and {j + 1}");
        }

        [Test] public void EveryGapBetweenABoxAndAWallIsAtLeastTheBoardsMinimum()
        {
            DominionLaneLayout layout = Layout();
            for (int i = 0; i < layout.Boxes.Count; i++)
                foreach (LaneRect wall in layout.Walls)
                    Assert.GreaterOrEqual(LaneGeometry.Gap(layout.BoxRect(i), wall), layout.MinimumGapMetres - 1e-3f, $"box {i + 1} and {wall.name}");
        }

        [Test] public void NoBoxTouchesABarrierOrHidesInsideAWall()
        {
            DominionLaneLayout layout = Layout();
            for (int i = 0; i < layout.Boxes.Count; i++)
                foreach (LaneRect barrier in layout.Barriers)
                    Assert.Greater(LaneGeometry.Gap(layout.BoxRect(i), barrier), 0f);
        }

        [Test] public void EachPocketHoldsARowOfThreeBoxesAndARowOfTwoInFrontOfItsTower()
        {
            DominionLaneLayout layout = Layout();
            foreach (LaneSpawn spawn in layout.Spawns)
            {
                var columns = new SortedDictionary<float, int>();
                foreach (Vector2 box in layout.Boxes)
                {
                    if (Mathf.Sign(box.x) != Mathf.Sign(spawn.towerCentre.x)) continue;
                    float key = Mathf.Round(box.x * 10f) / 10f;
                    columns[key] = columns.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                var counts = new List<int>(columns.Values);
                counts.Sort();
                CollectionAssert.AreEqual(new[] { 2, 3 }, counts, $"the pocket of team {spawn.team} has a row of 3 and a row of 2");
            }
        }

        // ---------------------------------------------------------------- the zones

        [Test] public void BothZonesAreTheTierThreeSizeOfTheTerritoryConfig()
        {
            DominionLaneLayout layout = Layout();
            var territory = AssetDatabase.LoadAssetAtPath<TerritoryConfig>("Assets/Gameplay/Config/TerritoryConfig.asset");
            Assert.IsNotNull(territory);
            Assert.AreEqual(3, layout.ZoneTier);
            float radius = territory.ForTier(layout.ZoneTier).captureRadius;
            Assert.AreEqual(radius, layout.ZoneDrawnRadiusMetres, 0.25f, "the board's circle is the Tier 3 circle");
        }

        [Test] public void EveryZoneCircleIsInsideThePlayableOutlineAndClearOfTheH()
        {
            DominionLaneLayout layout = Layout();
            var territory = AssetDatabase.LoadAssetAtPath<TerritoryConfig>("Assets/Gameplay/Config/TerritoryConfig.asset");
            float radius = territory.ForTier(layout.ZoneTier).captureRadius;
            var bounds = Overpower.Arena.ArenaBounds.FromPolygon(layout.Outline);
            Assert.IsNotNull(bounds);
            foreach (Vector2 zone in layout.ZoneCentres)
            {
                Assert.GreaterOrEqual(bounds.SignedDistance(zone), radius - 0.2f, $"zone at {zone} fits inside the playable ground");
                foreach (LaneRect wall in layout.Walls)
                    if (wall.name.StartsWith("H "))
                        Assert.Greater(LaneGeometry.Gap(new LaneRect("", zone, Vector2.one * 0.01f), wall), radius * 0.5f, $"the zone's middle is clear of {wall.name}");
            }
        }

        [Test] public void ThePlusIsAlsoTheOnlyThingJoiningTheTwoArmsOfTheH()
        {
            // "Nobody walks straight between the zones": the H's arms and the plus form one unbroken line across the middle of the hall.
            DominionLaneLayout layout = Layout();
            float left = float.MaxValue, right = float.MinValue;
            foreach (LaneRect wall in layout.Walls)
                if (wall.name.StartsWith("H Arm")) { left = Mathf.Min(left, wall.Min.x); right = Mathf.Max(right, wall.Max.x); }
            float barrierLeft = float.MaxValue, barrierRight = float.MinValue;
            foreach (LaneRect barrier in layout.Barriers)
                if (Mathf.Abs(barrier.centre.y) < Tolerance && barrier.size.x > barrier.size.y) { barrierLeft = Mathf.Min(barrierLeft, barrier.Min.x); barrierRight = Mathf.Max(barrierRight, barrier.Max.x); }
            foreach (LaneRect wall in layout.Walls)
                if (wall.name.StartsWith("H Arm"))
                {
                    float gapToPlus = LaneGeometry.Gap(wall, new LaneRect("", new Vector2((barrierLeft + barrierRight) * 0.5f, 0f), new Vector2(barrierRight - barrierLeft, 0.01f)));
                    Assert.AreEqual(0f, gapToPlus, Tolerance, $"{wall.name} meets the plus");
                }
            Assert.Less(left, barrierLeft);
            Assert.Greater(right, barrierRight);
        }

        // ---------------------------------------------------------------- the playable ground

        [Test] public void EveryWallAndBoxIsWithinTheFloor()
        {
            DominionLaneLayout layout = Layout();
            Vector2 half = layout.FloorSize * 0.5f;
            foreach (LaneRect wall in layout.Walls)
                Assert.IsTrue(wall.Max.x <= half.x && wall.Min.x >= -half.x && wall.Max.y <= half.y && wall.Min.y >= -half.y, wall.name);
            for (int i = 0; i < layout.Boxes.Count; i++)
            {
                LaneRect box = layout.BoxRect(i);
                Assert.IsTrue(box.Max.x <= half.x && box.Min.x >= -half.x && box.Max.y <= half.y && box.Min.y >= -half.y, box.name);
            }
        }

        [Test] public void TheAnchorsAreTheBoardsOwnNumbersSoAMirroredPixelTypoIsCaught()
        {
            // Read straight off the board (12 px to the metre, middle (550, 342)): the pocket end walls' inner faces at x = (87 - 550) / 12, the spawn towers
            // at x = (178 - 550) / 12, the hall's top wall's inner face at z = -(155 - 342) / 12. A pixel typed on the wrong side of the middle moves these.
            DominionLaneLayout layout = Layout();
            float maxX = 0f, maxZ = 0f;
            foreach (Vector2 p in layout.Outline) { maxX = Mathf.Max(maxX, Mathf.Abs(p.x)); maxZ = Mathf.Max(maxZ, Mathf.Abs(p.y)); }
            Assert.AreEqual(38.58f, maxX, Tolerance, "pocket inner faces");
            Assert.AreEqual(15.58f, maxZ, Tolerance, "hall top inner face (the pocket walls' inner faces are at 10.08)");
            foreach (LaneSpawn spawn in layout.Spawns) Assert.AreEqual(31f, Mathf.Abs(spawn.towerCentre.x), Tolerance, "spawn tower of team " + spawn.team);
            Assert.AreEqual(-31f, layout.Spawns.First(s => s.team == 1).towerCentre.x, Tolerance, "Purple is the left pocket");
            Assert.AreEqual(31f, layout.Spawns.First(s => s.team == 0).towerCentre.x, Tolerance, "White is the right pocket");
        }

        [Test] public void TheBuildersMeasuresLiveInTheLayoutNotInTheBuilder()
        {
            DominionLaneLayout layout = Layout();
            Assert.Greater(layout.BarrierHeightMetres, 0f);
            Assert.Greater(layout.UnusedUnderAttackOffsetMetres, 0f);
            Assert.GreaterOrEqual(layout.MinimapPixels, 256);
            Assert.IsFalse(string.IsNullOrEmpty(layout.WallLayerName));
            Assert.AreNotEqual(-1, LayerMask.NameToLayer(layout.WallLayerName), "the layer exists");
        }

        [Test] public void TheBuildersPlanGivesTheZonesTheLayoutsTierAndTheSpawnTowersTierOne()
        {
            var layout = ScriptableObject.CreateInstance<DominionLaneLayout>();
            try
            {
                typeof(DominionLaneLayout).GetField("zoneTier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(layout, 2);
                var plan = Overpower.EditorTools.DominionLaneBuilder.PlanFor(layout);
                Assert.AreEqual(2, plan[Overpower.EditorTools.DominionLaneBuilder.TopZone].tier, "a different tier in the asset reaches the zones");
                Assert.AreEqual(2, plan[Overpower.EditorTools.DominionLaneBuilder.BottomZone].tier);
                Assert.AreEqual(1, plan[Overpower.EditorTools.DominionLaneBuilder.WhiteSpawnZone].tier, "a spawn tower is a capital whatever the zones are");
                Assert.AreEqual(1, plan[Overpower.EditorTools.DominionLaneBuilder.PurpleSpawnZone].tier);
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test] public void EverySpawnAndBoxIsInsideThePlayableOutline()
        {
            DominionLaneLayout layout = Layout();
            var bounds = Overpower.Arena.ArenaBounds.FromPolygon(layout.Outline);
            foreach (LaneSpawn spawn in layout.Spawns)
            {
                Assert.Greater(bounds.SignedDistance(spawn.spawnPoint), 1f);
                Assert.Greater(bounds.SignedDistance(spawn.towerCentre), 1f);
            }
            foreach (Vector2 box in layout.Boxes) Assert.Greater(bounds.SignedDistance(box), layout.BoxSizeMetres * 0.5f);
        }
    }
}
