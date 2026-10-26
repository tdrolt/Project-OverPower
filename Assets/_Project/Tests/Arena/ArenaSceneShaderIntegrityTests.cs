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
                        string problem = ShaderProblem(sh);
                        if (problem != null)
                            problems.Add($"{Path(renderer.transform)}: material[{i}] '{m.name}' {problem}");
                    }
                }
                Assert.IsEmpty(problems, "Active renderer(s) with a missing/null/non-URP shader (would render magenta):\n" + string.Join("\n", problems));
            });
        }

        [Test]
        public void UnknownPrefixStillFails()
        {
            Assert.IsFalse(NameAllowed("Overpower/Not An Arena Shader"), "an Overpower shader that is not on the list");
            Assert.IsFalse(NameAllowed("Legacy Shaders/Diffuse"), "a built-in pipeline shader");
            Assert.IsFalse(NameAllowed("Overpower/Arena Surface (copy)"), "the list is exact names, not prefixes");
            Assert.IsTrue(NameAllowed("Universal Render Pipeline/Lit"));
            Assert.IsTrue(NameAllowed("Overpower/Arena Surface"));
            Shader builtIn = Shader.Find("Unlit/Color");
            Assert.NotNull(builtIn, "the fixture: Unlit/Color exists");
            StringAssert.Contains("not a " + UrpShaderPrefix, ShaderProblem(builtIn) ?? "", "a real built-in pipeline shader is reported by name");
            Assert.IsNull(ShaderProblem(Shader.Find("Universal Render Pipeline/Lit")));
        }

        /// <summary>Custom shaders an arena renderer may use. Each is hand-written on URP's own includes; a new one is added here by
        /// name, never by renaming it into the URP prefix.</summary>
        private static readonly string[] AllowedCustomShaders = { "Overpower/Arena Surface" };

        private static bool NameAllowed(string shaderName) =>
            shaderName.StartsWith(UrpShaderPrefix) || Array.IndexOf(AllowedCustomShaders, shaderName) >= 0;

        private static string ShaderProblem(Shader sh)
        {
            if (sh == null) return "has a null shader";
            if (sh.name == "Hidden/InternalErrorShader") return "uses the missing-shader placeholder";
            if (!NameAllowed(sh.name)) return $"uses '{sh.name}', not a {UrpShaderPrefix}* shader or an allowed custom one";
            if (ShaderUtil.ShaderHasError(sh)) return $"uses '{sh.name}', which has compile errors";
            if (!sh.isSupported) return $"uses '{sh.name}', which this machine cannot run";
            return null;
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
