using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>Arena rebuild, controller finding (2026-09-19): a top-down render and a Play Mode capture showed two
    /// bright magenta slabs - Unity's tell for a renderer with a null or shader-less material. The actual cause
    /// turned out to be five leaked `~TestBarrier` HideAndDontSave props from an earlier Play Mode driver
    /// (SCRATCH\Step4aPlayCheck.cs) that never destroyed them and whose runtime `new Material(...)` was itself
    /// destroyed on leaving Play Mode, leaving a live GameObject with a null sharedMaterial - never part of
    /// Game Scene.unity, so it never showed in any scene diff. This test pins the thing that actually matters: no
    /// active renderer saved IN the scene ever ships with a missing, null or non-URP shader. It would not have
    /// caught the leaked props (they are not scene objects by construction), but it guards the real regression the
    /// controller was worried about.</summary>
    public class ArenaSceneShaderIntegrityTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";
        private const string UrpShaderPrefix = "Universal Render Pipeline/";

        [Test]
        public void NoActiveRendererUsesAMissingNullOrNonUrpShader()
        {
            WithGameScene(scene =>
            {
                var problems = new List<string>();
                foreach (Renderer renderer in Find<Renderer>(scene))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

                    Material[] mats = renderer.sharedMaterials;
                    if (mats == null || mats.Length == 0)
                    {
                        problems.Add($"{Path(renderer.transform)}: no materials");
                        continue;
                    }
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (m == null) { problems.Add($"{Path(renderer.transform)}: material[{i}] is null"); continue; }
                        Shader sh = m.shader;
                        if (sh == null) { problems.Add($"{Path(renderer.transform)}: material[{i}] '{m.name}' has a null shader"); continue; }
                        if (sh.name == "Hidden/InternalErrorShader")
                        {
                            problems.Add($"{Path(renderer.transform)}: material[{i}] '{m.name}' uses the missing-shader placeholder");
                            continue;
                        }
                        if (!sh.name.StartsWith(UrpShaderPrefix))
                            problems.Add($"{Path(renderer.transform)}: material[{i}] '{m.name}' uses '{sh.name}', not a {UrpShaderPrefix}* shader");
                    }
                }
                Assert.IsEmpty(problems, "Active renderer(s) with a missing/null/non-URP shader (would render magenta):\n" + string.Join("\n", problems));
            });
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        // Uses Game Scene if it's already open (the normal case: read-only checks on what's loaded), otherwise opens it
        // additively and closes it again. Never opens it Single, which would prompt to save a dirty scene.
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
