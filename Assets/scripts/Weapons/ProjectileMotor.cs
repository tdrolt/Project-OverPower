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
    ///
    /// THE WALL-RATTLE FIX (2026-09-21). Tudor measured a bounced Bounce-gun shot dealing LESS
    /// per trigger pull than a direct one, the opposite of what three bounces at +20% each should
    /// do. The cause: after a bounce this motor left the sphere exactly touching the wall (moved
    /// to hit.distance), and on the VERY NEXT sweep Physics.SphereCast reported that same wall
    /// again - Unity's own documented behaviour for a collider already overlapping the sphere at
    /// the start of a sweep is distance 0, point Vector3.zero, and normal the sweep direction
    /// REVERSED. This motor could not tell that apart from a genuine new wall, so BounceOffWalls
    /// reflected about the reversed normal (exactly re-reversing the direction), the next sweep
    /// repeated the same false overlap with the normal flipped back again, and the bullet
    /// "rattled" in place until its bounce budget ran out on the wall it should have flown away
    /// from - worse at 65-120m from the origin where float precision is looser, which is why an
    /// earlier probe near the world origin failed to reproduce it. Two changes fix it, both
    /// scoped to the collider a bounce just happened against, never to every wall a shot meets:
    /// Redirect (below) lifts the projectile off the surface by BounceSurfaceSkin along the hit
    /// normal, and TrySweep's IsRestingOverlapOnLastBounce refuses to count a same-collider,
    /// zero-distance, reversed-normal result as a new hit. Update was split into Step(deltaTime,
    /// physicsScene) so an edit-mode test could drive a real ProjectileMotor against an isolated
    /// preview scene and reproduce the exact rattle before touching either fix - see
    /// BounceOffWallsRattleTests.
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

        // Not a design tunable: how far, in metres, a bounce lifts the projectile off the wall it
        // just left, along that wall's own normal, before the next sweep. See the class comment's
        // "WALL-RATTLE FIX" for why a sphere left exactly touching a collider needs this at all -
        // Physics.SphereCast reads that resting touch as a brand new overlap on the very next
        // sweep. 0.02m is small enough that nobody watching a bounced shot sees it pop sideways,
        // and comfortably larger than the float-precision band (measured at 65-120m from the
        // origin) that caused the false overlap in the first place.
        private const float BounceSurfaceSkin = 0.02f;

        // Not a design tunable: how close to zero a sweep's reported distance must be to count as
        // "this sweep started already touching the collider" - see BounceSurfaceSkin's comment.
        // Unity reports an initial overlap at exactly 0; this only absorbs float noise around
        // that, the same reasoning RangeBudget.ReachedTolerance uses for its own zero.
        private const float RestingOverlapDistance = 0.0001f;

        // Not a design tunable: how close two directions must be to "exactly opposite" to count as
        // the resting-overlap signature Unity documents (RaycastHit.normal is the sweep direction
        // reversed for an initial overlap) - not an approximation a designer would ever tune.
        // cos(5 degrees) leaves headroom around the bit-for-bit opposite the investigation's own
        // log showed. M4 correction (2026-09-21 review): this threshold is NOT what tells the
        // artifact apart from a genuine head-on hit - a real head-on hit against a DIFFERENT wall
        // produces the exact same "nearly opposite" normal, and a shallow graze produces a normal
        // nowhere near opposite the travel direction regardless of which collider it hits, so
        // grazes were never a risk here at all. What actually tells the artifact apart is
        // IsRestingOverlapOnLastBounce's other two conditions: the SAME collider this projectile
        // most recently bounced off, and essentially zero distance travelled this sweep.
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

        // The collider a bounce most recently redirected this projectile away from, or null
        // before any bounce. Set only by Redirect below; used only by IsRestingOverlapOnLastBounce
        // to recognise ITS OWN resting-touch artifact on the very next sweep, never any other
        // collider - a shot that genuinely starts inside a wall it has never bounced off (or hits
        // a second wall at a corner) must still register normally. See the class comment.
        private Collider justBouncedOffCollider;

        /// <summary>True until this projectile has been told to go away - flips at the very start
        /// of Despawn, before OnExpired or the actual Destroy/DestroyImmediate call, so a caller
        /// driving Step() directly (an edit-mode test; Update never needs this) can stop cleanly
        /// instead of ticking a projectile that has already ended its flight.</summary>
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
        /// Turn the projectile after a bounce, and lift it off the surface it was resting against -
        /// the seam BounceOffWalls uses. hitNormal/hitCollider are the same RaycastHit the bounce
        /// just resolved. See the class comment's "WALL-RATTLE FIX": without the lift, the sphere is
        /// left exactly touching hitCollider, and the very next sweep reads that resting touch as a
        /// fresh hit on the same collider forever. justBouncedOffCollider is remembered too, so
        /// TrySweep's IsRestingOverlapOnLastBounce can refuse to count THIS SPECIFIC collider's
        /// resting-touch signature as a new hit next frame - belt and braces alongside the lift,
        /// since the lift is a distance, not a guarantee, at the float precision this bug was
        /// measured at (65-120m from the origin).
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

            // gameObject.scene's PhysicsScene IS Physics.defaultPhysicsScene for every projectile
            // that has ever existed in real play - a normal Instantiate lands in the loaded Game
            // Scene, never a preview scene - so this is not a behaviour change. It is the seam an
            // edit-mode test uses to call Step() directly against an isolated preview scene's own
            // PhysicsScene instead, which the old Physics.SphereCastNonAlloc could never see at
            // all (the same reason FireField.OverlapBurnZone and GroundSnap.TryFindGroundY take an
            // explicit PhysicsScene - see their own comments).
            Step(Time.deltaTime, gameObject.scene.GetPhysicsScene());
        }

        /// <summary>One frame of flight - everything Update used to do inline, taking deltaTime and
        /// the PhysicsScene to sweep against as parameters instead of reading Time.deltaTime and
        /// the static Physics class directly. See Update's own comment for why.</summary>
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

                // P1 (review follow-up, 2026-09-21): a behaviour kept it alive, so it resumes from
                // the contact point next frame - but Consume above already charged this WHOLE step,
                // even though only hit.distance of it was actually moved. Refund the difference, or
                // a bounced/pierced shot's total reach would depend on how big the frame's own step
                // happened to be (a lower frame rate forfeits more per hit than a higher one).
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

            // M1 (review follow-up, 2026-09-21): whether THIS sweep actually saw the resting-overlap
            // artifact against justBouncedOffCollider - see the field clear below.
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

            // M1: the guard is only ever meant to exempt "the very next sweep" (Redirect's own
            // comment), never every sweep for the rest of this projectile's life - a much later
            // sweep that happened to reproduce the exact same signature against the SAME collider
            // must register as a genuine hit. The first sweep where the artifact is no longer seen
            // is exactly when it is safe to disarm.
            if (justBouncedOffCollider != null && !restingArtifactStillPresent)
                justBouncedOffCollider = null;

            return found;
        }

        /// <summary>
        /// True when candidate is Unity's documented signature for "the sphere already overlapped
        /// this collider at the start of the sweep" (distance ~0, normal the sweep direction
        /// reversed) AND collider is the exact one this projectile most recently bounced off - see
        /// the class comment's "WALL-RATTLE FIX" and justBouncedOffCollider's own comment.
        ///
        /// Deliberately NOT "every initial overlap": a projectile that starts touching or inside a
        /// wall it has never bounced off - a shot fired flush against one (SafeMuzzlePosition
        /// already pulls the MUZZLE back off a wall, but a hand-placed or ability projectile is not
        /// guaranteed the same), or the second wall of an inside corner - must still register that
        /// hit normally, which is exactly what leaving collider != justBouncedOffCollider unfiltered
        /// achieves.
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
        /// P2 (review follow-up, 2026-09-21). Unity's documented behaviour for a sweep that STARTS
        /// already overlapping a collider - an enemy stands at the muzzle, or steps into a
        /// projectile between frames - is hit.point == Vector3.zero and hit.distance == 0, whatever
        /// the collider's real position is (the same signature IsRestingOverlapOnLastBounce reads,
        /// but here for a collider this projectile has never bounced off, so the hit is real and
        /// must still be reported, just not at the origin). Left unfixed, that (0,0,0) point becomes
        /// the impact VFX/SFX position (Despawn), the damage marker (DamageInfo.HitPoint), a
        /// rocket's splash centre (ExplodeOnImpact.Detonate) and the zip gun's pull target
        /// (ZipGunAbility.HandleZipHit) - all snapping to the world origin instead of the real hit.
        ///
        /// collider.ClosestPoint(transform.position) is used rather than the projectile's own
        /// transform.position: a sphere sweep that starts inside a collider is reporting THAT
        /// COLLIDER's surface as "where it got hit", the same thing hit.point already means for a
        /// normal, non-overlapping sweep, whereas transform.position could sit anywhere inside a
        /// large collider's volume and would not read as "the surface that was struck."
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

        /// <summary>Deals the damage, then asks the attached behaviours what to do next. Damage is
        /// applied on every client and only the victim's own component acts on it - see the class
        /// comment on victim-side detection. A pure-utility ability shot (the zip gun, damage 0)
        /// skips ApplyDamage entirely rather than calling it for zero - see the class comment on
        /// Task 1.7b's two ways a shot is built.</summary>
        private ProjectileHitResponse ResolveHit(RaycastHit hit)
        {
            IDamageable victim = hit.collider.GetComponentInParent<IDamageable>();

            if (victim != null && context.Damage > 0f)
            {
                // T3 review fix (item 12): context.SourceId is "weapon id, or the ability id when
                // there is no weapon" (its own comment) - using it for WeaponId here used to also
                // set AbilityId to the SAME value for an ability shot, so a hit from one carried
                // w == ab, redundant and confusing to read. WeaponId is now only ever a real
                // weapon's id (-1 for an ability shot); AbilityId is only ever a real ability's id
                // (-1 for a weapon shot) - the two are mutually exclusive, matching every other
                // DamageInfo site in the game.
                int weaponId = context.Weapon != null ? context.Weapon.Id : -1;
                int abilityId = context.Weapon == null ? context.AbilityId : -1;
                victim.ApplyDamage(new DamageInfo(context.Damage, context.ShooterActorNumber,
                                                   context.ShooterTeamId, weaponId,
                                                   DamageSource.Projectile, false, hit.point, abilityId));
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
                // context.Weapon is null for an ability shot (Task 1.7b) - the fallback below is
                // then the only impact VFX this projectile can show, exactly as if a weapon simply
                // had none of its own set.
                GameObject vfx = context.Weapon != null && context.Weapon.ImpactVfx != null
                    ? context.Weapon.ImpactVfx : fallbackImpactVfx;
                if (vfx != null && VFXManager.Instance != null)
                    VFXManager.Instance.PlayVFX(vfx, at);

                if (impactSfx != null && AudioManager.Instance != null)
                    AudioManager.Instance.Play3D(impactSfx, at);
            }

            for (int i = 0; i < behaviours.Length; i++)
                behaviours[i].OnExpired(this, context);

            // Destroy is always what real play uses (Application.isPlaying is true in every actual
            // Play Mode session and every build) - this is not a behaviour change there. The
            // DestroyImmediate branch exists only so BounceOffWallsRattleTests can drive a real
            // ProjectileMotor to a genuine despawn from an edit-mode test: Object.Destroy logs an
            // error when called outside Play Mode ("Destroy may not be called from edit mode!"),
            // which Unity's edit-mode test runner treats as a failure.
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
