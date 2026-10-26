using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Each gun family lands with its own impact and every gun fires the white muzzle flash; the old shared puff is gone from the
    /// guns. The Stun Gun and the Zip Gun have their own impacts. Impacts are timed particles only: no collider, no light, no loop
    /// (VFXManager times each from its particle systems and reuses it).</summary>
    public class WeaponImpactPrefabTests
    {
        private const string PuffPath = "Assets/_Project/Shared/FX/Legacy/FXBulletExplosion.prefab";
        private const string OldMuzzlePath = "Assets/_Project/Shared/FX/Legacy/FXShot.prefab";
        private const string MuzzlePath = "Assets/_Project/Shared/FX/FX Muzzle Flash.prefab";
        private const string ImpactsFolder = "Assets/_Project/Shared/FX/Impacts/";

        // The shop's weapon tree: a gun's family is the gun its upgrade line grows from, the one bought straight off the starting gun
        // (the starting gun is its own family). Read from the data, so adding or renaming a gun needs no edit here.
        private static WeaponDefinition FamilyOf(WeaponDefinition weapon)
        {
            WeaponDefinition family = weapon;
            while (family.Parent != null && family.Parent.Parent != null)
                family = family.Parent;
            return family;
        }

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
            var impactOfFamily = new Dictionary<WeaponDefinition, string>();
            foreach (WeaponDefinition weapon in BulletPrefabGuardTests.Weapons())
            {
                WeaponDefinition family = FamilyOf(weapon);
                string impact = PathOf(weapon.ImpactVfx);
                if (impactOfFamily.TryGetValue(family, out string seen))
                    Assert.AreEqual(seen, impact, weapon.name + " lands like the rest of the " + family.name + " family");
                else
                    impactOfFamily[family] = impact;
            }
            Assert.AreEqual(impactOfFamily.Count, new HashSet<string>(impactOfFamily.Values).Count, "no two families share an impact");
        }

        [Test]
        public void TheStunAndZipGunsKeepTheirOwnImpacts()
        {
            Assert.AreEqual("Assets/_Project/Shared/FX/Impacts/FX Stun Burst.prefab",
                FallbackOf(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Weapons/Projectiles/Stun Gun Bullet.prefab")));
            const string zipHit = "Assets/_Project/Shared/FX/Impacts/FX Zip Hit.prefab";
            Assert.AreEqual(zipHit, FallbackOf(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Weapons/Projectiles/Zip Gun Bullet.prefab")),
                "the Zip Gun's hit is its own silver burst, not the old yellow-to-red puff (yellow reads as the Shield's gold)");
            var tint = AssetDatabase.LoadAssetAtPath<GameObject>(zipHit).GetComponent<LookTint>();
            Assert.IsNotNull(tint, "coloured by the look table");
            Assert.AreEqual(18, new SerializedObject(tint).FindProperty("abilityId").intValue, "from the Zip Gun's row (silver, movement)");
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
