using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Each gun family lands with its own impact and every gun fires the white muzzle flash; the old shared puff is gone from the
    /// guns. The Stun Gun and the Zip Gun keep their own impacts. Impacts are timed particles only: no collider, no light, no loop
    /// (VFXManager times each from its particle systems and reuses it).</summary>
    public class WeaponImpactPrefabTests
    {
        private const string PuffPath = "Assets/Prefabs/FXBulletExplosion.prefab";
        private const string OldMuzzlePath = "Assets/Prefabs/FXShot.prefab";
        private const string MuzzlePath = "Assets/Gameplay/FX/FX Muzzle Flash.prefab";
        private const string ImpactsFolder = "Assets/Gameplay/FX/Impacts/";

        // The shop's weapon tree: each upgrade stays in its root gun's family.
        private static readonly Dictionary<string, string> FamilyOf = new Dictionary<string, string>
        {
            { "01 Baseline", "Baseline" },
            { "02 Rocket", "Rocket" }, { "03 Rocket - Distance", "Rocket" }, { "04 Rocket - Cursor Fire", "Rocket" },
            { "05 Burst", "Burst" }, { "06 Burst - Charge", "Burst" }, { "07 Burst - Bounce", "Burst" },
            { "08 SMG", "SMG" }, { "09 SMG - Double Rate", "SMG" }, { "10 SMG - Shotgun", "SMG" },
            { "11 Laser", "Laser" }, { "12 Laser - Mark", "Laser" }, { "13 Laser - Through Walls", "Laser" },
        };

        private static string PathOf(Object o) => o != null ? AssetDatabase.GetAssetPath(o) : "(none)";

        private static string FallbackOf(GameObject projectile)
        {
            var motor = projectile.GetComponent<ProjectileMotor>();
            Assert.IsNotNull(motor, projectile.name);
            return PathOf(new SerializedObject(motor).FindProperty("fallbackImpactVfx").objectReferenceValue);
        }

        [Test]
        public void NoWeaponUsesThePuff()
        {
            foreach (WeaponDefinition weapon in BulletPrefabGuardTests.Weapons())
            {
                Assert.AreNotEqual(PuffPath, PathOf(weapon.ImpactVfx), weapon.name + " lands with its family's impact, not the shared puff");
                StringAssert.StartsWith(ImpactsFolder, PathOf(weapon.ImpactVfx), weapon.name);
                Assert.AreEqual(MuzzlePath, PathOf(weapon.MuzzleVfx), weapon.name + " fires the white muzzle flash");
                if (weapon.ProjectilePrefab.GetComponent<ProjectileMotor>() != null)
                    Assert.AreEqual(PathOf(weapon.ImpactVfx), FallbackOf(weapon.ProjectilePrefab),
                        weapon.ProjectilePrefab.name + ": its own fallback is its family's impact too");
            }
        }

        [Test]
        public void EachFamilyHasItsOwnImpact()
        {
            var impactOfFamily = new Dictionary<string, string>();
            foreach (WeaponDefinition weapon in BulletPrefabGuardTests.Weapons())
            {
                Assert.IsTrue(FamilyOf.TryGetValue(weapon.name, out string family), weapon.name + " is in the family table");
                string impact = PathOf(weapon.ImpactVfx);
                if (impactOfFamily.TryGetValue(family, out string seen))
                    Assert.AreEqual(seen, impact, weapon.name + " lands like the rest of the " + family + " family");
                else
                    impactOfFamily[family] = impact;
            }
            Assert.AreEqual(5, impactOfFamily.Count);
            Assert.AreEqual(5, new HashSet<string>(impactOfFamily.Values).Count, "no two families share an impact");
        }

        [Test]
        public void TheStunAndZipGunsKeepTheirOwnImpacts()
        {
            Assert.AreEqual("Assets/Gameplay/FX/Impacts/FX Stun Burst.prefab",
                FallbackOf(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Projectiles/Stun Gun Bullet.prefab")));
            Assert.AreEqual(PuffPath, FallbackOf(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab")));
        }

        [Test]
        public void ImpactsAndTheMuzzleFlashAreShortParticlesWithNoColliderOrLight()
        {
            var paths = new HashSet<string> { MuzzlePath };
            foreach (WeaponDefinition weapon in BulletPrefabGuardTests.Weapons())
                paths.Add(PathOf(weapon.ImpactVfx));
            Assert.AreNotEqual(OldMuzzlePath, MuzzlePath);
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                Assert.IsNotEmpty(systems, path + ": VFXManager times an effect from its particle systems");
                Assert.IsNotNull(prefab.GetComponent<ParticleSystem>(), path + ": a particle system on the root");
                foreach (ParticleSystem system in systems)
                {
                    Assert.IsFalse(system.main.loop, path + "/" + system.name + " must end on its own");
                    Assert.IsTrue(system.main.playOnAwake, path + "/" + system.name + " plays when it is switched on");
                }
                Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), path + " must have no collider");
                Assert.IsEmpty(prefab.GetComponentsInChildren<Light>(true), path + ": no light (the few shot lights are the only ones)");
            }
        }
    }
}
