using System.Collections.Generic;
using Overpower.Arena;
using Overpower.Data;
using UnityEditor;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>
    /// Reads today's Source hierarchy (a group per kind, a placement unit per direct child) into ArenaLayout rows -
    /// one Piece per unit's own collider, box for box (base plan Decision 3). The Boundry group is skipped outright:
    /// arena step 4 amended the boundary to be built fresh from Arena Symmetry's Source Outline every time
    /// (ArenaWallPlan), so a captured wall row would only go stale the moment somebody moved the outline. A group
    /// literally named "Barriers" becomes Barrier rows (Amendment 1's jersey barriers); every other group becomes
    /// plain Block rows.
    /// </summary>
    public static class ArenaLayoutCapture
    {
        private const string BarriersGroupName = "Barriers";
        private const float MaxTiltDegrees = 0.5f;
        private const float TowerKeepClearMarginMetres = 0.5f;

        /// <summary>
        /// Walks every group directly under <paramref name="root"/> and every unit directly under each group. Skips
        /// (and reports) the Boundry group entirely, a unit inside a tower's keep-clear circle, and a unit with no
        /// box to read. Reports a pitch or roll over 0.5 degrees as a PROBLEM (it would be lost - a captured box is
        /// always upright). Colliders on a unit's own children are counted and ignored: only the unit's own first
        /// enabled non-trigger BoxCollider (or, failing that, its own MeshCollider) is read.
        /// </summary>
        public static List<ArenaLayout.Piece> Capture(Transform root, IReadOnlyList<(Vector3 centre, float radius)> keepClear,
                                                        List<string> report)
        {
            var pieces = new List<ArenaLayout.Piece>();
            if (root == null)
            {
                report.Add("PROBLEM: no Source root given.");
                return pieces;
            }

            int boundrySkipped = 0, keepClearSkipped = 0, noBoxSkipped = 0, childCollidersIgnored = 0;

            for (int g = 0; g < root.childCount; g++)
            {
                Transform group = root.GetChild(g);
                bool isBoundary = group.name == ArenaSymmetry.BoundaryGroupName;
                bool isBarriers = group.name == BarriersGroupName;
                ArenaLayout.PieceKind kind = isBarriers ? ArenaLayout.PieceKind.Barrier : ArenaLayout.PieceKind.Block;

                for (int u = 0; u < group.childCount; u++)
                {
                    Transform unit = group.GetChild(u);

                    if (isBoundary)
                    {
                        boundrySkipped++;
                        continue;
                    }

                    Vector3 xz = new Vector3(unit.position.x, 0f, unit.position.z);
                    bool nearTower = false;
                    for (int t = 0; t < (keepClear?.Count ?? 0); t++)
                    {
                        (Vector3 centre, float radius) tower = keepClear[t];
                        Vector3 towerXz = new Vector3(tower.centre.x, 0f, tower.centre.z);
                        if (Vector3.Distance(xz, towerXz) <= tower.radius + TowerKeepClearMarginMetres)
                        {
                            nearTower = true;
                            break;
                        }
                    }
                    if (nearTower)
                    {
                        keepClearSkipped++;
                        report.Add($"skipped '{unit.name}' (in {group.name}): inside a tower's keep-clear circle.");
                        continue;
                    }

                    if (!TryReadBox(unit, kind, out Vector3 centre, out Vector3 size, out float yaw, out int ignoredOnChildren, report))
                    {
                        noBoxSkipped++;
                        report.Add($"skipped '{unit.name}' (in {group.name}): no BoxCollider or MeshCollider of its own.");
                        continue;
                    }
                    childCollidersIgnored += ignoredOnChildren;

                    pieces.Add(new ArenaLayout.Piece
                    {
                        name = unit.name,
                        kind = kind,
                        centre = centre,
                        yawDegrees = yaw,
                        size = size
                    });
                }
            }

            report.Add($"capture: {pieces.Count} rows; Boundry skipped {boundrySkipped} (from the outline); " +
                       $"{keepClearSkipped} skipped near a tower; {noBoxSkipped} skipped with no box; " +
                       $"{childCollidersIgnored} child colliders ignored.");
            return pieces;
        }

        private static bool TryReadBox(Transform unit, ArenaLayout.PieceKind kind, out Vector3 centre, out Vector3 size,
                                        out float yawDegrees, out int ignoredOnChildren, List<string> report)
        {
            centre = unit.position;
            size = Vector3.zero;
            yawDegrees = unit.eulerAngles.y;
            ignoredOnChildren = 0;

            ReportTilt(unit, report);

            if (kind == ArenaLayout.PieceKind.Barrier)
            {
                // A built barrier's own BoxCollider spans the BLOCKING band (e.g. world y -1..3), not the look - it
                // is set from ArenaLayout's separate barrierBlockingBottomY/TopY, not from the row's own size.
                // Reading it back as the row's size would inflate a 1 m look into a multi-metre one, and the next
                // Build would show a wall shots pass through where a barrier should be (review, 2026-09-19). The
                // look IS exactly the unit's own scale (x=length, y=look height, z=thickness); the centre's Y is
                // ignored because Build always re-derives a barrier's vertical position from size.y itself.
                Vector3 barrierLossy = unit.lossyScale;
                size = new Vector3(Mathf.Abs(barrierLossy.x), Mathf.Abs(barrierLossy.y), Mathf.Abs(barrierLossy.z));
                centre = new Vector3(unit.position.x, 0f, unit.position.z);
                foreach (BoxCollider box in unit.GetComponentsInChildren<BoxCollider>(true))
                    if (box.transform != unit) ignoredOnChildren++;
                foreach (MeshCollider mesh in unit.GetComponentsInChildren<MeshCollider>(true))
                    if (mesh.transform != unit) ignoredOnChildren++;
                return true;
            }

            // No "break" on finding the unit's own box: every OTHER collider found while scanning (on a child, or a
            // second one on the unit itself) must still be counted as ignored, not left uncounted just because the
            // loop stopped early the moment a usable one turned up.
            BoxCollider[] allBoxes = unit.GetComponentsInChildren<BoxCollider>(true);
            BoxCollider ownBox = null;
            foreach (BoxCollider box in allBoxes)
            {
                if (box.transform != unit)
                {
                    ignoredOnChildren++;
                    continue;
                }
                if (ownBox != null || box.isTrigger || !box.enabled)
                    continue;
                ownBox = box;
            }

            if (ownBox != null)
            {
                centre = unit.TransformPoint(ownBox.center);
                Vector3 lossy = unit.lossyScale;
                size = Vector3.Scale(ownBox.size, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
                return true;
            }

            MeshCollider[] allMeshes = unit.GetComponentsInChildren<MeshCollider>(true);
            MeshCollider ownMesh = null;
            foreach (MeshCollider mesh in allMeshes)
            {
                if (mesh.transform != unit)
                {
                    ignoredOnChildren++;
                    continue;
                }
                if (ownMesh != null || !mesh.enabled || mesh.sharedMesh == null)
                    continue;
                ownMesh = mesh;
            }
            if (ownMesh == null)
                return false;

            Bounds bounds = ownMesh.sharedMesh.bounds;
            centre = unit.TransformPoint(bounds.center);
            Vector3 meshLossy = unit.lossyScale;
            size = Vector3.Scale(bounds.size, new Vector3(Mathf.Abs(meshLossy.x), Mathf.Abs(meshLossy.y), Mathf.Abs(meshLossy.z)));
            return true;
        }

        private static void ReportTilt(Transform unit, List<string> report)
        {
            Vector3 euler = unit.eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, euler.x);
            float roll = Mathf.DeltaAngle(0f, euler.z);
            if (Mathf.Abs(pitch) > MaxTiltDegrees || Mathf.Abs(roll) > MaxTiltDegrees)
                report.Add($"PROBLEM: '{unit.name}' is tilted (pitch {pitch:0.00} deg, roll {roll:0.00} deg) - " +
                           "a captured box is always upright, so this tilt would be lost.");
        }

        [MenuItem("OverPower/Arena/Capture layout from Source")]
        private static void MenuCapture()
        {
            ArenaSymmetry[] arenas = Object.FindObjectsByType<ArenaSymmetry>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (arenas.Length != 1)
            {
                Debug.LogError($"[ArenaLayoutCapture] Found {arenas.Length} ArenaSymmetry components (need exactly one).");
                return;
            }
            ArenaLayout layout = AssetDatabase.LoadAssetAtPath<ArenaLayout>("Assets/Gameplay/Config/ArenaLayout.asset");
            if (layout == null)
            {
                Debug.LogError("[ArenaLayoutCapture] Assets/Gameplay/Config/ArenaLayout.asset does not exist yet.");
                return;
            }

            // Every tower in the scene, not just ones nested under Source: a snapped triplet's own "Source" member
            // (the one a designer places by hand) lives wherever the scene puts towers (under Houses/Cathedral
            // groups), never as a child of ArenaSymmetry.source itself - that transform only ever holds the
            // environment art Rebuild copies.
            var keepClear = new List<(Vector3 centre, float radius)>();
            const float TowerLookCapsuleRadius = 2.6f; // Tower Look.prefab's own collider radius (base plan Decision 6).
            foreach (BuildingCapture tower in Object.FindObjectsByType<BuildingCapture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                keepClear.Add((tower.transform.position, TowerLookCapsuleRadius));

            var report = new List<string>();
            List<ArenaLayout.Piece> pieces = Capture(arenas[0].source, keepClear, report);
            WritePieces(layout, pieces);
            AssetDatabase.SaveAssetIfDirty(layout);

            Debug.Log("[ArenaLayoutCapture] " + string.Join("\n", report));
        }

        /// <summary>Writes <paramref name="pieces"/> into <paramref name="layout"/>'s serialized field through a
        /// SerializedObject, the same way every hand-saved asset in this project is written (CODING-STANDARDS #6) -
        /// shared by the real "Capture layout from Source" menu and by tests that need a layout with known rows
        /// without going through a whole scene capture. Does not itself save the asset.</summary>
        public static void WritePieces(ArenaLayout layout, IReadOnlyList<ArenaLayout.Piece> pieces)
        {
            var so = new SerializedObject(layout);
            SerializedProperty piecesProp = so.FindProperty("pieces");
            piecesProp.arraySize = pieces.Count;
            for (int i = 0; i < pieces.Count; i++)
            {
                SerializedProperty element = piecesProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = pieces[i].name;
                element.FindPropertyRelative("kind").enumValueIndex = (int)pieces[i].kind;
                element.FindPropertyRelative("centre").vector3Value = pieces[i].centre;
                element.FindPropertyRelative("yawDegrees").floatValue = pieces[i].yawDegrees;
                element.FindPropertyRelative("size").vector3Value = pieces[i].size;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Writes every material reference through a SerializedObject, the same reasoning as WritePieces -
        /// shared by whatever creates ArenaLayout.asset for real and by tests that need a layout with known
        /// materials. Does not itself save the asset.</summary>
        public static void WriteMaterials(ArenaLayout layout, Material wall, float wallThickness, float wallBottomY, float wallTopY,
                                           Material block, Material barrier, float barrierBlockingBottomY, float barrierBlockingTopY,
                                           Material floor, Vector2 floorSize, float floorThickness)
        {
            var so = new SerializedObject(layout);
            so.FindProperty("wallMaterial").objectReferenceValue = wall;
            so.FindProperty("wallThickness").floatValue = wallThickness;
            so.FindProperty("wallBottomY").floatValue = wallBottomY;
            so.FindProperty("wallTopY").floatValue = wallTopY;
            so.FindProperty("blockMaterial").objectReferenceValue = block;
            so.FindProperty("barrierMaterial").objectReferenceValue = barrier;
            so.FindProperty("barrierBlockingBottomY").floatValue = barrierBlockingBottomY;
            so.FindProperty("barrierBlockingTopY").floatValue = barrierBlockingTopY;
            so.FindProperty("floorMaterial").objectReferenceValue = floor;
            so.FindProperty("floorSize").vector2Value = floorSize;
            so.FindProperty("floorThickness").floatValue = floorThickness;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
