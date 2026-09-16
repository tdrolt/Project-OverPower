using System.Collections.Generic;
using Overpower.Arena;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>
    /// The arena tool behind ArenaSymmetry's "Rebuild thirds" and "Validate" buttons. See ArenaSymmetry's class
    /// comment for the designer workflow; this class is the how.
    /// </summary>
    public static class ArenaSymmetryBuilder
    {
        /// <summary>How far a copy or snapped partner may sit from where its source puts it before Validate reports
        /// it. Tight on purpose: the tool places copies exactly, so anything bigger means a hand edit or a forgotten
        /// rebuild.</summary>
        public const float PositionToleranceMetres = 0.05f;
        public const float AngleToleranceDegrees = 0.5f;

        private const string UndoName = "Rebuild arena thirds";

        /// <summary>Deletes both generated thirds, copies Source into them turned 120° and 240°, moves snapped
        /// partners and centred objects, and returns Validate's findings. If the setup is wrong it changes nothing
        /// and returns what is wrong. It never saves the scene: the designer looks first, then saves.</summary>
        public static List<string> Rebuild(ArenaSymmetry arena, bool recordUndo)
        {
            List<string> problems = CheckSetup(arena);
            if (problems.Count > 0)
                return problems;

            int undoGroup = Undo.GetCurrentGroup();
            if (recordUndo)
                Undo.SetCurrentGroupName(UndoName);

            Transform[] targets = { arena.generated120, arena.generated240 };
            for (int t = 0; t < targets.Length; t++)
            {
                Transform target = targets[t];
                for (int i = target.childCount - 1; i >= 0; i--)
                {
                    GameObject old = target.GetChild(i).gameObject;
                    if (recordUndo) Undo.DestroyObjectImmediate(old);
                    else UnityEngine.Object.DestroyImmediate(old);
                }

                int thirds = t + 1;
                for (int i = 0; i < arena.source.childCount; i++)
                {
                    Transform original = arena.source.GetChild(i);
                    GameObject copy = UnityEngine.Object.Instantiate(original.gameObject, target);
                    copy.name = original.name;
                    copy.transform.SetPositionAndRotation(
                        RadialSymmetry.RotatePoint(original.position, arena.centre, thirds),
                        RadialSymmetry.RotateRotation(original.rotation, thirds));
                    copy.transform.localScale = original.localScale;
                    foreach (Transform part in copy.GetComponentsInChildren<Transform>(true))
                        part.gameObject.hideFlags |= HideFlags.NotEditable;
                    if (recordUndo)
                        Undo.RegisterCreatedObjectUndo(copy, UndoName);
                }
            }

            foreach (ArenaSymmetry.SnappedTriplet triplet in arena.snappedTriplets)
            {
                Place(triplet.at120, RadialSymmetry.RotatePoint(triplet.source.position, arena.centre, 1),
                      RadialSymmetry.RotateRotation(triplet.source.rotation, 1), recordUndo);
                Place(triplet.at240, RadialSymmetry.RotatePoint(triplet.source.position, arena.centre, 2),
                      RadialSymmetry.RotateRotation(triplet.source.rotation, 2), recordUndo);
            }

            foreach (Transform middle in arena.centred)
                Place(middle, new Vector3(arena.centre.x, middle.position.y, arena.centre.z), middle.rotation, recordUndo);

            if (recordUndo)
                Undo.CollapseUndoOperations(undoGroup);
            if (!EditorSceneManager.IsPreviewScene(arena.gameObject.scene))
                EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);

            return Validate(arena);
        }

        /// <summary>Everything that doesn't match its source, one line each. Empty means symmetric.</summary>
        public static List<string> Validate(ArenaSymmetry arena)
        {
            List<string> problems = CheckSetup(arena);
            if (problems.Count > 0)
                return problems;

            Transform[] targets = { arena.generated120, arena.generated240 };
            for (int t = 0; t < targets.Length; t++)
            {
                Transform target = targets[t];
                if (target.childCount != arena.source.childCount)
                {
                    problems.Add($"{target.name} has {target.childCount} objects but Source has {arena.source.childCount}: press Rebuild thirds.");
                    continue;
                }
                for (int i = 0; i < arena.source.childCount; i++)
                {
                    Transform original = arena.source.GetChild(i);
                    Transform copy = target.GetChild(i);
                    if (copy.name != original.name)
                        problems.Add($"{target.name}: object {i} is '{copy.name}' but Source's is '{original.name}': press Rebuild thirds.");
                    Compare(original, copy, arena.centre, t + 1, problems);
                    if ((copy.localScale - original.localScale).sqrMagnitude > 1e-6f)
                        problems.Add($"{copy.name} ({(t + 1) * 120}°) has a different scale from its Source.");
                }
            }

            foreach (ArenaSymmetry.SnappedTriplet triplet in arena.snappedTriplets)
            {
                Compare(triplet.source, triplet.at120, arena.centre, 1, problems);
                Compare(triplet.source, triplet.at240, arena.centre, 2, problems);
            }

            foreach (Transform middle in arena.centred)
            {
                float off = new Vector2(middle.position.x - arena.centre.x, middle.position.z - arena.centre.z).magnitude;
                if (off > PositionToleranceMetres)
                    problems.Add($"{middle.name} is {off:0.00} m from the centre.");
            }

            return problems;
        }

        private static List<string> CheckSetup(ArenaSymmetry arena)
        {
            var problems = new List<string>();
            if (arena == null) { problems.Add("No ArenaSymmetry given."); return problems; }
            if (arena.source == null || arena.generated120 == null || arena.generated240 == null)
            {
                problems.Add("Source, Generated 120 and Generated 240 must all be assigned.");
                return problems;
            }
            if (arena.source == arena.generated120 || arena.source == arena.generated240 || arena.generated120 == arena.generated240
                || arena.generated120.IsChildOf(arena.source) || arena.generated240.IsChildOf(arena.source)
                || arena.source.IsChildOf(arena.generated120) || arena.source.IsChildOf(arena.generated240))
                problems.Add("Source and the two generated thirds must be three separate objects, none inside another.");

            foreach (Transform parent in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                // Copies are placed in world space but keep Source's local scale, which is only the same thing when
                // these parents have no position, rotation or scale of their own.
                if (parent.position != Vector3.zero || parent.rotation != Quaternion.identity || parent.lossyScale != Vector3.one)
                    problems.Add($"{parent.name} must sit at position 0, rotation 0, scale 1 (it doesn't).");
            }

            foreach (PhotonView view in arena.source.GetComponentsInChildren<PhotonView>(true))
                problems.Add($"'{view.name}' under Source has a PhotonView. Networked objects can't be copied (their view ids must stay unique): move it out of Source and add it to Snapped Triplets instead.");
            foreach (BuildingCapture tower in arena.source.GetComponentsInChildren<BuildingCapture>(true))
                problems.Add($"'{tower.name}' under Source is a capture tower. Towers can't be copied (their ids must stay unique): move it out of Source and add it to Snapped Triplets instead.");

            for (int i = 0; i < arena.snappedTriplets.Count; i++)
            {
                ArenaSymmetry.SnappedTriplet triplet = arena.snappedTriplets[i];
                if (triplet == null || triplet.source == null || triplet.at120 == null || triplet.at240 == null)
                {
                    problems.Add($"Snapped Triplet {i} has an empty slot.");
                    continue;
                }
                foreach (Transform member in new[] { triplet.source, triplet.at120, triplet.at240 })
                    if (IsUnderArenaThirds(arena, member))
                        problems.Add($"'{member.name}' (Snapped Triplet {i}) is inside Source or a generated third, so a rebuild would copy or delete it.");
            }
            foreach (Transform middle in arena.centred)
            {
                if (middle == null) problems.Add("Centred has an empty slot.");
                else if (IsUnderArenaThirds(arena, middle)) problems.Add($"'{middle.name}' (Centred) is inside Source or a generated third.");
            }
            return problems;
        }

        private static bool IsUnderArenaThirds(ArenaSymmetry arena, Transform t) =>
            t.IsChildOf(arena.source) || t.IsChildOf(arena.generated120) || t.IsChildOf(arena.generated240);

        private static void Compare(Transform original, Transform copy, Vector3 centre, int thirds, List<string> problems)
        {
            float drift = Vector3.Distance(RadialSymmetry.RotatePoint(original.position, centre, thirds), copy.position);
            if (drift > PositionToleranceMetres)
                problems.Add($"{copy.name} ({thirds * 120}°) is {drift:0.00} m from where {original.name} puts it.");
            float turn = Quaternion.Angle(RadialSymmetry.RotateRotation(original.rotation, thirds), copy.rotation);
            if (turn > AngleToleranceDegrees)
                problems.Add($"{copy.name} ({thirds * 120}°) is turned {turn:0.0}° away from where {original.name} puts it.");
        }

        private static void Place(Transform target, Vector3 position, Quaternion rotation, bool recordUndo)
        {
            if (recordUndo)
                Undo.RecordObject(target, UndoName);
            target.SetPositionAndRotation(position, rotation);
            // Towers 1/2/4/5/7/8 are prefab instances: without this a scripted move can be lost on save.
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
