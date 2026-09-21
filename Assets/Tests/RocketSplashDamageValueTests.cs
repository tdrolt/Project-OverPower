using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overpower.Weapons;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 2026-09-21: "the explosion radius should deal more damage". The controller's default:
    /// splashDamage 20 -&gt; 28 (+40%) on all three rocket prefabs, falloff and radius unchanged - a near
    /// miss should hurt enough to matter (13.3 -&gt; 18.7 at 1m, 6.7 -&gt; 9.3 at 2m, on the 3m-radius
    /// prefabs) while a direct hit (38, or 30 on the cursor rocket) still pays more than any splash.
    ///
    /// AbilityVisualPrefabGuardTests.RocketSplashNumbersAreUnchanged already pins the raw authored
    /// numbers (splashDamage/splashRadius/the falloff curve's own keys) read-only off the prefab
    /// assets - this file does not repeat that. What it adds: the actual damage ExplodeOnImpact's own
    /// SplashDamageAt computes at specific distances, so a change to the falloff FORMULA (not just the
    /// tunable numbers) would also be caught. Each prefab's real splashDamage/splashRadius/falloff are
    /// read off its asset first (so this fails the same way the guard test does if a value drifts),
    /// then fed into a throwaway ExplodeOnImpact built fresh in an isolated preview scene (never the
    /// prefab's own cached instance) - the same isolation RocketAlwaysExplodesTests uses - so nothing
    /// here touches the real, currently-open Game Scene.
    /// </summary>
    public class RocketSplashDamageValueTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        private static T Field<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name}.{name}");
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }

        /// <summary>A fresh ExplodeOnImpact in an isolated preview scene, with splashDamage/splashRadius/falloff
        /// copied from the real prefab asset (never the asset's own cached component) - real authored numbers,
        /// zero risk of touching the currently-open Game Scene or the prefab asset itself.</summary>
        private ExplodeOnImpact BuildFromPrefab(string prefabPath)
        {
            ExplodeOnImpact source = LoadPrefab(prefabPath).GetComponent<ExplodeOnImpact>();
            Assert.IsNotNull(source, $"{prefabPath} has no ExplodeOnImpact");

            GameObject go = EditorUtility.CreateGameObjectWithHideFlags("Rocket", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            ExplodeOnImpact explode = go.AddComponent<ExplodeOnImpact>();

            SetField(explode, "splashDamage", Field<float>(source, "splashDamage"));
            SetField(explode, "splashRadius", Field<float>(source, "splashRadius"));
            SetField(explode, "falloff", Field<AnimationCurve>(source, "falloff"));
            SetField(explode, "splashMask", (LayerMask)0); // Isolation only - see RocketAlwaysExplodesTests's own note.

            ProjectileContext context = new ProjectileContext(-1, 1f, 1f, 100f, 10f, 1, 0, Vector3.forward, Vector3.zero);
            explode.OnSpawned(null, context);
            return explode;
        }

        private static float SplashDamageAt(ExplodeOnImpact explode, float distance)
        {
            MethodInfo method = typeof(ExplodeOnImpact).GetMethod("SplashDamageAt", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, "ExplodeOnImpact.SplashDamageAt");
            return (float)method.Invoke(explode, new object[] { Vector3.zero, new Vector3(distance, 0f, 0f) });
        }

        // Rocket.prefab / Rocket Distance.prefab: radius 3m. At the new 28 splashDamage: full at the centre,
        // 18.667 at 1m (13.333 at the old 20), 9.333 at 2m (6.667 at the old 20), 0 at the 3m edge - the exact
        // worked numbers in the controller's own brief.
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 0f, 28f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 1f, 18.6667f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 2f, 9.3333f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 3f, 0f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 0f, 28f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 1f, 18.6667f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 2f, 9.3333f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 3f, 0f)]
        // Rocket Cursor.prefab: radius 2.5m, so the same normalised (distance/radius) points land at 1m and 2m
        // differently - 16.8 at 1m, 5.6 at 2m, 0 at the 2.5m edge.
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 0f, 28f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 1f, 16.8f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 2f, 5.6f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 2.5f, 0f)]
        public void SplashDamageAtDistanceMatchesTheNewValue(string prefabPath, float distance, float expected)
        {
            ExplodeOnImpact explode = BuildFromPrefab(prefabPath);
            float amount = SplashDamageAt(explode, distance);
            Assert.AreEqual(expected, amount, 0.01f, $"{prefabPath} at {distance}m");
        }

        // Direct damage must be untouched by the splash retune - Tudor asked for more splash, not more direct
        // damage, and the whole "aiming still matters" argument in the brief depends on this staying put.
        [TestCase("Assets/Gameplay/Weapons/02 Rocket.asset", 38f)]
        [TestCase("Assets/Gameplay/Weapons/03 Rocket - Distance.asset", 38f)]
        [TestCase("Assets/Gameplay/Weapons/04 Rocket - Cursor Fire.asset", 30f)]
        public void DirectWeaponDamageIsUntouchedByTheSplashRetune(string weaponPath, float expectedDamage)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<Overpower.Data.WeaponDefinition>(weaponPath);
            Assert.IsNotNull(weapon, weaponPath);
            Assert.AreEqual(expectedDamage, weapon.Damage, 0.01f, weaponPath);
        }
    }
}
