using Overpower.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>Shader globals do not survive into a fresh Editor session, so the arena would draw its fallback look in the Scene
    /// view and in tests. After every domain reload and scene open this pushes ArenaLook and the open arena's centre. It sets
    /// shader state only: no scene object, material or asset is changed.</summary>
    [InitializeOnLoad]
    public static class ArenaLookEditorGlobals
    {
        private const string LookPath = "Assets/_Project/Design/Settings/ArenaLook.asset";

        static ArenaLookEditorGlobals()
        {
            // AssetDatabase is not ready inside an InitializeOnLoad constructor.
            EditorApplication.delayCall += PushAll;
            EditorSceneManager.sceneOpened += (scene, mode) => PushAll();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void PushAll()
        {
            ArenaShaderGlobals.Push(AssetDatabase.LoadAssetAtPath<ArenaLook>(LookPath));
            var arena = Object.FindFirstObjectByType<ArenaSymmetry>(FindObjectsInactive.Include);
            if (arena != null)
                ArenaShaderGlobals.SetGridOrigin(new Vector2(arena.centre.x, arena.centre.z));
        }

        // Play Mode leaves the last played arena's centre behind (Dominion's, after a Dominion match).
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                PushAll();
        }
    }
}
