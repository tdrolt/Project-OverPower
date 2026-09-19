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
                "Source's boundary walls, blocks and barriers come from Assets/Gameplay/Config/ArenaLayout.asset, " +
                "never hand-placed. Edit a row there, or move a block or barrier in the Scene view and press " +
                "'Capture layout from Source', then press 'Build primitive arena' below to rebuild Source from the " +
                "layout, copy it into both generated thirds and re-bake the minimap, all at once. 'Rebuild thirds' " +
                "alone still works for a quick look after moving a tower or a spawn point, without touching Source's " +
                "own walls/blocks/barriers - it also re-bakes the minimap; for other changes you can see from above, " +
                "use OverPower > Arena > Bake minimap image. The two generated thirds are rebuilt from Source every " +
                "time either button runs, so edits made to them are thrown away.",
                MessageType.Info);
            DrawDefaultInspector();

            var arena = (ArenaSymmetry)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Build primitive arena"))
                Report("Build primitive arena", ArenaPrimitiveBuilder.BuildAll(arena.gameObject.scene));
            if (GUILayout.Button("Rebuild thirds"))
            {
                Report("Rebuild thirds", ArenaSymmetryBuilder.Rebuild(arena, recordUndo: true));
                // Here and in the menu item, not inside ArenaSymmetryBuilder.Rebuild: its tests rebuild tiny arenas in
                // preview scenes and must not overwrite the real minimap image.
                MinimapBaker.LogResult(MinimapBaker.Bake(arena));
            }
            if (GUILayout.Button("Validate"))
                Report("Validate", ArenaSymmetryBuilder.Validate(arena));
        }

        [MenuItem("OverPower/Arena/Rebuild thirds")]
        private static void RebuildFromMenu()
        {
            ArenaSymmetry arena = FindArena();
            if (arena == null)
                return;
            Report("Rebuild thirds", ArenaSymmetryBuilder.Rebuild(arena, recordUndo: true));
            MinimapBaker.LogResult(MinimapBaker.Bake(arena));
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
            // FindFirstObjectByType would silently pick one of several open scenes' ArenaSymmetry components with
            // no way for the designer to tell which; with more than one, act on none and name them instead so the
            // designer can use that component's own Inspector button.
            ArenaSymmetry[] arenas = UnityEngine.Object.FindObjectsByType<ArenaSymmetry>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (arenas.Length == 0)
            {
                Debug.LogWarning("[Arena] There is no ArenaSymmetry in the open scene.");
                return null;
            }
            if (arenas.Length > 1)
            {
                var names = new System.Text.StringBuilder();
                foreach (ArenaSymmetry a in arenas)
                {
                    if (names.Length > 0) names.Append(", ");
                    names.Append($"'{a.name}' (scene '{a.gameObject.scene.name}')");
                }
                Debug.LogWarning($"[Arena] Found {arenas.Length} ArenaSymmetry components: {names}. Use that " +
                                  "component's own Inspector button instead of this menu.");
                return null;
            }
            return arenas[0];
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
