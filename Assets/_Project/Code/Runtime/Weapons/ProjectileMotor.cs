using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Vision;

namespace Overpower.Weapons
{
    /// <summary>
    /// Moves one projectile by SWEEPING it forward each frame: no Rigidbody, no collider, Physics.SphereCast does both the moving and the hitting.
    /// A Discrete-collision Rigidbody only asks "am I overlapping anything?" once per physics step, so a bullet this small and fast (0.10m radius at
    /// 55 m/s covers ~0.92m a frame) appears on the far side of walls and players; a swept cast cannot tunnel however fast it goes.
    /// HIT DETECTION IS VICTIM-SIDE. Every client simulates every projectile and calls ApplyDamage on what it hits; only the victim's own PlayerHealth
    /// acts on it (ApplyDamage returns unless photonView.IsMine), so no damage RPC is needed. This deliberately favours the DEFENDER: if it looked like
    /// a hit on your screen it was one, and a high-ping shooter sometimes sees a hit that does not register. Shooter-side detection with lag
    /// compensation (what most commercial shooters do) favours the attacker; worth revisiting after a playtest with real latency.
    /// Walls stop the projectile on every client independently, safe because all clients simulate from the same origin, direction and seed.
    /// Pierce, explode and bounce are IProjectileBehaviour components on their own projectile prefab, never edits to this file.
    /// </summary>
    public class ProjectileMotor : MonoBehaviour
    {
        [SerializeField, Tooltip("Which layers this projectile may hit. Building and Default cover " +
                 "level geometry and players (a living player sits on Default). Bullet, DeadPlayer and " +
                 "Barrier are stripped in code whatever you tick here - a shot always passes a jersey " +
                 "barrier (GDD p.29) - see BuildMask.")]
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

        // Not a design tunable: how far (metres) a bounce lifts the projectile off the wall along its normal before the next sweep - see Redirect.
        // 0.02m is small enough that nobody sees a bounced shot pop sideways, and comfortably larger than the float-precision band (measured at
        // 65-120m from the origin) that caused the false overlap.
        private const float BounceSurfaceSkin = 0.02f;

        // Not a design tunable: how close to zero a sweep's distance must be to count as "this sweep started already touching the collider".
        // Unity reports an initial overlap at exactly 0; this only absorbs float noise, the same reasoning RangeBudget.ReachedTolerance uses for its zero.
        private const float RestingOverlapDistance = 0.0001f;

        // Not a design tunable: how close two directions must be to "exactly opposite" to match Unity's resting-overlap signature (RaycastHit.normal is
        // the sweep direction reversed for an initial overlap). cos(5 degrees) leaves headroom around the bit-for-bit opposite seen in the investigation's
        // log. This threshold does NOT tell the artifact from a genuine head-on hit (a real head-on hit on a DIFFERENT wall gives the same "nearly
        // opposite" normal; a graze never comes near opposite whichever collider it hits) - IsRestingOverlapOnLastBounce's other two conditions do:
        // the SAME collider this projectile most recently bounced off, and essentially zero distance travelled this sweep.
        private const float OppositeDirectionDot = -0.996f;

        private ProjectileContext context;
        private RangeBudget range;
        private IProjectileBehaviour[] behaviours;

        private Vector3 direction;
        private float speed;
        private float radius;
        private int mask;
        private float ageSeconds;
        private bool initialised;

        // The collider a bounce most recently redirected this projectile away from; null before any bounce and again from the first sweep that no longer
        // sees the resting-overlap artifact (TrySweep clears it), so the exemption covers only the very next sweep(s). Used only by
        // IsRestingOverlapOnLastBounce, never for any other collider: a shot that starts inside a wall it never bounced off, or hits a second wall at a
        // corner, must still register.
        private Collider justBouncedOffCollider;

        /// <summary>True until this projectile has been told to go away. Flips at the very start of Despawn, before OnExpired and the Destroy, so a
        /// caller driving Step() directly (an edit-mode test; Update never needs this) can stop instead of ticking a finished projectile.</summary>
        public bool IsAlive { get; private set; } = true;

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
            speed = shot.ProjectileSpeed;
            radius = shot.ProjectileRadius;
            range = new RangeBudget(shot.MaxRange);
            mask = BuildMask();
            transform.forward = direction;
            initialised = true;

            for (int i = 0; i < behaviours.Length; i++)
                behaviours[i].OnSpawned(this, context);
        }

        /// <summary>
        /// Turns the projectile after a bounce and lifts it off the surface it was resting against - the seam BounceOffWalls uses; hitNormal and
        /// hitCollider are the RaycastHit the bounce just resolved.
        /// WALL-RATTLE TRAP: a sphere left exactly touching the wall reads that resting touch as a fresh hit on the very next sweep (Unity reports an
        /// initial overlap as distance 0, point zero, normal = sweep direction REVERSED). BounceOffWalls then reflected about the reversed normal,
        /// re-reversing the direction, and the bullet rattled in place until its bounce budget ran out - worse 65-120m from the origin, where float
        /// precision is looser. Two fixes, both scoped to the collider just bounced off: the lift by BounceSurfaceSkin along hitNormal, and
        /// justBouncedOffCollider, which TrySweep's IsRestingOverlapOnLastBounce uses to refuse that collider's resting-touch signature (belt and
        /// braces: the lift is a distance, not a guarantee, at that precision).
        /// </summary>
        public void Redirect(Vector3 newDirection, Vector3 hitNormal, Collider hitCollider)
        {
            direction = newDirection.normalized;
            transform.forward = direction;
            transform.position += hitNormal * BounceSurfaceSkin;
            justBouncedOffCollider = hitCollider;
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

            // gameObject.scene's PhysicsScene IS Physics.defaultPhysicsScene for every projectile in real play (a normal Instantiate lands in the loaded
            // Game Scene, never a preview scene). It is the seam an edit-mode test uses to call Step() against an isolated preview scene's own
            // PhysicsScene, which the global Physics.SphereCastNonAlloc could never see (the same reason FireField.OverlapBurnZone and
            // GroundSnap.TryFindGroundY take an explicit PhysicsScene).
            Step(Time.deltaTime, gameObject.scene.GetPhysicsScene());
        }

        /// <summary>One frame of flight, taking deltaTime and the PhysicsScene to sweep against as parameters instead of reading Time.deltaTime and the
        /// static Physics class, so an edit-mode test can drive a real motor in an isolated preview scene (BounceOffWallsRattleTests). See Update.</summary>
        public void Step(float deltaTime, PhysicsScene physicsScene)
        {
            if (!initialised || !IsAlive)
                return;

            ageSeconds += deltaTime;
            if (ageSeconds >= maxLifetimeSeconds)
            {
                Despawn(false, transform.position);
                return;
            }

            float step = range.Consume(speed * deltaTime);
            if (step <= 0f)
            {
                Despawn(false, transform.position);
                return;
            }

            if (TrySweep(physicsScene, step, out RaycastHit hit))
            {
                // Stop where the sphere first touches, not where its centre would have ended up.
                transform.position += direction * hit.distance;

                if (ResolveHit(hit) == ProjectileHitResponse.Despawn)
                {
                    Despawn(true, hit.point);
                    return;
                }

                // A behaviour kept it alive, so it resumes from the contact point next frame - but Consume above charged this WHOLE step though only
                // hit.distance was moved. Refund the difference, or a bounced/pierced shot's total reach would depend on the frame's step size (a lower
                // frame rate forfeits more per hit).
                range.Refund(step - hit.distance);
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
        private bool TrySweep(PhysicsScene physicsScene, float step, out RaycastHit nearest)
        {
            nearest = default;

            int count = physicsScene.SphereCast(transform.position, radius, direction, HitBuffer,
                                                 step, mask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float best = float.MaxValue;

            bool restingArtifactStillPresent = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = HitBuffer[i];
                Collider collider = candidate.collider;
                if (collider == null || ignored.Contains(collider))
                    continue;

                if (IsRestingOverlapOnLastBounce(candidate, collider))
                {
                    restingArtifactStillPresent = true;
                    continue; // The wall we just bounced off, reporting the resting touch as a new hit - not real.
                }

                if (FliesThrough(collider))
                {
                    ignored.Add(collider); // Remembered, so later steps skip the lookup.
                    continue;
                }

                if (candidate.distance < best)
                {
                    best = candidate.distance;
                    nearest = FixOriginHitPoint(candidate, collider);
                    found = true;
                }
            }

            // The guard exempts only the sweep right after a bounce: a much later sweep that reproduces the same signature against the SAME collider
            // must register as a genuine hit. The first sweep where the artifact is no longer seen is when it is safe to disarm.
            if (justBouncedOffCollider != null && !restingArtifactStillPresent)
                justBouncedOffCollider = null;

            return found;
        }

        /// <summary>
        /// True when candidate is Unity's signature for "the sphere already overlapped this collider at the start of the sweep" (distance ~0, normal the
        /// sweep direction reversed) AND collider is the exact one this projectile most recently bounced off - see Redirect and justBouncedOffCollider.
        /// Deliberately NOT "every initial overlap": a projectile that starts touching or inside a wall it never bounced off - a shot fired flush against
        /// one (SafeMuzzlePosition pulls the MUZZLE back off a wall, but a hand-placed or ability projectile gets no such guarantee), or the second wall
        /// of an inside corner - must still register that hit normally, which is what leaving collider != justBouncedOffCollider unfiltered achieves.
        /// </summary>
        private bool IsRestingOverlapOnLastBounce(RaycastHit candidate, Collider collider)
        {
            if (collider != justBouncedOffCollider)
                return false;

            if (candidate.distance > RestingOverlapDistance)
                return false;

            return Vector3.Dot(candidate.normal, direction) < OppositeDirectionDot;
        }

        /// <summary>
        /// Unity reports a sweep that STARTS already overlapping a collider (an enemy at the muzzle, or stepping into a projectile between frames) as
        /// hit.point == Vector3.zero and hit.distance == 0, whatever the collider's real position. This is the signature IsRestingOverlapOnLastBounce
        /// reads, but here the collider is not the one just bounced off, so the hit is real and must be reported, just not at the origin. Left unfixed,
        /// (0,0,0) becomes the impact VFX/SFX position (Despawn), the damage marker (DamageInfo.HitPoint), a rocket's splash centre
        /// (ExplodeOnImpact.Detonate) and the zip gun's pull target (ZipGunAbility.HandleZipHit) - all snapping to the world origin.
        /// collider.ClosestPoint(transform.position), not transform.position itself: it reports the collider's surface, which is what hit.point means
        /// for a normal sweep, whereas transform.position could sit anywhere inside a large collider's volume.
        /// </summary>
        /// </summary>
        private RaycastHit FixOriginHitPoint(RaycastHit hit, Collider collider)
        {
            if (hit.distance <= RestingOverlapDistance && hit.point == Vector3.zero)
                hit.point = collider.ClosestPoint(transform.position);

            return hit;
        }

        /// <summary>You cannot shoot yourself and you cannot shoot a teammate; in both cases the
        /// shot carries on rather than stopping, so a teammate crossing your line of fire is not a
        /// shield. Delegates to FriendlyFire.IsSelfOrTeammate, shared with ExplodeOnImpact and
        /// BeamResolver - see that method for the fail-open rule.</summary>
        private bool FliesThrough(Collider collider)
        {
            IDamageable target = collider.GetComponentInParent<IDamageable>();
            if (target == null)
                return false; // Level geometry. Stops the shot.

            return FriendlyFire.IsSelfOrTeammate(context.ShooterActorNumber, target.ActorNumber,
                                                  context.ShooterTeamId, target.TeamId);
        }

        /// <summary>Deals the damage, then asks the attached behaviours what to do next. Damage is applied on every client and only the victim's own
        /// component acts on it (victim-side, see the class comment). A pure-utility ability shot (the zip gun, damage 0) skips ApplyDamage entirely
        /// rather than calling it for zero.</summary>
        private ProjectileHitResponse ResolveHit(RaycastHit hit)
        {
            IDamageable victim = hit.collider.GetComponentInParent<IDamageable>();

            if (victim != null && context.Damage > 0f)
            {
                // WeaponId is only ever a real weapon's id (-1 for an ability shot) and AbilityId only a real ability's (-1 for a weapon shot): mutually
                // exclusive, like every other DamageInfo site. context.SourceId would set both to the same value for an ability shot.
                int weaponId = context.Weapon != null ? context.Weapon.Id : -1;
                int abilityId = context.Weapon == null ? context.AbilityId : -1;
                victim.ApplyDamage(new DamageInfo(context.Damage, context.ShooterActorNumber,
                                                   context.ShooterTeamId, weaponId,
                                                   DamageSource.Projectile, false, hit.point, abilityId));
                // A weapon with Reveal On Hit Seconds shows the enemy it hit to the shooter's team (every client runs this shot).
                if (context.Weapon != null)
                    TeamSight.RevealOnHit(context.Weapon, victim, context.ShooterTeamId);
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
            IsAlive = false;

            if (onImpact)
            {
                // context.Weapon is null for an ability shot - the fallback below is then the only impact VFX this projectile can show, exactly as if a
                // weapon had none of its own set.
                // had none of its own set.
                GameObject vfx = context.Weapon != null && context.Weapon.ImpactVfx != null
                    ? context.Weapon.ImpactVfx : fallbackImpactVfx;
                // An enemy shot's impact flash in the fog is not shown (it would give away where it landed); the sound below is untouched. An exploding
                // projectile's flash follows the blast rule, like its Splash Shell (so it also shows when the blast reaches my team). (D2)
                ExplodeOnImpact blast = GetComponent<ExplodeOnImpact>();
                bool flashShown = blast != null
                    ? TeamSight.BlastShownAt(context.ShooterTeamId, at, blast.SplashRadius)
                    : TeamSight.ShotShownAt(context.ShooterTeamId, at);
                if (vfx != null && VFXManager.Instance != null && flashShown)
                    VFXManager.Instance.PlayVFX(vfx, at);

                if (impactSfx != null && AudioManager.Instance != null)
                    AudioManager.Instance.Play3D(impactSfx, at);
            }

            for (int i = 0; i < behaviours.Length; i++)
                behaviours[i].OnExpired(this, context);

            // Destroy is what real play always uses (Application.isPlaying is true in every Play Mode session and build). The DestroyImmediate branch
            // exists only so BounceOffWallsRattleTests can drive a real ProjectileMotor to a genuine despawn from an edit-mode test: Object.Destroy
            // logs "Destroy may not be called from edit mode!", which the edit-mode test runner treats as a failure.
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        /// <summary>The designer's layer choices, minus the three invariants HitMasks enforces for
        /// every shot - see HitMasks.StripNonNegotiableLayers.</summary>
        private int BuildMask()
        {
            return HitMasks.StripNonNegotiableLayers(hitMask);
        }
    }
}
