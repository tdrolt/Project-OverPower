using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.UI;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.EditorTools
{
    /// <summary>
    /// Dominion Task 11: builds the 2v2 lane map into the open Dominion 2v2 scene from DominionLaneLayout.asset, replacing Task 10's stand-in triangle arena.
    /// It uses the triangle arena's own pieces (the boundary wall, block and jersey-barrier boxes, their materials and layers), the scene's four existing
    /// towers (two Tier 3 zones, two Tier 1 spawn towers - the others are removed), and sets the per-scene map data: the capital list, the tower
    /// links, the spawn points, the healing areas, the arena outline and the lane's own minimap picture.
    ///
    /// Everything it makes sits under ONE object it owns ("Dominion Lane" under Arena), which a re-run destroys and builds again; the towers, spawn
    /// points and the scene's data are updated in place. It only ever edits a scene called Dominion 2v2 (never Game Scene), never in Play Mode, and never
    /// saves: the caller looks at the report, then saves.
    /// Zone numbering: 0 = the top zone, 1 = the bottom zone, 2 = White's spawn tower, 3 = Purple's spawn tower (contiguous from 0, so the zone count is 4).
    /// Links: each spawn tower links to both zones and the two zones link to each other, so either team can take either zone from the start
    /// (a capital counts as held for "capture next to a zone you hold").
    /// </summary>
    public static class DominionLaneBuilder
    {
        public const string SceneName = "Dominion 2v2";
        public const string RootName = "Dominion Lane";
        public const string HealGroupName = "Spawn Heal Areas";
        public const string LayoutPath = DominionLaneLayoutFactory.AssetPath;
        public const string MinimapImagePath = "Assets/Gameplay/UI/DominionLaneMinimap.png";
        public const string MinimapConfigPath = "Assets/Gameplay/Config/DominionLaneMinimapConfig.asset";

        public const int TopZone = 0, BottomZone = 1, WhiteSpawnZone = 2, PurpleSpawnZone = 3;

        [MenuItem("OverPower/Build Dominion Lane Map")]
        private static void BuildFromMenu()
        {
            List<string> report = Build(SceneManager.GetActiveScene());
            Debug.Log("[DominionLane] Build:\n" + string.Join("\n", report));
        }

        [MenuItem("OverPower/Measure Dominion Lane Gaps")]
        private static void MeasureFromMenu()
        {
            List<string> report = MeasureGaps(SceneManager.GetActiveScene(), out _);
            Debug.Log("[DominionLane] Gaps:\n" + string.Join("\n", report));
        }

        private static Vector3 World(ArenaSymmetry arena, Vector2 lane) => new Vector3(arena.centre.x + lane.x, 0f, arena.centre.z + lane.y);

        public static List<string> Build(Scene scene)
        {
            var report = new List<string>();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { report.Add("PROBLEM: not in Play Mode."); return report; }
            if (scene.name != SceneName) { report.Add($"PROBLEM: the open scene is '{scene.name}', not '{SceneName}' - the lane is never built into any other scene."); return report; }

            var layout = AssetDatabase.LoadAssetAtPath<DominionLaneLayout>(LayoutPath);
            var arenaLayout = AssetDatabase.LoadAssetAtPath<ArenaLayout>("Assets/Gameplay/Config/ArenaLayout.asset");
            if (layout == null || arenaLayout == null) { report.Add("PROBLEM: the lane layout or the arena layout asset is missing."); return report; }

            ArenaSymmetry[] arenas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArenaSymmetry>(true)).ToArray();
            if (arenas.Length != 1) { report.Add($"PROBLEM: {arenas.Length} ArenaSymmetry components in the scene (need exactly one)."); return report; }
            ArenaSymmetry arena = arenas[0];
            Transform environment = arena.transform.parent;

            // ---- 1. out with the stand-in, in with the lane's one root
            Transform oldRoot = arena.transform.Find(RootName);
            if (oldRoot != null) Object.DestroyImmediate(oldRoot.gameObject);
            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                if (third == null || third.name == RootName) continue;
                foreach (Transform child in third)
                    if (child.GetComponent<ArenaBuiltGroup>() == null) { report.Add($"PROBLEM: '{child.name}' under '{third.name}' is not the builder's own - nothing was changed."); return report; }
            }
            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
                if (third != null && third.name != RootName) Object.DestroyImmediate(third.gameObject);

            var rootGo = new GameObject(RootName);
            rootGo.transform.SetParent(arena.transform, false);
            rootGo.AddComponent<ArenaBuiltGroup>();
            Transform boundry = ArenaPrimitiveBuilder.NewMarkedGroup(rootGo.transform, ArenaSymmetry.BoundaryGroupName);
            Transform blocks = ArenaPrimitiveBuilder.NewMarkedGroup(rootGo.transform, ArenaSymmetry.BlocksGroupName);
            Transform barriers = ArenaPrimitiveBuilder.NewMarkedGroup(rootGo.transform, ArenaSymmetry.BarriersGroupName);
            Transform heals = ArenaPrimitiveBuilder.NewMarkedGroup(rootGo.transform, HealGroupName);

            arena.source = rootGo.transform;
            arena.generated120 = null;
            arena.generated240 = null;
            arena.snappedTriplets.Clear();
            arena.centred.Clear();
            arena.layout = arenaLayout;
            arena.outlineIsWholeArena = true;
            arena.sourceOutline = layout.Outline.Select(p => new Vector2(arena.centre.x + p.x, arena.centre.z + p.y)).ToList();

            // ---- 2. walls (the outer walls are boundary walls and block a portal's path; the H is in the blocks group), boxes, the plus
            float wallCentreY = (arenaLayout.WallBottomY + arenaLayout.WallTopY) * 0.5f;
            float wallHeight = arenaLayout.WallTopY - arenaLayout.WallBottomY;
            foreach (LaneRect wall in layout.Walls)
            {
                // The H stands in the Blocks group (a Portal crosses it, like the triangle's middle wall); the outer walls stay boundary walls.
                GameObject go = ArenaPrimitiveBuilder.NewPrimitiveChild(DominionLaneLayout.IsBlockWall(wall.name) ? blocks : boundry, wall.name, arenaLayout.WallMaterial, layout.WallLayerName);
                Vector3 at = World(arena, wall.centre);
                go.transform.SetPositionAndRotation(new Vector3(at.x, wallCentreY, at.z), Quaternion.identity);
                go.transform.localScale = new Vector3(wall.size.x, wallHeight, wall.size.y);
            }
            for (int i = 0; i < layout.Boxes.Count; i++)
            {
                GameObject go = ArenaPrimitiveBuilder.NewPrimitiveChild(blocks, "Box " + (i + 1), arenaLayout.BlockMaterial, layout.WallLayerName);
                Vector3 at = World(arena, layout.Boxes[i]);
                go.transform.SetPositionAndRotation(new Vector3(at.x, layout.BoxHeightMetres * 0.5f, at.z), Quaternion.identity);
                go.transform.localScale = new Vector3(layout.BoxSizeMetres, layout.BoxHeightMetres, layout.BoxSizeMetres);
            }
            foreach (LaneRect barrier in layout.Barriers)
            {
                GameObject go = ArenaPrimitiveBuilder.NewPrimitiveChild(barriers, barrier.name, arenaLayout.BarrierMaterial, ArenaLayers.BarrierLayerName);
                Vector3 at = World(arena, barrier.centre);
                float lookHeight = layout.BarrierHeightMetres; // waist high, as the triangle's barriers (set in the layout asset)
                var position = new Vector3(at.x, lookHeight * 0.5f, at.z);
                go.transform.SetPositionAndRotation(position, Quaternion.identity);
                go.transform.localScale = new Vector3(barrier.size.x, lookHeight, barrier.size.y);
                ArenaPieceShapes.BarrierBlockingBox(arenaLayout.BarrierBlockingBottomY, arenaLayout.BarrierBlockingTopY, position.y, lookHeight, out Vector3 c, out Vector3 s);
                BoxCollider box = go.GetComponent<BoxCollider>();
                box.center = c; box.size = s;
            }
            report.Add($"Walls {layout.Walls.Count}, boxes {layout.Boxes.Count}, barriers {layout.Barriers.Count} built under '{RootName}'.");

            // ---- 3. floor
            ArenaPrimitiveBuilder.BuildFloor(environment, arenaLayout, arena.centre);
            Transform floor = environment.Find(ArenaPrimitiveBuilder.FloorObjectName);
            floor.localScale = new Vector3(layout.FloorSize.x, arenaLayout.FloorThickness, layout.FloorSize.y);
            report.Add($"Floor {layout.FloorSize.x} x {layout.FloorSize.y} m.");

            // ---- 4. towers: keep four, number them, put them where the drawing has them, remove the rest
            List<string> towerReport = PlaceTowers(scene, arena, layout);
            report.AddRange(towerReport);
            if (towerReport.Any(l => l.StartsWith("PROBLEM"))) return report;

            // ---- 5. spawn points, healing areas, per-scene data
            report.AddRange(PlaceSpawns(scene, arena, layout, heals));
            report.AddRange(WriteBuildingData(scene));

            // ---- 6. the minimap picture
            report.Add(BakeMinimap(scene, arena, layout, rootGo.transform));

            // ---- 6b. the straight camera (A41): own spawn on the left of the screen
            report.Add(EnsureCameraConfig(scene));

            // ---- 7. checks
            report.AddRange(CheckSceneViewIds(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            return report;
        }

        // ------------------------------------------------------------------------------------------------ towers

        private const int SpawnTowerTier = 1; // a capital (the respawn zone that cannot be captured)

        // The two zones' tier comes from the layout asset (ZoneTier), the spawn towers are capitals.
        public static (int zone, string[] names, int tier)[] PlanFor(DominionLaneLayout layout) => new[]
        {
            (TopZone, new[] { "Zone Top", "House_05 (12)" }, layout.ZoneTier),
            (BottomZone, new[] { "Zone Bottom", "House_05 (18)" }, layout.ZoneTier),
            (WhiteSpawnZone, new[] { "Spawn Tower White", "Cathedral_Team0" }, SpawnTowerTier),
            (PurpleSpawnZone, new[] { "Spawn Tower Purple", "Cathedral_Team1" }, SpawnTowerTier),
        };
        private static readonly string[] NewNames = { "Zone Top", "Zone Bottom", "Spawn Tower White", "Spawn Tower Purple" };

        private static List<string> PlaceTowers(Scene scene, ArenaSymmetry arena, DominionLaneLayout layout)
        {
            var report = new List<string>();
            var plan = PlanFor(layout);
            BuildingCapture[] all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BuildingCapture>(true)).ToArray();
            var keep = new BuildingCapture[4];
            for (int i = 0; i < plan.Length; i++)
            {
                keep[i] = all.FirstOrDefault(t => plan[i].names.Contains(t.name));
                if (keep[i] == null) { report.Add($"PROBLEM: no tower called {string.Join(" / ", plan[i].names)}."); return report; }
            }

            // Remove the others, and their flag carpets.
            foreach (BuildingCapture tower in all)
            {
                if (keep.Contains(tower)) continue;
                if (tower.flagRenderer != null && !keep.Any(k => k.flagRenderer == tower.flagRenderer)) Object.DestroyImmediate(tower.flagRenderer.gameObject);
                Object.DestroyImmediate(tower.gameObject);
            }

            Vector2[] centres =
            {
                layout.ZoneCentres.OrderByDescending(z => z.y).First(),   // top
                layout.ZoneCentres.OrderBy(z => z.y).First(),             // bottom
                layout.Spawns.First(s => s.team == 0).towerCentre,
                layout.Spawns.First(s => s.team == 1).towerCentre,
            };
            for (int i = 0; i < keep.Length; i++)
            {
                BuildingCapture tower = keep[i];
                Vector3 carpetOffset = tower.flagRenderer != null ? tower.flagRenderer.transform.position - tower.transform.position : Vector3.zero;
                Vector3 target = World(arena, centres[i]);
                tower.transform.position = new Vector3(target.x, tower.transform.position.y, target.z);
                tower.gameObject.name = NewNames[i];
                tower.buildingID = plan[i].zone;
                tower.tier = plan[i].tier;
                if (tower.flagRenderer != null) tower.flagRenderer.transform.position = tower.transform.position + carpetOffset;
                ScaleBody(tower, i < 2 ? layout.ZoneTowerSizeMetres : layout.SpawnTowerSizeMetres, report);
                RecordIfPrefab(tower.transform); RecordIfPrefab(tower.gameObject); RecordIfPrefab(tower);
                if (tower.flagRenderer != null) RecordIfPrefab(tower.flagRenderer.transform);
                report.Add($"zone {plan[i].zone} = {NewNames[i]} tier {plan[i].tier} at lane {centres[i]}");
            }
            return report;
        }

        /// <summary>
        /// The tower's body ("Tower Look": the plinth, drum, crown and columns, with the capsule that is its cover) is the triangle's, 5.2 m across; the lane's board
        /// draws a 2 m zone tower and a 3.8 m spawn tower. Scales the body sideways (never its height) so its capsule is <paramref name="widthMetres"/> across.
        /// Re-running changes nothing once the width is right. The capture trigger and ring are not part of the body and keep the Territory Config's radius.
        /// </summary>
        private static void ScaleBody(BuildingCapture tower, float widthMetres, List<string> report)
        {
            Transform look = tower.transform.Find("Tower Look");
            CapsuleCollider capsule = look != null ? look.GetComponent<CapsuleCollider>() : null;
            if (capsule == null) { report.Add($"PROBLEM: {tower.name} has no 'Tower Look' capsule to size."); return; }
            float sideways = Mathf.Max(look.lossyScale.x, look.lossyScale.z);
            float widthNow = capsule.radius * 2f * sideways;
            float factor = widthMetres / widthNow;
            look.localScale = new Vector3(look.localScale.x * factor, look.localScale.y, look.localScale.z * factor);
            RecordIfPrefab(look);
            report.Add($"{tower.name}: body {widthNow:0.00} m -> {capsule.radius * 2f * Mathf.Max(look.lossyScale.x, look.lossyScale.z):0.00} m across");
        }

        private static void RecordIfPrefab(Object target)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        // ------------------------------------------------------------------------------------------------ spawns

        private static List<string> PlaceSpawns(Scene scene, ArenaSymmetry arena, DominionLaneLayout layout, Transform heals)
        {
            var report = new List<string>();
            RoomManager rooms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RoomManager>(true)).FirstOrDefault();
            if (rooms == null) { report.Add("PROBLEM: no RoomManager."); return report; }
            var so = new SerializedObject(rooms);
            SerializedProperty team = so.FindProperty("teamSpawnPoints");
            SerializedProperty under = so.FindProperty("capitalUnderAttackSpawnPoints");
            if (team == null || under == null) { report.Add("PROBLEM: RoomManager spawn point arrays not found."); return report; }

            var keepers = new HashSet<Transform>();
            for (int index = 0; index < 2; index++)
            {
                if (!layout.TryGetSpawn(index, out LaneSpawn spawn)) { report.Add($"PROBLEM: the layout has no spawn for team {index}."); return report; }
                var normal = team.GetArrayElementAtIndex(index).objectReferenceValue as Transform;
                var attacked = under.GetArrayElementAtIndex(index).objectReferenceValue as Transform;
                if (normal == null || attacked == null) { report.Add($"PROBLEM: RoomManager has no spawn point object for team {index}."); return report; }
                // The team looks towards the middle of the map.
                float yaw = spawn.spawnPoint.x < 0f ? 90f : 270f;
                string colour = index == 0 ? "White" : "Purple";
                normal.SetPositionAndRotation(World(arena, spawn.spawnPoint), Quaternion.Euler(0f, yaw, 0f));
                normal.gameObject.name = "Spawn " + colour;
                // Two points, to either side of the spawn point: the team's two players take one each (RoomManager.SpawnPointFor).
                for (int c = normal.childCount - 1; c >= 0; c--) Object.DestroyImmediate(normal.GetChild(c).gameObject);
                for (int side = 0; side < 2; side++)
                {
                    var slot = new GameObject($"Spawn {colour} {(side == 0 ? "A" : "B")}");
                    slot.transform.SetParent(normal, false);
                    slot.transform.SetPositionAndRotation(World(arena, spawn.spawnPoint + new Vector2(0f, side == 0 ? -layout.SpawnSideOffsetMetres : layout.SpawnSideOffsetMetres)),
                                                          Quaternion.Euler(0f, yaw, 0f));
                }
                // Dominion never uses the "capital under attack" spawn; it stands next to the normal one so the array stays whole.
                Vector2 beside = spawn.spawnPoint + new Vector2(0f, layout.UnusedUnderAttackOffsetMetres);
                attacked.SetPositionAndRotation(World(arena, beside), Quaternion.Euler(0f, yaw, 0f));
                attacked.gameObject.name = "Spawn " + colour + " (unused under attack)";
                keepers.Add(normal); keepers.Add(attacked);

                var healGo = new GameObject("Spawn Heal " + colour);
                healGo.transform.SetParent(heals, false);
                healGo.transform.position = World(arena, spawn.healCentre);
                healGo.AddComponent<SpawnHealArea>().Configure(index, SpawnHealArea.AreaShape.Box, spawn.healSize, 1f);
                report.Add($"team {index} ({colour}): spawn point {spawn.spawnPoint}, heal area {spawn.healSize.x:0.##} x {spawn.healSize.y:0.##} m at {spawn.healCentre}");
            }
            // Drop the third team's points.
            for (int i = team.arraySize - 1; i >= 2; i--)
            {
                var t = team.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (t != null && !keepers.Contains(t)) Object.DestroyImmediate(t.gameObject);
            }
            for (int i = under.arraySize - 1; i >= 2; i--)
            {
                var t = under.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (t != null && !keepers.Contains(t)) Object.DestroyImmediate(t.gameObject);
            }
            team.arraySize = 2;
            under.arraySize = 2;
            so.ApplyModifiedProperties();
            return report;
        }

        // ------------------------------------------------------------------------------------------------ BuildingManager data

        private static List<string> WriteBuildingData(Scene scene)
        {
            var report = new List<string>();
            BuildingManager manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BuildingManager>(true)).FirstOrDefault();
            if (manager == null) { report.Add("PROBLEM: no BuildingManager."); return report; }
            var so = new SerializedObject(manager);

            int[][] adjacents =
            {
                new[] { BottomZone, WhiteSpawnZone, PurpleSpawnZone },   // top zone
                new[] { TopZone, WhiteSpawnZone, PurpleSpawnZone },      // bottom zone
                new[] { TopZone, BottomZone },                           // White's spawn
                new[] { TopZone, BottomZone },                           // Purple's spawn
            };
            SerializedProperty list = so.FindProperty("TowerDictionary.dictionaryList");
            list.arraySize = adjacents.Length;
            for (int i = 0; i < adjacents.Length; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Key").intValue = i;
                e.FindPropertyRelative("index").intValue = i;
                e.FindPropertyRelative("isKeyDuplicated").boolValue = false;
                e.FindPropertyRelative("Value.Building").objectReferenceValue = null;
                e.FindPropertyRelative("Value.isCaptured").boolValue = false;
                e.FindPropertyRelative("Value.controllingTeam").intValue = -1;
                SerializedProperty adj = e.FindPropertyRelative("Value.Adjacents");
                adj.arraySize = adjacents[i].Length;
                for (int k = 0; k < adjacents[i].Length; k++) adj.GetArrayElementAtIndex(k).intValue = adjacents[i][k];
            }

            SerializedProperty capitals = so.FindProperty("capitalZones");
            capitals.arraySize = 2;
            capitals.GetArrayElementAtIndex(0).FindPropertyRelative("zone").intValue = WhiteSpawnZone;
            capitals.GetArrayElementAtIndex(0).FindPropertyRelative("team").intValue = 0;
            capitals.GetArrayElementAtIndex(1).FindPropertyRelative("zone").intValue = PurpleSpawnZone;
            capitals.GetArrayElementAtIndex(1).FindPropertyRelative("team").intValue = 1;
            so.ApplyModifiedProperties();
            report.Add("BuildingManager: 4 zones (0 top, 1 bottom, 2 White spawn, 3 Purple spawn), capitals 2 -> team 0 and 3 -> team 1, links: each spawn to both zones, the zones to each other.");
            return report;
        }

        // ------------------------------------------------------------------------------------------------ minimap

        private static string BakeMinimap(Scene scene, ArenaSymmetry arena, DominionLaneLayout layout, Transform root)
        {
            // The lane is long and narrow, so its minimap is a rectangle (MinimapConfig.RectangularFrame): the picture covers the walls' bounding box plus the margin.
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds b = renderer.bounds;
                min = Vector2.Min(min, new Vector2(b.min.x, b.min.z));
                max = Vector2.Max(max, new Vector2(b.max.x, b.max.z));
                any = true;
            }
            if (!any) return "not baked: no meshes.";
            float margin = layout.MinimapMarginMetres;
            float width = max.x - min.x + 2f * margin;
            float depth = max.y - min.y + 2f * margin;
            var centre = (min + max) * 0.5f;
            // The picture's size is the minimap config's own Image Pixels (the triangle arena's baker reads the same field), not a number kept here.
            var config = AssetDatabase.LoadAssetAtPath<MinimapConfig>(MinimapConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MinimapConfig>();
                AssetDatabase.CreateAsset(config, MinimapConfigPath);
            }
            int pixels = Mathf.Clamp(config.ImagePixels, 256, 2048);

            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, MinimapImagePath);
            File.WriteAllBytes(full, TopDownRender.RenderPng(centre, width, depth, pixels));
            AssetDatabase.ImportAsset(MinimapImagePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(MinimapImagePath);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = pixels;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var so = new SerializedObject(config);
            so.FindProperty("arenaImage").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(MinimapImagePath);
            so.FindProperty("worldCentre").vector2Value = centre;
            so.FindProperty("worldSizeMetres").floatValue = width;
            so.FindProperty("worldDepthMetres").floatValue = depth;
            so.FindProperty("rectangularFrame").boolValue = true;
            so.FindProperty("imagePixels").intValue = pixels;
            so.FindProperty("marginMetres").floatValue = layout.MinimapMarginMetres;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);

            // The scene's own pointer to it.
            SceneMinimapConfig holder = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SceneMinimapConfig>(true)).FirstOrDefault();
            if (holder == null)
            {
                var go = new GameObject("Lane Minimap Config");
                SceneManager.MoveGameObjectToScene(go, scene);
                holder = go.AddComponent<SceneMinimapConfig>();
            }
            var holderSo = new SerializedObject(holder);
            holderSo.FindProperty("config").objectReferenceValue = config;
            holderSo.ApplyModifiedPropertiesWithoutUndo();
            return $"baked {MinimapImagePath} ({pixels} px across) covering {width:0.0} x {depth:0.0} m centred on ({centre.x:0.00}, {centre.y:0.00}); rectangular frame.";
        }

        /// <summary>Puts the scene's camera rule on the lane's config object (the lane plays on a straight view; see SceneCameraConfig). Safe to run again.</summary>
        public static string EnsureCameraConfig(Scene scene)
        {
            SceneMinimapConfig holder = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SceneMinimapConfig>(true)).FirstOrDefault();
            if (holder == null) return "PROBLEM: no scene config object for the camera rule.";
            SceneCameraConfig camera = holder.GetComponent<SceneCameraConfig>();
            if (camera == null) camera = holder.gameObject.AddComponent<SceneCameraConfig>();
            var so = new SerializedObject(camera);
            so.FindProperty("ownSpawnOnLeft").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            return "camera rule: own spawn on the left of the screen (straight lane view).";
        }

        // ------------------------------------------------------------------------------------------------ checks

        /// <summary>Every PhotonView in the scene (the shared prefab's included) with its scene view id; lists duplicates and zeros.</summary>
        public static List<string> CheckSceneViewIds(Scene scene)
        {
            var report = new List<string>();
            var seen = new Dictionary<int, string>();
            foreach (PhotonView view in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PhotonView>(true)))
            {
                int id = view.sceneViewId;
                if (id == 0) { report.Add($"PROBLEM: {view.name} has sceneViewId 0."); continue; }
                if (seen.TryGetValue(id, out string other)) report.Add($"PROBLEM: sceneViewId {id} is used by both {other} and {view.name}.");
                else seen[id] = view.name;
            }
            report.Add($"sceneViewIds: {seen.Count} views, ids {string.Join(",", seen.Keys.OrderBy(k => k))}.");
            return report;
        }

        /// <summary>
        /// Measures, in the built scene, the gaps between the colliders of the boxes, the walls, the barriers and the towers' bodies (every collider here is an
        /// upright box or capsule, so the plan-view gap between their bounds is exact for boxes and a safe low estimate for a capsule). Returns the report;
        /// smallestBoxGap is the smallest gap that involves a box (the board promises 2.75 m or more).
        /// </summary>
        public static List<string> MeasureGaps(Scene scene, out float smallestBoxGap)
        {
            var report = new List<string>();
            smallestBoxGap = float.MaxValue;
            Transform root = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == RootName);
            if (root == null) { report.Add("PROBLEM: no Dominion Lane in the scene."); return report; }

            Physics.SyncTransforms(); // newly built colliders report empty bounds until the physics scene has seen them
            var items = new List<(string name, Bounds bounds, bool isBox)>();
            foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
                if (c.enabled && !c.isTrigger)
                    items.Add((c.name, c.bounds, c.name.StartsWith("Box ")));
            foreach (BuildingCapture tower in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BuildingCapture>(false)))
                foreach (Collider c in tower.GetComponentsInChildren<Collider>(false))
                    if (c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy)
                        items.Add((tower.name + "/" + c.name, c.bounds, false));

            string worst = "";
            float worstWallGap = float.MaxValue; string worstWall = "";
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    float gap = LaneGeometry.Gap(new Vector2(items[i].bounds.min.x, items[i].bounds.min.z), new Vector2(items[i].bounds.max.x, items[i].bounds.max.z),
                                                 new Vector2(items[j].bounds.min.x, items[j].bounds.min.z), new Vector2(items[j].bounds.max.x, items[j].bounds.max.z));
                    if (gap <= 0.0001f) continue; // touching: joined pieces (an arm meets its wall), not a gap to walk through
                    bool involvesBox = items[i].isBox || items[j].isBox;
                    if (involvesBox && gap < smallestBoxGap) { smallestBoxGap = gap; worst = items[i].name + " <-> " + items[j].name; }
                    if (!involvesBox && gap < worstWallGap) { worstWallGap = gap; worstWall = items[i].name + " <-> " + items[j].name; }
                }
            report.Add($"colliders measured: {items.Count}");
            report.Add($"smallest gap involving a box: {smallestBoxGap:0.000} m ({worst})");
            report.Add($"smallest gap between walls/barriers/towers (not touching): {worstWallGap:0.000} m ({worstWall})");
            return report;
        }
    }
}
