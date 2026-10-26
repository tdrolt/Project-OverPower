using UnityEngine;
using Overpower.Weapons;
using Overpower.Match;
using Overpower.Vision;

namespace Overpower.Abilities
{
    /// <summary>
    /// Fires a large, slow bolt that stuns the first enemy it touches. Pure utility like the zip gun: zero damage, and
    /// this module owns neither the flight nor the status. ProjectileMotor already flies through teammates and stops at
    /// the first enemy or wall (FriendlyFire.IsSelfOrTeammate); ApplyStatusOnHit on the projectile prefab turns that
    /// stop into a stun.
    /// EVERY CLIENT SPAWNS THE SAME LOCAL PROJECTILE AND REACTS TO ITS OWN HIT, unlike the zip gun: a stun must land on
    /// the VICTIM's machine (status effects are owner-only, see IStatusReceiver), so there is no caster-only copy, no
    /// ProjectileContext.OnAbilityHit and no AbilityHitRelay. Every client's copy finds the same victim and calls
    /// ApplyStatus, and the victim's owner-guard makes exactly one call count.
    /// No aim-cone spread: it fires straight down ctx.AimDirection, like the zip gun.
    /// </summary>
    public sealed class StunGunAbility : AbilityModule
    {
        // Not a design tunable: the stun gun is pure utility, like ZipGunAbility's NoDamage.
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
            // D2: an enemy's Stun Gun bolt is drawn only while inside my team's sight (own team's always).
            VisibleWhenSeen.Attach(projectile, cast.CasterTeam);
        }

        public override string ShopStatsText()
        {
            ApplyStatusOnHit status = projectilePrefab != null ? projectilePrefab.GetComponent<ApplyStatusOnHit>() : null;
            return ShopNumberFormat.Lines($"Range {ShopNumberFormat.Compact(range)}m",
                status != null ? $"Stuns for {ShopNumberFormat.Compact(status.Duration)}s" : "");
        }
    }
}
