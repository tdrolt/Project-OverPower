using System.Collections.Generic;
using System.Linq;
using Overpower.Arena;
using Overpower.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.EditorTools
{
    /// <summary>
    /// The arena rebuild's Editor tool. Step 3 built BuildTowerLooks (stamps Tower Look.prefab onto every
    /// BuildingCapture and hides that tower's old house and flag carpet - never removes them, base plan Decision 5).
    /// Step 4 added the rest as separate phases, not yet run on Game Scene: MoveOldArtAside (moves everything old
    /// under Source and the generated thirds into an inactive Old Arena (off), keeping world positions, base plan
    /// Decision 13), BuildSource (the boundary walls fresh from Arena Symmetry's Source Outline, plus every Block and
    /// Barrier row from ArenaLayout, base plan Decision 4 and Amendment 1's D3/D11/D24/D25), and BuildFloor (one flat
    /// slab). Step 5's BuildAll runs every phase in order, then Rebuild thirds and the minimap bake, so the menu
    /// applies the whole primitive arena in one call ("OverPower > Arena > Build primitive arena"). None of these
    /// ever run in Play Mode (a live, networked tower or a live boundary wall would change on this client only and
    /// desync the match), and none of them save the scene themselves - the caller looks at the report first, then
    /// saves.
    /// </summary>
    public static class ArenaPrimitiveBuilder
    {
        public const string TowerLookChildName = "Tower Look";
        public const string OldArtGroupName = "Old Arena (off)";
        public const string SceneryGroupName = "Scenery";
        public const string BoundryGroupName = ArenaSymmetry.BoundaryGroupName;
        public const string BlocksGroupName = "Blocks";
        public const string BarriersGroupName = "Barriers";
        public const string FloorObjectName = "Arena Floor";

        private const string PlayModeRefusal = "Can't build the arena in Play Mode: it would change live, networked " +
                                                "objects on this client only and desync the match. Stop Play Mode first.";

        public static List<string> BuildTowerLooks(Scene scene, GameObject towerLookPrefab)
        {
            var report = new List<string>();
            if (EditorApplication.isPlaying)
            {
                report.Add("Can't build tower looks in Play Mode: it would change a live, networked tower on this " +
                           "client only and desync the match. Stop Play Mode first.");
                return report;
            }
            if (towerLookPrefab == null)
            {
                report.Add("No Tower Look prefab given.");
                return report;
            }

            List<BuildingCapture> towers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BuildingCapture>(true))
                .OrderBy(t => t.buildingID)
                .ToList();

            foreach (BuildingCapture tower in towers)
                BuildOne(tower, towerLookPrefab, report);

            EditorSceneManager.MarkSceneDirty(scene);
            return report;
        }

        private static void BuildOne(BuildingCapture tower, GameObject towerLookPrefab, List<string> report)
        {
            Transform root = tower.transform;

            // Hide the house: disable its own MeshRenderer and every non-trigger collider directly on it, then
            // deactivate every old child (windows, doors...). Every change is recorded against the prefab instance,
            // or it can be silently lost the next time this instance is saved (CODING-STANDARDS #6).
            var ownRenderer = tower.GetComponent<MeshRenderer>();
            if (ownRenderer != null && ownRenderer.enabled)
            {
                ownRenderer.enabled = false;
                RecordIfPrefab(ownRenderer);
            }
            foreach (Collider c in tower.GetComponents<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                c.enabled = false;
                RecordIfPrefab(c);
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.name == TowerLookChildName)
                    continue; // handled below: destroyed and rebuilt fresh every run
                if (child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                    RecordIfPrefab(child.gameObject);
                }
            }

            // Hide the carpet (flag), never remove it: BuildingManager.ApplyOwnerVisual and the symmetry test both
            // still need flagRenderer assigned and its PhotonView untouched (Decision 10).
            if (tower.flagRenderer != null && tower.flagRenderer.enabled)
            {
                tower.flagRenderer.enabled = false;
                RecordIfPrefab(tower.flagRenderer);
            }

            // Replace any Tower Look from an earlier run, so this is safe to call twice.
            Transform existingLook = root.Find(TowerLookChildName);
            if (existingLook != null)
                Object.DestroyImmediate(existingLook.gameObject);

            var lookInstance = (GameObject)PrefabUtility.InstantiatePrefab(towerLookPrefab, root);
            lookInstance.name = TowerLookChildName;
            PrefabUtility.UnpackPrefabInstance(lookInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Vector3 towerScale = root.lossyScale;
            bool uniform = Mathf.Abs(towerScale.x - towerScale.y) < 0.001f && Mathf.Abs(towerScale.y - towerScale.z) < 0.001f;
            if (!uniform)
                report.Add($"PROBLEM: tower {tower.buildingID}'s lossyScale {towerScale:F4} is not uniform - " +
                           "Tower Look would not be authored in true metres.");

            lookInstance.transform.localPosition = Vector3.zero;
            lookInstance.transform.localRotation = Quaternion.identity;
            float inverseScale = 1f / towerScale.x;
            lookInstance.transform.localScale = new Vector3(inverseScale, inverseScale, inverseScale);

            TowerLook look = lookInstance.GetComponent<TowerLook>();
            int columns = TowerLookRules.ColumnsForTier(tower.tier);
            look.ApplyColumns(tower.tier);

            report.Add($"tower id={tower.buildingID} tier={tower.tier} columns={columns} scale={inverseScale:F4}");
        }

        private static void RecordIfPrefab(Object target)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        // ---- arena step 4: move the old art aside, build Source from the layout, lay the floor -------------------

        /// <summary>
        /// Moves everything old aside into an inactive "Old Arena (off)" under <paramref name="environment"/>,
        /// keeping every object's world position (base plan Decision 13). Never deletes anything. Idempotent: once
        /// an object has been moved, it is no longer a child of Source/a generated third/environment, so a second
        /// run finds nothing left to move there and reports nothing for it.
        /// </summary>
        public static List<string> MoveOldArtAside(ArenaSymmetry arena, Transform environment)
        {
            var report = new List<string>();
            if (EditorApplication.isPlaying) { report.Add(PlayModeRefusal); return report; }
            if (arena == null || arena.source == null || arena.generated120 == null || arena.generated240 == null)
            {
                report.Add("PROBLEM: ArenaSymmetry's Source and both generated thirds must all be assigned.");
                return report;
            }
            if (environment == null) { report.Add("PROBLEM: no Enviorment root given."); return report; }

            Transform oldArt = environment.Find(OldArtGroupName);
            if (oldArt == null)
            {
                var go = new GameObject(OldArtGroupName);
                Undo.RegisterCreatedObjectUndo(go, "Move old arena art aside");
                go.transform.SetParent(environment, false);
                oldArt = go.transform;
            }
            oldArt.gameObject.SetActive(false);

            MoveUnmarkedChildren(arena.source, GetOrCreateChild(oldArt, arena.source.name), report);
            MoveUnmarkedChildren(arena.generated120, GetOrCreateChild(oldArt, arena.generated120.name), report);
            MoveUnmarkedChildren(arena.generated240, GetOrCreateChild(oldArt, arena.generated240.name), report);

            Transform scenery = GetOrCreateChild(oldArt, SceneryGroupName);
            foreach (string name in new[] { "Mountains", "Props", "Nature" })
            {
                Transform t = environment.Find(name);
                if (t != null && t != oldArt)
                    MoveOneKeepingWorld(t, scenery, report);
            }
            for (int i = environment.childCount - 1; i >= 0; i--)
            {
                Transform child = environment.GetChild(i);
                if (child == oldArt || !child.name.StartsWith("Terrain"))
                    continue;
                MoveOneKeepingWorld(child, scenery, report);
            }

            return report;
        }

        private static void MoveUnmarkedChildren(Transform group, Transform destination, List<string> report)
        {
            for (int i = group.childCount - 1; i >= 0; i--)
            {
                Transform child = group.GetChild(i);
                if (child.GetComponent<ArenaBuiltGroup>() != null)
                    continue; // the builder's own group from an earlier run: leave it for BuildSource to replace.
                MoveOneKeepingWorld(child, destination, report);
            }
        }

        private static Transform GetOrCreateChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
                return existing;
            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void MoveOneKeepingWorld(Transform child, Transform destination, List<string> report)
        {
            child.SetParent(destination, true); // worldPositionStays: true
            report.Add($"moved '{child.name}' -> {destination.name}");
        }

        /// <summary>
        /// Rebuilds Source from <paramref name="layout"/>: the boundary walls fresh from
        /// <paramref name="arena"/>.sourceOutline (ArenaWallPlan - never captured, so "no gaps at the corners" can
        /// never go stale against a moved outline), then every Block and Barrier row from the layout, box for box
        /// (base plan Decision 3, Amendment 1 D24). Refuses outright while Source holds anything that isn't the
        /// builder's own (call MoveOldArtAside first) - the builder must never delete a hand-placed piece or old art
        /// that hasn't been moved aside yet (base plan Decision 4).
        /// </summary>
        public static List<string> BuildSource(ArenaSymmetry arena, ArenaLayout layout)
        {
            var report = new List<string>();
            if (EditorApplication.isPlaying) { report.Add(PlayModeRefusal); return report; }
            if (arena == null || arena.source == null)
            {
                report.Add("PROBLEM: ArenaSymmetry's Source must be assigned.");
                return report;
            }
            if (layout == null) { report.Add("PROBLEM: no ArenaLayout given."); return report; }

            for (int i = 0; i < arena.source.childCount; i++)
            {
                Transform child = arena.source.GetChild(i);
                if (child.GetComponent<ArenaBuiltGroup>() == null)
                {
                    report.Add($"PROBLEM: '{child.name}' under Source is not the builder's own - move the old art " +
                               "aside first (MoveOldArtAside) before building.");
                    return report;
                }
            }

            for (int i = arena.source.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(arena.source.GetChild(i).gameObject);

            Transform boundry = NewMarkedGroup(arena.source, BoundryGroupName);
            Transform blocks = NewMarkedGroup(arena.source, BlocksGroupName);
            Transform barriers = NewMarkedGroup(arena.source, BarriersGroupName);

            ArenaBounds wholeOutline = ArenaBounds.FromSourceOutline(arena.sourceOutline, arena.centre);
            int wallCount = 0;
            if (wholeOutline == null)
            {
                report.Add("PROBLEM: Source Outline has fewer than two points - no boundary walls were built.");
            }
            else
            {
                List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(wholeOutline.Polygon, arena.sourceOutline.Count, layout.WallThickness);
                float wallCentreY = (layout.WallBottomY + layout.WallTopY) * 0.5f;
                float wallHeight = layout.WallTopY - layout.WallBottomY;
                for (int i = 0; i < runs.Count; i++)
                {
                    ArenaWallPlan.Run run = runs[i];
                    Vector2 centreXZ = run.Centre(layout.WallThickness);
                    GameObject wall = NewPrimitiveChild(boundry, $"Wall {i}", layout.WallMaterial, "Building");
                    wall.transform.SetPositionAndRotation(new Vector3(centreXZ.x, wallCentreY, centreXZ.y),
                        Quaternion.Euler(0f, run.UnityYawDegrees, 0f));
                    wall.transform.localScale = new Vector3(run.Length, wallHeight, layout.WallThickness);
                    wallCount++;
                }
            }
            report.Add($"Boundry: {wallCount} walls built from the outline.");

            int blockCount = 0, barrierCount = 0;
            foreach (ArenaLayout.Piece piece in layout.Pieces)
            {
                if (piece.kind == ArenaLayout.PieceKind.Block)
                {
                    GameObject block = NewPrimitiveChild(blocks, piece.name, layout.BlockMaterial, "Building");
                    block.transform.SetPositionAndRotation(piece.centre, Quaternion.Euler(0f, piece.yawDegrees, 0f));
                    block.transform.localScale = piece.size;
                    blockCount++;
                }
                else
                {
                    GameObject barrier = NewPrimitiveChild(barriers, piece.name, layout.BarrierMaterial, ArenaLayers.BarrierLayerName);
                    Vector3 position = new Vector3(piece.centre.x, 0f, piece.centre.z);
                    barrier.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, piece.yawDegrees, 0f));
                    barrier.transform.localScale = piece.size; // x = length, y = look height, z = thickness

                    BoxCollider box = barrier.GetComponent<BoxCollider>();
                    float blockingCentreYWorld = (layout.BarrierBlockingBottomY + layout.BarrierBlockingTopY) * 0.5f;
                    float blockingHeightWorld = layout.BarrierBlockingTopY - layout.BarrierBlockingBottomY;
                    float scaleY = Mathf.Max(0.0001f, piece.size.y);
                    box.center = new Vector3(0f, (blockingCentreYWorld - position.y) / scaleY, 0f);
                    box.size = new Vector3(1f, blockingHeightWorld / scaleY, 1f);
                    barrierCount++;
                }
            }
            report.Add($"Blocks: {blockCount} built. Barriers: {barrierCount} built.");

            EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
            return report;
        }

        private static Transform NewMarkedGroup(Transform parent, string groupName)
        {
            var go = new GameObject(groupName);
            go.transform.SetParent(parent, false);
            go.AddComponent<ArenaBuiltGroup>();
            return go.transform;
        }

        private static GameObject NewPrimitiveChild(Transform parent, string childName, Material material, string layerName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer(layerName);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BoxCollider>();
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        /// <summary>Replaces the single marked floor slab under <paramref name="environment"/> (a sibling of Source,
        /// never inside it - Rebuild thirds would otherwise copy it three times, base plan Decision 12). One flat
        /// Default-layer cube, its top at world Y 0.</summary>
        public static void BuildFloor(Transform environment, ArenaLayout layout, Vector3 centre)
        {
            Transform existing = environment.Find(FloorObjectName);
            GameObject floor;
            if (existing != null)
            {
                floor = existing.gameObject;
                if (floor.GetComponent<MeshFilter>() == null) floor.AddComponent<MeshFilter>();
                if (floor.GetComponent<MeshRenderer>() == null) floor.AddComponent<MeshRenderer>();
                if (floor.GetComponent<BoxCollider>() == null) floor.AddComponent<BoxCollider>();
                if (floor.GetComponent<ArenaBuiltGroup>() == null) floor.AddComponent<ArenaBuiltGroup>();
            }
            else
            {
                floor = new GameObject(FloorObjectName);
                floor.transform.SetParent(environment, false);
                floor.AddComponent<ArenaBuiltGroup>();
                floor.AddComponent<MeshFilter>();
                floor.AddComponent<MeshRenderer>();
                floor.AddComponent<BoxCollider>();
            }

            floor.layer = LayerMask.NameToLayer("Default");
            floor.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            floor.GetComponent<MeshRenderer>().sharedMaterial = layout.FloorMaterial;
            GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.BatchingStatic);

            floor.transform.SetPositionAndRotation(new Vector3(centre.x, -layout.FloorThickness * 0.5f, centre.z), Quaternion.identity);
            floor.transform.localScale = new Vector3(layout.FloorSize.x, layout.FloorThickness, layout.FloorSize.y);
        }

        // ---- arena step 5: everything, in one call, applied to the real scene -------------------------------------

        /// <summary>
        /// Arena step 5: runs every phase, in order, on the one ArenaSymmetry found in <paramref name="scene"/> -
        /// 1) BuildTowerLooks, 2) MoveOldArtAside, 3) BuildSource, 4) BuildFloor, 5) ArenaSymmetryBuilder.Rebuild,
        /// 6) MinimapBaker.Bake, 7) the report and a final Validate. Stops before touching the floor or the copies if
        /// BuildSource reports a PROBLEM (Source held something it doesn't own) - never a partial build. Never runs
        /// in Play Mode; never saves the scene itself.
        /// </summary>
        public static List<string> BuildAll(Scene scene)
        {
            var report = new List<string>();
            if (EditorApplication.isPlaying)
            {
                report.Add(PlayModeRefusal);
                return report;
            }

            List<ArenaSymmetry> arenas = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ArenaSymmetry>(true))
                .ToList();
            if (arenas.Count != 1)
            {
                report.Add($"PROBLEM: found {arenas.Count} ArenaSymmetry components in the scene (need exactly one).");
                return report;
            }
            ArenaSymmetry arena = arenas[0];

            ArenaLayout layout = AssetDatabase.LoadAssetAtPath<ArenaLayout>("Assets/Gameplay/Config/ArenaLayout.asset");
            if (layout == null)
            {
                report.Add("PROBLEM: Assets/Gameplay/Config/ArenaLayout.asset does not exist - run Capture layout from Source first.");
                return report;
            }
            GameObject towerLookPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Arena/Tower Look.prefab");
            Transform environment = arena.transform.parent;
            if (environment == null)
            {
                report.Add($"PROBLEM: '{arena.name}' has no parent to hold Old Arena (off), the floor and the scenery.");
                return report;
            }

            report.Add("--- 1. BuildTowerLooks ---");
            report.AddRange(BuildTowerLooks(scene, towerLookPrefab));

            report.Add("--- 2. MoveOldArtAside ---");
            report.AddRange(MoveOldArtAside(arena, environment));

            report.Add("--- 3. BuildSource ---");
            List<string> sourceReport = BuildSource(arena, layout);
            report.AddRange(sourceReport);
            if (sourceReport.Any(l => l.Contains("PROBLEM")))
            {
                report.Add("Stopped: BuildSource reported a PROBLEM, so the floor, the copies and the minimap were not touched.");
                return report;
            }

            report.Add("--- 4. BuildFloor ---");
            BuildFloor(environment, layout, arena.centre);
            report.Add("Floor built.");

            report.Add("--- 5. ArenaSymmetryBuilder.Rebuild ---");
            report.AddRange(ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false));

            report.Add("--- 6. MinimapBaker.Bake ---");
            string bakeResult = MinimapBaker.Bake(arena);
            MinimapBaker.LogResult(bakeResult);
            report.Add(bakeResult);

            report.Add("--- 7. Final Validate ---");
            report.AddRange(ArenaSymmetryBuilder.Validate(arena));

            return report;
        }

        [MenuItem("OverPower/Arena/Build primitive arena")]
        private static void MenuBuildAll()
        {
            Scene scene = SceneManager.GetActiveScene();
            List<string> report = BuildAll(scene);
            Debug.Log("[ArenaPrimitiveBuilder] Build primitive arena:\n" + string.Join("\n", report));
        }
    }
}
