using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Moves one projectile by SWEEPING it forward each frame. There is no Rigidbody and no
    /// collider on a projectile at all - Physics.SphereCast does both the moving and the hitting.
    ///
    /// The bullet this replaces was a Discrete-collision Rigidbody, and the design wants bullets
    /// far smaller and faster: the baseline is 0.10m radius at 55 m/s, which covers about 0.92m per
    /// frame at 60fps against a 0.20m diameter. Discrete collision only asks "am I overlapping
    /// anything?" once per physics step, so a bullet that small at that speed appears on the far
    /// side of walls and players. A swept cast asks "did anything get in the way along the whole
    /// path?", which cannot tunnel however fast the projectile goes.
    ///
    /// HIT DETECTION IS VICTIM-SIDE. Every client simulates every projectile and calls ApplyDamage
    /// on whatever it hits; only the victim's own PlayerHealth acts on it, because ApplyDamage
    /// returns immediately unless photonView.IsMine. The player who got shot is the one who decides
    /// they got shot, so no damage RPC is needed and the health bar cannot fight itself.
    ///
    /// The tradeoff, deliberately taken: victim-side detection favours the DEFENDER. If it looked
    /// like it hit you on your screen, it did. A high-ping shooter will sometimes see a hit that
    /// does not register. The alternative - shooter-side detection with lag compensation - favours
    /// the attacker and is what most commercial shooters do. This is a deliberate choice and worth
    /// revisiting after a playtest with real latency.
    ///
    /// Walls stop the projectile on every client independently, which is safe because all clients
    /// simulate from the same origin, direction and seed.
    ///
    /// Pierce, explode and bounce are added as IProjectileBehaviour components on their own
    /// projectile prefab - never by editing this file. See IProjectileBehaviour.
    /// </summary>
    public class ProjectileMotor : MonoBehaviour
    {
        [SerializeField, Tooltip("Which layers this projectile may hit. Building and Default cover " +
                 "level geometry and players (a living player sits on Default). Bullet and " +
                 "DeadPlayer are stripped in code whatever you tick here - see BuildMask.")]
        private LayerMask hitMask = ~0;

        [SerializeField, Tooltip("Hard cap in seconds on how long a projectile may exist, whatever " +
                 "else happens. Max Range is what normally ends a shot; this is the backstop so a " +
                 "mistake in the range maths can never leak a permanent object the way the old " +
                 "bullets did. Keep it well above Max Range divided by Projectile Speed - 30m at " +
                 "55 m/s takes 0.55s, so 5 seconds is generous.")]
        private float maxLifetimeSeconds = 5f;

        [SerializeField, Tooltip("Effect spawned where this projectile lands, used only when the " +
                 "weapon that fired it has no Impact Vfx of its own.")]
        private GameObject fallbackImpactVfx;

        [SerializeField, Tooltip("Sound played where this projectile lands. Leave empty for a " +
                 "silent impact.")]
        private AudioClip impactSfx;

        // Not a tuning value: how many overlapping colliders one sweep step considers. A 0.1m
        // sphere moving under a metre cannot plausibly touch eight things at once, and a fixed
        // shared buffer keeps the sweep allocation-free.
        private const int MaxHitsPerStep = 8;
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[MaxHitsPerStep];

        private ProjectileContext context;
        private RangeBudget range;
        private IProjectileBehaviour[] behaviours;

        private Vector3 direction;
        private float speed;
        private float radius;
        private int mask;
        private float ageSeconds;
        private bool initialised;

        // Everything this projectile flies through: the shooter, teammates, and anything a pierce
        // behaviour waves past.
        private readonly HashSet<Collider> ignored = new HashSet<Collider>();

        public ProjectileContext Context => context;
        public RangeBudget Range => range;
        public Vector3 Direction => direction;

        private void Awake() => behaviours = GetComponents<IProjectileBehaviour>();

        /// <summary>Called by WeaponFiring straight after a local Instantiate - see
        /// ProjectileContext for why assigning to the returned object is right for a local spawn
        /// and wrong for a networked one.</summary>
        public void Initialize(ProjectileContext shot)
        {
            context = shot;
            direction = shot.Direction.normalized;
            speed = shot.Weapon.ProjectileSpeed;
            radius = shot.Weapon.ProjectileRadius;
            range = new RangeBudget(shot.Weapon.MaxRange);
            mask = BuildMask();
            transform.forward = direction;
            initialised = true;

            for (int i = 0; i < behaviours.Length; i++)
                behaviours[i].OnSpawned(this, context);
        }

        /// <summary>Turn the projectile. The seam a bounce behaviour uses; nothing bounces yet.</summary>
        public void Redirect(Vector3 newDirection)
        {
            direction = newDirection.normalized;
            transform.forward = direction;
        }

        /// <summary>Fly through this collider from now on. The seam a pierce behaviour uses.</summary>
        public void Ignore(Collider collider)
        {
            if (collider != null)
                ignored.Add(collider);
        }

        /// An uninitialised projectile would sit in the scene forever, which is the exact failure
        /// this system removes. Start runs after the same-frame Initialize, so reaching here
        /// uninitialised is a real wiring mistake.
        private void Start()
        {
            if (initialised)
                return;

            Debug.LogError($"[ProjectileMotor] {name} was spawned without Initialize - destroying " +
                            "it rather than leaking it. Spawn projectiles through WeaponFiring.");
            Destroy(gameObject);
        }

        private void Update()
        {
            if (!initialised)
                return;

            ageSeconds += Time.deltaTime;
            if (ageSeconds >= maxLifetimeSeconds)
            {
                Despawn(false, transform.position);
                return;
            }

            float step = range.Consume(speed * Time.deltaTime);
            if (step <= 0f)
            {
                Despawn(false, transform.position);
                return;
            }

            if (TrySweep(step, out RaycastHit hit))
            {
                // Stop where the sphere first touches, not where its centre would have ended up.
                transform.position += direction * hit.distance;

                if (ResolveHit(hit) == ProjectileHitResponse.Despawn)
                {
                    Despawn(true, hit.point);
                    return;
                }

                // A behaviour kept it alive; it resumes from the contact point next frame. The
                // unused remainder of this step is forfeited - at most one frame of travel, and it
                // keeps the range budget honest.
            }
            else
            {
                transform.position += direction * step;
            }

            if (range.IsSpent)
                Despawn(false, transform.position);
        }

        /// <summary>The nearest thing in the way this step, skipping everything this projectile
        /// flies through. Scanning for the nearest ACCEPTABLE hit, rather than taking the single
        /// nearest hit, is what lets a shot pass a teammate standing in front of an enemy.</summary>
        private bool TrySweep(float step, out RaycastHit nearest)
        {
            nearest = default;

            int count = Physics.SphereCastNonAlloc(transform.position, radius, direction, HitBuffer,
                                                    step, mask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float best = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider collider = HitBuffer[i].collider;
                if (collider == null || ignored.Contains(collider))
                    continue;

                if (FliesThrough(collider))
                {
                    ignored.Add(collider); // Remembered, so later steps skip the lookup.
                    continue;
                }

                if (HitBuffer[i].distance < best)
                {
                    best = HitBuffer[i].distance;
                    nearest = HitBuffer[i];
                    found = true;
                }
            }

            return found;
        }

        /// <summary>You cannot shoot yourself and you cannot shoot a teammate; in both cases the
        /// shot carries on rather than stopping, so a teammate crossing your line of fire is not a
        /// shield. Unknown teams fail OPEN and stay valid targets, matching Teams.AreSameTeam.</summary>
        private bool FliesThrough(Collider collider)
        {
            IDamageable target = collider.GetComponentInParent<IDamageable>();
            if (target == null)
                return false; // Level geometry. Stops the shot.

            if (target.ActorNumber == context.ShooterActorNumber)
                return true;

            return context.ShooterTeamId >= 0 && target.TeamId == context.ShooterTeamId;
        }

        /// <summary>Deals the damage, then asks the attached behaviours what to do next. Damage is
        /// applied on every client and only the victim's own component acts on it - see the class
        /// comment on victim-side detection.</summary>
        private ProjectileHitResponse ResolveHit(RaycastHit hit)
        {
            IDamageable victim = hit.collider.GetComponentInParent<IDamageable>();

            if (victim != null)
            {
                victim.ApplyDamage(new DamageInfo(context.Damage, context.ShooterActorNumber,
                                                   context.ShooterTeamId, context.Weapon.Id,
                                                   DamageSource.Projectile, false, hit.point));
            }

            // No behaviours attached is the normal case and means "stop here". Any single
            // behaviour asking to keep flying wins over the others.
            ProjectileHitResponse response = ProjectileHitResponse.Despawn;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i].OnHit(this, context, hit, victim) == ProjectileHitResponse.KeepFlying)
                    response = ProjectileHitResponse.KeepFlying;
            }

            return response;
        }

        private void Despawn(bool onImpact, Vector3 at)
        {
            if (onImpact)
            {
                GameObject vfx = context.Weapon.ImpactVfx != null ? context.Weapon.ImpactVfx : fallbackImpactVfx;
                if (vfx != null && VFXManager.Instance != null)
                    VFXManager.Instance.PlayVFX(vfx, at);

                if (impactSfx != null && AudioManager.Instance != null)
                    AudioManager.Instance.Play3D(impactSfx, at);
            }

            for (int i = 0; i < behaviours.Length; i++)
                behaviours[i].OnExpired(this, context);

            Destroy(gameObject);
        }

        /// <summary>The designer's layer choices, minus two that are never negotiable. Bullet is
        /// stripped because bullets colliding with each other was a fixed playtest bug, DeadPlayer
        /// because a corpse blocking shots was another. Both are correctness invariants rather than
        /// tuning, so they are enforced here instead of trusting the Inspector dropdown.</summary>
        private int BuildMask()
        {
            return hitMask.value & ~LayerBit("Bullet") & ~LayerBit("DeadPlayer");
        }

        private static int LayerBit(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? 1 << layer : 0;
        }
    }
}
