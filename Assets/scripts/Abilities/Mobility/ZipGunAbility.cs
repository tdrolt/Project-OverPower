using UnityEngine;
using Overpower.Combat;
using Overpower.Weapons;

namespace Overpower.Abilities
{
    /// <summary>
    /// Fires a bolt up to 15m; hitting a wall or a player pulls the SHOOTER toward wherever it
    /// landed - Tudor's spec. Not a weapon and not a knockback: nothing else is ever touched by it,
    /// and the pull always travels through PlayerDisplacement as a Voluntary move, so it automatically
    /// loses to a real knockback in flight and automatically cancels on death the same way a dash does.
    ///
    /// THE FIRST ABILITY TO FIRE A PROJECTILE, which is why Task 1.7b generalised ProjectileContext
    /// and ProjectileMotor first (see that file's class comment) rather than duplicating the sweep
    /// here. This module owns none of the flight - it hands ProjectileMotor a context built from its
    /// own Inspector fields instead of a WeaponDefinition, and gets told about a hit through
    /// AbilityHitRelay, the IProjectileBehaviour on the projectile prefab.
    ///
    /// EVERY CLIENT SPAWNS THE SAME LOCAL PROJECTILE, exactly like a weapon's own RPC_FireWeapon -
    /// so a bystander's screen shows the bolt leave the muzzle and stop where it hits. Only the
    /// CASTER's own copy is built with a hit callback (see FireProjectile), which is what makes
    /// "only the shooter gets pulled" true without this class or the motor ever asking "am I the
    /// caster" - the answer already lives in which context each machine built.
    ///
    /// "Stops at walls and enemies, flies through teammates" needs no code here at all: that is
    /// ProjectileMotor's own FliesThrough rule (FriendlyFire.IsSelfOrTeammate), the same one every
    /// weapon already gets for free.
    /// </summary>
    public sealed class ZipGunAbility : AbilityModule
    {
        // Phase numbers this module defines. 0 is always the cast itself (firing the bolt).
        private const byte PhaseTether = 1;

        // Not a design tunable: Tudor's spec is explicit that the zip gun deals no direct damage at
        // all - it is pure utility. A field here would just be one more number a designer could
        // accidentally un-zero and turn into a free hitscan gun.
        private const float NoDamage = 0f;

        [Header("Projectile")]
        [SerializeField, Tooltip("How far the bolt can travel before it gives up, in metres. " +
                 "Tudor's spec: 15m.")]
        private float range = 15f;

        [SerializeField, Tooltip("How fast the bolt itself flies, in metres per second - not the " +
                 "pull that follows a hit. Faster reads as more responsive but gives less time to " +
                 "see it coming.")]
        private float projectileSpeed = 40f;

        [SerializeField, Tooltip("Radius in metres of the bolt's own hit-detection sphere - the " +
                 "same idea as a weapon's Projectile Radius.")]
        private float projectileRadius = 0.15f;

        [SerializeField, Tooltip("The projectile this ability fires - a ProjectileMotor plus " +
                 "AbilityHitRelay, no WeaponDefinition involved. Lives in Assets/Gameplay/Projectiles " +
                 "next to every other bullet.")]
        private GameObject projectilePrefab;

        [Header("Pull")]
        [SerializeField, Tooltip("How fast you travel toward wherever the bolt hit, in metres per " +
                 "second. The DISTANCE is whatever ground is left between you and the impact point, " +
                 "not a fixed number - a close hit pulls you a short way and a far one pulls you " +
                 "further, both at this same speed.")]
        private float pullSpeed = 25f;

        [Header("Remote tether (cosmetic only)")]
        [SerializeField, Tooltip("Radius in metres of the cheap marker shown at the impact point on " +
                 "every OTHER client's screen when your bolt lands a hit, so the shot reads as " +
                 "landing somewhere real even though only your own screen shows the actual pull. 0 " +
                 "shows nothing.")]
        private float tetherMarkerRadius = 0.35f;

        [SerializeField, Tooltip("Seconds the impact marker stays up before it disappears on its own.")]
        private float tetherMarkerSeconds = 0.3f;

        // Owner only: the player's own capsule, read once so the pull can stop the requested
        // distance short of the impact point instead of driving the player's centre straight into
        // whatever it hit - the same field BlinkAbility caches for its own destination check.
        private CapsuleCollider capsule;

        // Owner only: which move is currently in flight, so a Stunned/Died/Unequipped interrupt
        // cancels THIS ability's own pull and never someone else's Forced move that happens to be
        // running at the same moment - see PlayerDisplacement's priority rules, and DashAbility's
        // identical reasoning.
        private bool pulling;

        public override bool IsActive => pulling;

        public override void OnEquip()
        {
            capsule = Owner.Root.GetComponent<CapsuleCollider>();
            if (capsule == null)
                Debug.LogError($"[ZipGunAbility] {name}: the player has no CapsuleCollider - the pull " +
                                "cannot stop short of the impact point.");

            if (projectilePrefab == null)
                Debug.LogError($"[ZipGunAbility] {name}: Projectile Prefab is not assigned - the zip gun fires nothing.");
            else if (projectilePrefab.GetComponent<ProjectileMotor>() == null)
                Debug.LogError($"[ZipGunAbility] {name}: Projectile Prefab '{projectilePrefab.name}' has no ProjectileMotor.");

            // Tudor's spec: a takedown (kill OR assist) refills the charge immediately instead of
            // waiting out the rest of the 15s cooldown. CombatEvents.LocalTakedown fires only on the
            // machine that actually earned it (see that class's own comment) - HandleLocalTakedown
            // still re-checks Owner.IsMine because this same module class is instantiated once per
            // player who has the zip gun equipped, including on OTHER clients purely to visualise
            // their loadout, and RefillCharges is documented "owner only".
            CombatEvents.LocalTakedown += HandleLocalTakedown;
        }

        private void OnDestroy()
        {
            CombatEvents.LocalTakedown -= HandleLocalTakedown;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            range = Mathf.Max(0.1f, range);
            projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
            projectileRadius = Mathf.Max(0.01f, projectileRadius);
            pullSpeed = Mathf.Max(0.1f, pullSpeed);
            tetherMarkerRadius = Mathf.Max(0f, tetherMarkerRadius);
            tetherMarkerSeconds = Mathf.Max(0f, tetherMarkerSeconds);
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

        public override void Interrupt(InterruptReason reason)
        {
            if (reason != InterruptReason.Stunned && reason != InterruptReason.Died && reason != InterruptReason.Unequipped)
                return; // Silenced does not stop this - it is not a weapon and spends no heat, matching Dash.

            // Guarded on pulling rather than cancelling unconditionally: if a Forced move
            // (knockback) had already pre-empted this pull, or PlayerDisplacement's own death
            // handling had already cancelled it, pulling would already be false - see
            // PlayerDisplacement's priority rules and DashAbility's identical guard.
            if (!pulling)
                return;

            (Owner.Displacement as PlayerDisplacement)?.Cancel();
        }

        private void HandleLocalTakedown(bool isKill)
        {
            if (Owner.IsMine)
                RefillCharges();
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            switch (cast.Phase)
            {
                case 0:
                    FireProjectile(cast);
                    return;

                case PhaseTether:
                    // The caster's own screen already shows the real pull; this marker is only for
                    // everyone else, exactly like TeleportAbility's arrival VFX.
                    if (!cast.IsCasterClient)
                        PlayTetherMarker(cast.Payload.Point);
                    return;
            }
        }

        /// <summary>Runs on every client. Builds this shot's own local projectile - see the class
        /// comment for why only the caster's copy is handed a hit callback.</summary>
        private void FireProjectile(in CastEvent cast)
        {
            System.Action<ProjectileHitInfo> onHit = null;
            if (cast.IsCasterClient)
                onHit = HandleZipHit;

            var shot = new ProjectileContext(Definition.Id, projectileSpeed, projectileRadius, range,
                                             NoDamage, cast.CasterActor, cast.CasterTeam,
                                             cast.Payload.Direction, cast.Payload.Origin, onHit);

            GameObject projectile = Instantiate(projectilePrefab, cast.Payload.Origin,
                                                Quaternion.LookRotation(cast.Payload.Direction));
            ProjectileMotor motor = projectile.GetComponent<ProjectileMotor>();
            if (motor == null)
            {
                Debug.LogError($"[ZipGunAbility] {name}: projectile prefab '{projectilePrefab.name}' " +
                                "has no ProjectileMotor - destroying it rather than leaking it.");
                Destroy(projectile);
                return;
            }

            motor.Initialize(shot);
        }

        /// <summary>Caster only, called by AbilityHitRelay through the context it was built with -
        /// never reached on any other client's copy of this shot.</summary>
        private void HandleZipHit(ProjectileHitInfo hit)
        {
            // Belt-and-braces, matching PlayerDisplacement's own habit of re-checking IsMine even
            // where a caller is already expected to be the owner - see the class comment for why
            // this should already be guaranteed true whenever AbilityHitRelay reaches here.
            if (!Owner.IsMine || capsule == null)
                return;

            var displacement = Owner.Displacement as PlayerDisplacement;
            if (displacement == null)
                return;

            Vector3 toHit = hit.Point - Owner.Root.transform.position;
            toHit.y = 0f; // The pull is a ground-plane shove, like every other displacement.
            float distance = toHit.magnitude;

            if (distance <= capsule.radius)
                return; // Point-blank hit - already standing where the pull would end.

            Vector3 direction = toHit / distance;
            float travelDistance = distance - capsule.radius;

            pulling = true;
            bool started = displacement.DisplaceVoluntary(direction, travelDistance, pullSpeed, HandlePullEnd);
            if (!started)
            {
                // A knockback beat the pull to the mover in the single frame between the bolt
                // landing and here - the charge is already spent, same accepted race as every other
                // ability's own comment on this (see TeleportAbility.CompleteTravel).
                pulling = false;
                return;
            }

            // Sent from here, not from FireProjectile, so it carries where the shot ACTUALLY landed
            // rather than where it was aimed - Tudor's addendum note on why remote clients must draw
            // the tether at the real impact point.
            SendPhase(PhaseTether, new CastPayload { Point = hit.Point });
            LogZip(hit.Point, travelDistance);
        }

        private void HandlePullEnd(DisplaceEnd end)
        {
            pulling = false;
        }

        private void PlayTetherMarker(Vector3 point)
        {
            if (tetherMarkerRadius <= 0f)
                return;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Zip Tether VFX (cheap, cosmetic only)";
            // Removed immediately, not with Destroy, which waits for the end of the frame - see
            // TeleportAbility.SpawnMarker's identical trick: for that one frame the sphere would
            // otherwise be a solid object sitting in the world.
            DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.position = point;
            marker.transform.localScale = Vector3.one * tetherMarkerRadius * 2f;
            Destroy(marker, tetherMarkerSeconds);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogZip(Vector3 hitPoint, float travelDistance)
        {
            Debug.Log($"[ZIP] hit={hitPoint} pullDistance={travelDistance:F2}");
        }
    }
}
