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
    /// Tudor, 2026-09-21: "the explosion radius should deal more damage" (the controller's default: splashDamage
    /// 20 -&gt; 28 on all three rocket prefabs, falloff and radius unchanged). Rewritten 2026-09-24 (Tudor: "remove
    /// tests that are outdated" - he keeps tuning splashDamage/splashRadius/weapon Damage from the Inspector, so
    /// pinning either the raw numbers or a distance's literal resulting damage went red on every tuning pass with
    /// nothing actually broken).
    ///
    /// What is left guards the RULES, not the numbers, both read fresh off the real assets every run:
    /// - SplashDamageAtDistanceFractionMatchesTheFalloffFormula: ExplodeOnImpact.SplashDamageAt's own formula
    ///   (splashDamage * falloff.Evaluate(distance / splashRadius)) still matches an independent recomputation of
    ///   that same formula from the prefab's OWN current splashDamage/splashRadius/falloff - so a change to the
    ///   FORMULA itself (not the tunable numbers) is still caught, at whatever numbers Tudor has set.
    /// - ADirectHitPaysMoreThanTheBestSplash: a direct hit must still pay more than the best possible splash (the
    ///   whole "aiming still matters" argument depends on this), never a fixed number.
    ///
    /// Each prefab's real splashDamage/splashRadius/falloff are read off its asset, then fed into a throwaway
    /// ExplodeOnImpact built fresh in an isolated preview scene (never the prefab's own cached instance) - the same
    /// isolation RocketAlwaysExplodesTests uses - so nothing here touches the real, currently-open Game Scene.
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

        // Four points across the blast (centre, a third out, two thirds out, the edge), as a FRACTION of whatever
        // splashRadius the prefab currently has - not a fixed metre distance - so this keeps testing the same four
        // meaningful points on the curve no matter how Tudor retunes splashRadius.
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 0f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 0.3333333f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 0.6666667f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 1f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 0f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 0.3333333f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 0.6666667f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 1f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 0f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 0.3333333f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 0.6666667f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 1f)]
        public void SplashDamageAtDistanceFractionMatchesTheFalloffFormula(string prefabPath, float radiusFraction)
        {
            ExplodeOnImpact source = LoadPrefab(prefabPath).GetComponent<ExplodeOnImpact>();
            Assert.IsNotNull(source, $"{prefabPath} has no ExplodeOnImpact");
            float radius = Field<float>(source, "splashRadius");
            float damage = Field<float>(source, "splashDamage");
            AnimationCurve falloff = Field<AnimationCurve>(source, "falloff");

            float distance = radius * radiusFraction;
            // The same formula ExplodeOnImpact.SplashDamageAt itself uses (see its own comment), recomputed here
            // independently from the prefab's live numbers rather than compared against a hardcoded result.
            float expected = damage * Mathf.Max(0f, falloff.Evaluate(Mathf.Clamp01(radiusFraction)));

            ExplodeOnImpact explode = BuildFromPrefab(prefabPath);
            float actual = SplashDamageAt(explode, distance);
            Assert.AreEqual(expected, actual, 0.01f, $"{prefabPath} at {radiusFraction:P0} of splashRadius ({distance:F2}m)");
        }

        // Direct damage must always pay more than the best possible splash - Tudor asked for more splash, not more
        // direct damage, and the whole "aiming still matters" argument depends on this relationship, not on either
        // number's actual value.
        [TestCase("Assets/Gameplay/Weapons/02 Rocket.asset")]
        [TestCase("Assets/Gameplay/Weapons/03 Rocket - Distance.asset")]
        [TestCase("Assets/Gameplay/Weapons/04 Rocket - Cursor Fire.asset")]
        public void ADirectHitPaysMoreThanTheBestSplash(string weaponPath)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<Overpower.Data.WeaponDefinition>(weaponPath);
            Assert.IsNotNull(weapon, weaponPath);
            Assert.IsNotNull(weapon.ProjectilePrefab, $"{weaponPath}.ProjectilePrefab");

            string projectilePrefabPath = AssetDatabase.GetAssetPath(weapon.ProjectilePrefab);
            ExplodeOnImpact explode = BuildFromPrefab(projectilePrefabPath);
            float bestSplash = SplashDamageAt(explode, 0f); // distance 0 = the strongest a splash hit can ever be

            Assert.Greater(weapon.Damage, bestSplash,
                $"{weaponPath}.Damage ({weapon.Damage}) should pay more than {projectilePrefabPath}'s best splash ({bestSplash})");
        }
    }
}
