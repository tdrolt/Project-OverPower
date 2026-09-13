using UnityEngine;
using Overpower.Weapons;

namespace Overpower.Abilities
{
    /// <summary>
    /// Fires a large, slow, 8m bolt that stuns the first enemy it touches for 2.5s - Tudor's
    /// Equipment spec. Pure utility, exactly like the zip gun (Task 1.7b): zero damage, and this
    /// module owns none of the flight or the status itself. ProjectileMotor already "flies through
    /// teammates, stops at the first enemy or a wall" for free (FriendlyFire.IsSelfOrTeammate, the
    /// same rule every weapon's bullet uses), so "stuns the FIRST enemy hit" needs no code here at
    /// all - it falls out of the motor's own sweep. ApplyStatusOnHit (new, Weapons/Effects) is the one
    /// behaviour that turns that stop into a stun, carried on this ability's own projectile prefab.
    ///
    /// EVERY CLIENT SPAWNS THE SAME LOCAL PROJECTILE AND REACTS TO ITS OWN HIT - unlike the zip gun,
    /// nothing here is caster-only. A stun must land on the VICTIM's own machine (status effects are
    /// owner-only - see IStatusReceiver's class comment), so there is no "only the caster's copy acts"
    /// split, no ProjectileContext.OnAbilityHit callback and no AbilityHitRelay: every client's copy
    /// of this shot independently finds the same victim and calls ApplyStatus on it, and that
    /// victim's own owner-guard is what makes exactly one of those calls actually do anything.
    ///
    /// No aim-cone spread - Controller's call, matching the addendum: this fires straight down
    /// ctx.AimDirection, the same "no accuracy penalty on a utility shot" choice the zip gun already
    /// made.
    /// </summary>
    public sealed class StunGunAbility : AbilityModule
    {
        // Not a design tunable: Tudor's spec is explicit that the stun gun deals no direct damage at
        // all - it is pure utility, the identical reasoning behind ZipGunAbility's own NoDamage.
        private const float NoDamage = 0f;

        [Header("Projectile")]
        [SerializeField, Tooltip("How far the bolt can travel before it gives up, in metres. Tudor's spec: 8m.")]
        private float range = 8f;

        [SerializeField, Tooltip("How fast the bolt flies, in metres per second. Controller's call: " +
                 "medium speed, so dodging it is real counterplay rather than a guaranteed stun.")]
        private float projectileSpeed = 16f;

        [SerializeField, Tooltip("Radius in metres of the bolt's own hit-detection sphere. Tudor's " +
                 "spec calls this a 'large' projectile - noticeably bigger than a weapon's own bullet.")]
        private float projectileRadius = 0.45f;

        [SerializeField, Tooltip("The projectile this ability fires - a ProjectileMotor plus " +
                 "ApplyStatusOnHit, no WeaponDefinition involved. Lives in Assets/Gameplay/Projectiles " +
                 "next to every other bullet.")]
        private GameObject projectilePrefab;

        public override void OnEquip()
        {
            if (projectilePrefab == null)
                Debug.LogError($"[StunGunAbility] {name}: Projectile Prefab is not assigned - the stun gun fires nothing.");
            else if (projectilePrefab.GetComponent<ProjectileMotor>() == null)
                Debug.LogError($"[StunGunAbility] {name}: Projectile Prefab '{projectilePrefab.name}' has no ProjectileMotor.");
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            range = Mathf.Max(0.1f, range);
            projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
            projectileRadius = Mathf.Max(0.01f, projectileRadius);
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            if (projectilePrefab == null)
                return false; // OnEquip already logged why.

            payload = new CastPayload { Origin = ctx.Muzzle, Direction = ctx.AimDirection };
            return true;
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0)
                return;

            var shot = new ProjectileContext(Definition.Id, projectileSpeed, projectileRadius, range,
                                             NoDamage, cast.CasterActor, cast.CasterTeam,
                                             cast.Payload.Direction, cast.Payload.Origin);

            GameObject projectile = Instantiate(projectilePrefab, cast.Payload.Origin,
                                                Quaternion.LookRotation(cast.Payload.Direction));
            ProjectileMotor motor = projectile.GetComponent<ProjectileMotor>();
            if (motor == null)
            {
                Debug.LogError($"[StunGunAbility] {name}: projectile prefab '{projectilePrefab.name}' " +
                                "has no ProjectileMotor - destroying it rather than leaking it.");
                Destroy(projectile);
                return;
            }

            motor.Initialize(shot);
        }
    }
}
