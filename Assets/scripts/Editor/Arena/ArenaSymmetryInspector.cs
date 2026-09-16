using System.Collections.Generic;
using Overpower.Arena;
using UnityEditor;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>Adds the Rebuild thirds / Validate buttons to ArenaSymmetry, and the same two actions under the
    /// OverPower > Arena menu. Results go to the Console.</summary>
    [CustomEditor(typeof(ArenaSymmetry))]
    public class ArenaSymmetryInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Edit only the objects under Source, then press Rebuild thirds, check the arena, and save the scene. " +
                "The two generated thirds are rebuilt from Source every time, so edits made to them are thrown away.",
                MessageType.Info);
            DrawDefaultInspector();

            var arena = (ArenaSymmetry)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild thirds"))
                Report("Rebuild thirds", ArenaSymmetryBuilder.Rebuild(arena, recordUndo: true));
            if (GUILayout.Button("Validate"))
                Report("Validate", ArenaSymmetryBuilder.Validate(arena));
        }

        [MenuItem("OverPower/Arena/Rebuild thirds")]
        private static void RebuildFromMenu()
        {
            ArenaSymmetry arena = FindArena();
            if (arena != null)
                Report("Rebuild thirds", ArenaSymmetryBuilder.Rebuild(arena, recordUndo: true));
        }

        [MenuItem("OverPower/Arena/Validate")]
        private static void ValidateFromMenu()
        {
            ArenaSymmetry arena = FindArena();
            if (arena != null)
                Report("Validate", ArenaSymmetryBuilder.Validate(arena));
        }

        private static ArenaSymmetry FindArena()
        {
            ArenaSymmetry arena = UnityEngine.Object.FindFirstObjectByType<ArenaSymmetry>(FindObjectsInactive.Include);
            if (arena == null)
                Debug.LogWarning("[Arena] There is no ArenaSymmetry in the open scene.");
            return arena;
        }

        private static void Report(string action, List<string> problems)
        {
            if (problems.Count == 0)
                Debug.Log($"[Arena] {action}: every generated object and snapped partner matches its source.");
            else
                Debug.LogWarning($"[Arena] {action}: {problems.Count} problem(s):\n" + string.Join("\n", problems));
        }
    }
}
