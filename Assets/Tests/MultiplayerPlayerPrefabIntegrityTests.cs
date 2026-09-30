using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Guard added when the dead BulletController was removed from Multiplayer Player.prefab
    /// (2026-09-21, diagnosed 2026-09-20: legacy code nothing called, printing "Rigidbody isn't found!" on every
    /// player spawn). A precise YAML edit to drop one component is easy to get subtly wrong - drop the
    /// MonoBehaviour block but leave its `m_Component` entry, or the reverse - and Unity only shows that as a
    /// silent "Missing (Mono Script)" row in the Inspector; nothing throws and nothing logs by default. This pins
    /// that every MonoBehaviour anywhere in the prefab (recursively) still resolves to a real script.</summary>
    public class MultiplayerPlayerPrefabIntegrityTests
    {
        private const string PrefabPath = "Assets/Resources/Multiplayer Player.prefab";

        // A real MonoBehaviour subclass (compiled from this very file, so it has a genuine MonoScript asset and
        // GUID) whose m_Script reference the sanity check below breaks on purpose, entirely in memory. Test-only;
        // never attached to anything but the throwaway GameObject in the test below.
        private sealed class ProbeBehaviour : MonoBehaviour { }

        [Test]
        public void TheMissingScriptCheckActuallyCatchesAMissingScript()
        {
            // Proves the detector used below is not a no-op before trusting it on the real prefab - same
            // reasoning as ArenaSceneShaderIntegrityTests' own self-check against a preview scene. Breaks a REAL
            // component's script reference on a throwaway, never-saved GameObject (nothing under Assets/ is
            // created or deleted - the project rule against scratch files there stays intact).
            var probe = new GameObject("MissingScriptProbe");
            try
            {
                ProbeBehaviour real = probe.AddComponent<ProbeBehaviour>();
                var serialized = new SerializedObject(real);
                serialized.FindProperty("m_Script").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.Greater(CountMissingScripts(probe), 0,
                    "sanity check failed: breaking a component's m_Script reference should be detected as a missing script - the real prefab check below cannot be trusted otherwise");
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void EveryMonoBehaviourOnThePrefabResolvesToARealScript()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"{PrefabPath} not found");

            Assert.AreEqual(0, CountMissingScripts(prefab),
                $"{PrefabPath} has a MonoBehaviour whose script cannot be resolved - open the prefab and look for " +
                "'Missing (Mono Script)' in the Inspector.");
        }

        // Recursive: GameObjectUtility.GetMonoBehavioursWithMissingScriptCount only counts the GameObject it is
        // given, not its children, so this walks the whole hierarchy itself.
        private static int CountMissingScripts(GameObject root)
        {
            int total = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
            foreach (Transform child in root.transform)
                total += CountMissingScripts(child.gameObject);
            return total;
        }
    }
}
