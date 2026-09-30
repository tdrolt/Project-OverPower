using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Overpower.Data;
using Overpower.Weapons;

namespace Overpower.Tests
{
    /// <summary>
    /// Task 1.7b's generalisation: ProjectileContext can be built from a WeaponDefinition (unchanged
    /// behaviour) OR straight from an ability module's own numbers (no WeaponDefinition at all). This
    /// pins the contract ProjectileMotor now relies on - reading ProjectileSpeed/ProjectileRadius/
    /// MaxRange/SourceId off the context itself, never off context.Weapon directly - so a weapon
    /// shot and an ability shot are indistinguishable to the motor from that point on.
    ///
    /// WeaponDefinition is seeded through reflection, matching CatalogueTests' own reasoning: a
    /// test-only accessor on the production class would be worse than a private field name a rename
    /// would need to update here too.
    /// </summary>
    public class ProjectileContextTests
    {
        private WeaponDefinition weapon;

        [TearDown]
        public void DestroyWeapon()
        {
            if (weapon != null)
                Object.DestroyImmediate(weapon);
            weapon = null;
        }

        private WeaponDefinition NewWeapon(int id, float projectileSpeed, float projectileRadius, float maxRange)
        {
            weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            SetPrivate(weapon, "id", id);
            SetPrivate(weapon, "projectileSpeed", projectileSpeed);
            SetPrivate(weapon, "projectileRadius", projectileRadius);
            SetPrivate(weapon, "maxRange", maxRange);
            return weapon;
        }

        private static void SetPrivate(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Expected a private field '{name}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        // ---- a weapon shot behaves exactly as before the generalisation ----

        [Test]
        public void AWeaponShotReadsSpeedRadiusAndRangeFromTheWeapon()
        {
            NewWeapon(id: 3, projectileSpeed: 55f, projectileRadius: 0.1f, maxRange: 30f);
            var shot = new ProjectileContext(weapon, 1, 0, Vector3.forward, Vector3.zero, 0f, 10f);

            Assert.AreEqual(55f, shot.ProjectileSpeed);
            Assert.AreEqual(0.1f, shot.ProjectileRadius);
            Assert.AreEqual(30f, shot.MaxRange);
            Assert.AreSame(weapon, shot.Weapon);
        }

        [Test]
        public void AWeaponShotsSourceIdIsTheWeaponsOwnId()
        {
            NewWeapon(id: 7, projectileSpeed: 55f, projectileRadius: 0.1f, maxRange: 30f);
            var shot = new ProjectileContext(weapon, 1, 0, Vector3.forward, Vector3.zero, 0f, 10f);

            Assert.AreEqual(7, shot.SourceId);
        }

        [Test]
        public void AWeaponShotHasNoAbilityHitCallback()
        {
            NewWeapon(id: 1, projectileSpeed: 55f, projectileRadius: 0.1f, maxRange: 30f);
            var shot = new ProjectileContext(weapon, 1, 0, Vector3.forward, Vector3.zero, 0f, 10f);

            Assert.IsNull(shot.OnAbilityHit);
        }

        // ---- an ability shot (Task 1.7b: the zip gun) needs no WeaponDefinition at all ----

        [Test]
        public void AnAbilityShotReadsSpeedRadiusRangeAndDamageFromItsOwnArguments()
        {
            var shot = new ProjectileContext(abilityId: 18, projectileSpeed: 40f, projectileRadius: 0.15f,
                                              maxRange: 15f, damage: 0f, shooterActorNumber: 1,
                                              shooterTeamId: 0, direction: Vector3.forward, targetPoint: Vector3.zero);

            Assert.IsNull(shot.Weapon);
            Assert.AreEqual(40f, shot.ProjectileSpeed);
            Assert.AreEqual(0.15f, shot.ProjectileRadius);
            Assert.AreEqual(15f, shot.MaxRange);
            Assert.AreEqual(0f, shot.Damage);
        }

        [Test]
        public void AnAbilityShotsSourceIdIsTheAbilityId()
        {
            var shot = new ProjectileContext(abilityId: 18, projectileSpeed: 40f, projectileRadius: 0.15f,
                                              maxRange: 15f, damage: 0f, shooterActorNumber: 1,
                                              shooterTeamId: 0, direction: Vector3.forward, targetPoint: Vector3.zero);

            Assert.AreEqual(18, shot.AbilityId);
            Assert.AreEqual(18, shot.SourceId);
        }

        [Test]
        public void AnAbilityShotsChargeFractionIsAlwaysZero()
        {
            // No ability charges a projectile today - see the constructor's own comment.
            var shot = new ProjectileContext(abilityId: 18, projectileSpeed: 40f, projectileRadius: 0.15f,
                                              maxRange: 15f, damage: 0f, shooterActorNumber: 1,
                                              shooterTeamId: 0, direction: Vector3.forward, targetPoint: Vector3.zero);

            Assert.AreEqual(0f, shot.ChargeFraction);
        }

        [Test]
        public void DamageMultiplierStillScalesAnAbilityShotsDamage()
        {
            // The zip gun deals 0 damage, but the seam a future damaging ability would use must
            // still work identically for an ability shot as it does for a weapon shot.
            var shot = new ProjectileContext(abilityId: 19, projectileSpeed: 16f, projectileRadius: 0.45f,
                                              maxRange: 8f, damage: 20f, shooterActorNumber: 1,
                                              shooterTeamId: 0, direction: Vector3.forward, targetPoint: Vector3.zero);

            shot.SetDamageMultiplier(1.5f);

            Assert.AreEqual(30f, shot.Damage);
        }

        // ---- Task 2.6 review: RangeMultiplier and FireTimeDamageMultiplier ----

        [Test]
        public void RangeMultiplierScalesMaxRangeAndIsReadableOnItsOwn()
        {
            NewWeapon(id: 4, projectileSpeed: 55f, projectileRadius: 0.1f, maxRange: 30f);
            var shot = new ProjectileContext(weapon, 1, 0, Vector3.forward, Vector3.zero, 0f, 10f,
                                              rangeMultiplier: 1.1f);

            Assert.AreEqual(1.1f, shot.RangeMultiplier, 1e-4f);
            Assert.AreEqual(33f, shot.MaxRange, 1e-3f);
        }

        [Test]
        public void DefaultRangeMultiplierLeavesMaxRangeUnchanged()
        {
            NewWeapon(id: 5, projectileSpeed: 55f, projectileRadius: 0.1f, maxRange: 30f);
            var shot = new ProjectileContext(weapon, 1, 0, Vector3.forward, Vector3.zero, 0f, 10f);

            Assert.AreEqual(1f, shot.RangeMultiplier);
            Assert.AreEqual(30f, shot.MaxRange);
        }

        [Test]
        public void FireTimeDamageMultiplierScalesDamageOnceAndIsFixedForTheShotsLife()
        {
            NewWeapon(id: 6, projectileSpeed: 55f, projectileRadius: 0.1f, maxRange: 30f);
            var shot = new ProjectileContext(weapon, 1, 0, Vector3.forward, Vector3.zero, 0f, damage: 10f,
                                              rangeMultiplier: 1f, fireTimeDamageMultiplier: 1.1f);

            Assert.AreEqual(1.1f, shot.FireTimeDamageMultiplier, 1e-4f);
            Assert.AreEqual(11f, shot.Damage, 1e-3f);
        }

        [Test]
        public void FireTimeDamageMultiplierComposesWithDamageMultiplierRatherThanBeingOverwritten()
        {
            // The whole point of keeping this separate from DamageMultiplier (Task 2.6 review): a
            // Bounce/Distance behaviour calling SetDamageMultiplier must not erase a fire-time
            // bonus that was already baked in at construction.
            NewWeapon(id: 7, projectileSpeed: 55f, projectileRadius: 0.1f, maxRange: 30f);
            var shot = new ProjectileContext(weapon, 1, 0, Vector3.forward, Vector3.zero, 0f, damage: 10f,
                                              rangeMultiplier: 1f, fireTimeDamageMultiplier: 1.1f);

            shot.SetDamageMultiplier(2f);

            Assert.AreEqual(22f, shot.Damage, 1e-3f);
        }

        [Test]
        public void DefaultFireTimeDamageMultiplierLeavesDamageUnchanged()
        {
            NewWeapon(id: 8, projectileSpeed: 55f, projectileRadius: 0.1f, maxRange: 30f);
            var shot = new ProjectileContext(weapon, 1, 0, Vector3.forward, Vector3.zero, 0f, 10f);

            Assert.AreEqual(1f, shot.FireTimeDamageMultiplier);
            Assert.AreEqual(10f, shot.Damage);
        }

        [Test]
        public void AnAbilityShotsFireTimeDamageMultiplierIsAlwaysOne()
        {
            var shot = new ProjectileContext(abilityId: 18, projectileSpeed: 40f, projectileRadius: 0.15f,
                                              maxRange: 15f, damage: 20f, shooterActorNumber: 1,
                                              shooterTeamId: 0, direction: Vector3.forward, targetPoint: Vector3.zero);

            Assert.AreEqual(1f, shot.FireTimeDamageMultiplier);
            Assert.AreEqual(20f, shot.Damage);
        }

        // ---- OnAbilityHit: only set on the caster's own copy - see ZipGunAbility.FireProjectile ----

        [Test]
        public void OnAbilityHitIsNullWhenNoCallbackIsGiven()
        {
            var shot = new ProjectileContext(abilityId: 18, projectileSpeed: 40f, projectileRadius: 0.15f,
                                              maxRange: 15f, damage: 0f, shooterActorNumber: 1,
                                              shooterTeamId: 0, direction: Vector3.forward, targetPoint: Vector3.zero);

            Assert.IsNull(shot.OnAbilityHit);
        }

        [Test]
        public void OnAbilityHitFiresWithTheHitInfoItWasHandedWhenACallbackIsGiven()
        {
            ProjectileHitInfo? received = null;
            var expectedPoint = new Vector3(1f, 2f, 3f);

            var shot = new ProjectileContext(abilityId: 18, projectileSpeed: 40f, projectileRadius: 0.15f,
                                              maxRange: 15f, damage: 0f, shooterActorNumber: 1,
                                              shooterTeamId: 0, direction: Vector3.forward, targetPoint: Vector3.zero,
                                              onHit: info => received = info);

            Assert.IsNotNull(shot.OnAbilityHit);

            shot.OnAbilityHit.Invoke(new ProjectileHitInfo(expectedPoint, Vector3.up, null));

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(expectedPoint, received.Value.Point);
            Assert.AreEqual(Vector3.up, received.Value.Normal);
            Assert.IsNull(received.Value.Victim);
        }
    }
}
