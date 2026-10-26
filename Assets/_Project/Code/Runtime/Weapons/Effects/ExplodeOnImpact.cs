using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Turns a projectile into a rocket: when it stops, it detonates and everything standing near the blast takes a share of the splash damage.
    /// A component dropped onto a projectile prefab, which the motor finds with GetComponents - ProjectileMotor was not touched to make rockets exist.
    /// SPLASH IS APPLIED ON EVERY CLIENT, ON PURPOSE. Every machine simulates every projectile, so nine clients each run this OverlapSphere for the
    /// same rocket. That is safe for exactly one reason: PlayerHealth.ApplyDamage returns immediately unless photonView.IsMine, so each client can
    /// only ever damage its own player, and nine calls land as one hit on the machine that owns the victim. Do NOT "fix" this by having only the
    /// shooter apply splash - the victim is the authority on its own health everywhere (see ProjectileMotor's note on victim-side detection), and a
    /// second rule here would be the divergence that costs an afternoon later.
    /// What every client must NOT do is spawn or destroy networked objects (an earlier area-of-effect coroutine ran on all nine clients and each
    /// called PhotonNetwork.Destroy: eight errors per cast). Nothing in this file creates one; DetonateAtCursor does, and guards accordingly.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExplodeOnImpact : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Damage dealt to everything caught in the blast, at the very " +
                 "centre of it - the rocket's main damage. The thing it actually struck takes this " +
                 "too, ON TOP of the weapon's Damage (a small bonus for a precise hit).")]
        private float splashDamage = 20f;

        [SerializeField, Tooltip("How far the blast reaches, in metres. Everything with health " +
                 "inside this sphere is caught, friendlies excepted.")]
        private float splashRadius = 3f;

        [SerializeField, Tooltip("How much of Splash Damage a target takes, by how far it stands " +
                 "from the blast. The left edge is the dead centre of the explosion and the right " +
                 "edge is Splash Radius, so the default straight line means full damage at the " +
                 "centre fading to nothing at the rim. Flatten it for a rocket that hurts the " +
                 "same everywhere in its radius; make it drop off a cliff for one that only " +
                 "punishes a direct-ish hit.")]
        private AnimationCurve falloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [SerializeField, Tooltip("Which layers the blast can catch. Default is where living " +
                 "players and practice dummies are; nothing on any other layer has health to " +
                 "lose, so widening this only costs performance.")]
        private LayerMask splashMask = ~0;

        [Header("Occlusion")]
        [SerializeField, Tooltip("Metres the occlusion check's own start point is nudged off the " +
                 "struck surface, back toward where the shot came from, before it lines out to each " +
                 "splash candidate. Without this, a blast that hit a wall face-on would have its " +
                 "occlusion check start literally inside that same collider, which Unity's physics " +
                 "reports inconsistently. A few centimetres of numerical safety margin, not " +
                 "something a designer would balance gameplay against.")]
        private float occlusionNudge = 0.1f;

        // Not a tuning value: walls occlude splash whatever this rocket's own Splash Mask is set to
        // catch (a rocket that could splash right through a wall would make cover, and every real
        // wall, pointless against it) - see Detonate's own comment. Building is the one layer every
        // wall in the arena, cover included, already sits on. Computed once in Awake since
        // LayerMask.NameToLayer never changes at runtime.
        private int buildingMask;

        // Not a tuning value: how many colliders one blast considers. Nine players plus scenery
        // inside a 3m sphere cannot plausibly exceed this, and a fixed shared buffer keeps the
        // detonation allocation-free - matching ProjectileMotor's HitBuffer.
        private const int MaxSplashColliders = 32;
        private static readonly Collider[] OverlapBuffer = new Collider[MaxSplashColliders];

        // Not a tuning value: how many Building-layer hits one occlusion check considers between the
        // blast and a single candidate. A candidate is never behind more than a couple of walls at
        // once in this arena, and this only needs to find one hit that is not the struck collider.
        private const int MaxOcclusionHits = 4;
        private static readonly RaycastHit[] OcclusionBuffer = new RaycastHit[MaxOcclusionHits];

        // One blast, one hit per victim. A player is several colliders to Physics (body, head,
        // whatever a future model adds) and OverlapSphere reports each of them separately, so
        // without this a target with two colliders would quietly take double splash - which reads
        // in play as "rockets sometimes do twice the damage" and is miserable to reproduce.
        private readonly HashSet<IDamageable> caught = new HashSet<IDamageable>();

        private ProjectileContext context;

        /// A rocket must detonate exactly once. OnExpired ALWAYS runs after a hit despawns the
        /// projectile, so without this flag every impact would explode twice: once from OnHit and
        /// once from the despawn that followed it.
        private bool detonated;

        /// <summary>Splash Radius, read-only - RocketBlastView sizes the splash shell from this one number.</summary>
        public float SplashRadius => splashRadius;

        /// <summary>Splash Damage, read-only - the shop's pop-up shows it.</summary>
        public float SplashDamage => splashDamage;

        /// <summary>Raised once per rocket, on every client, right after the splash has been applied, with the blast centre and the rocket's own Splash
        /// Radius - ALWAYS the full radius, whatever the blast actually caught, so a wall hit or a range-end airburst still draws its blast (the damage
        /// numbers on whoever got hurt already show what it caught). Visual only: RocketBlastView reads this to decide where and how big to draw the
        /// shell. No new networked state - every client computes this identically, per "SPLASH IS APPLIED ON EVERY CLIENT". Nothing that affects damage
        /// listens.</summary>
        public event System.Action<Vector3, float> Detonated;

        private void Awake()
        {
            // Guarded the same way IgnoreWalls guards its own NameToLayer lookup: a missing layer
            // fails to occlude nothing rather than a nonsense bitmask (a negative shift is undefined
            // territory), which is the safer reading of a broken TagManager for a splash check that
            // is a bonus over the base "walls block bullets" behaviour, not the whole feature.
            int layer = LayerMask.NameToLayer("Building");
            buildingMask = layer >= 0 ? 1 << layer : 0;
        }

        public void OnSpawned(ProjectileMotor motor, ProjectileContext shot)
        {
            context = shot;
            detonated = false;
            caught.Clear();
        }

        /// <summary>Struck something, so detonate here. Returning Despawn is what ends the shot;
        /// a rocket that pierced would be a different weapon with a different prefab.</summary>
        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext shot,
                                            RaycastHit hit, IDamageable victim)
        {
            Detonate(hit.point, victim, hit.collider, hit.normal);
            return ProjectileHitResponse.Despawn;
        }

        /// <summary>
        /// The rocket has gone away for any reason - it hit something, it ran out of range, or a
        /// sibling behaviour decided it had arrived. Detonating here rather than only in OnHit is
        /// what makes a rocket that reaches the end of its flight still go off instead of quietly
        /// evaporating, and it is also how DetonateAtCursor gets its airburst without this file
        /// knowing that weapon exists. An airburst has no struck surface to nudge away from - see
        /// Detonate's own occlusionOrigin comment.
        /// </summary>
        public void OnExpired(ProjectileMotor motor, ProjectileContext shot)
        {
            Detonate(transform.position, null, null, Vector3.zero);
        }

        /// <summary>
        /// One blast at one point. Everything with health inside Splash Radius takes Splash Damage scaled by Falloff, except the shooter, the shooter's
        /// team, and anything with a wall (Building layer, cover included) standing between it and the blast: walls stop blasts, or cover would be
        /// pointless against a rocket lobbed just past it. FireField's own ground AoE is deliberately NOT given this check - it has no impact surface to
        /// occlude from, only a radius on the ground.
        /// </summary>
        /// <param name="directVictim">What the projectile physically struck, or null for an airburst. It takes the blast like everyone else, on top of
        /// the weapon's small impact Damage: the blast is the main damage, the impact rewards precision.</param>
        /// <param name="directHitCollider">The collider actually struck, or null for an airburst -
        /// used only to know whether there IS a struck surface to nudge away from (see
        /// occlusionOrigin below); it is deliberately NOT what gets excluded from the occlusion
        /// check itself, see IsOccludedByAWall's own comment for why that would be the wrong
        /// exclusion.</param>
        /// <param name="surfaceNormal">The struck surface's normal, used only to nudge the occlusion
        /// check's own start point off of it - meaningless (and unused) for an airburst.</param>
        private void Detonate(Vector3 at, IDamageable directVictim, Collider directHitCollider, Vector3 surfaceNormal)
        {
            if (detonated || context == null)
                return;

            detonated = true;
            caught.Clear();

            // Nudged off the struck surface, back toward the shooter, so this is not left sitting
            // inside the very collider it just hit - see Occlusion Nudge's own tooltip. Only the
            // OCCLUSION check uses this point; damage falloff below still measures from the real
            // impact point (at), so nudging this never changes a single damage number.
            Vector3 occlusionOrigin = directHitCollider != null ? at + surfaceNormal * occlusionNudge : at;

            int count = Physics.OverlapSphereNonAlloc(at, splashRadius, OverlapBuffer, splashMask,
                                                       QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable target = collider.GetComponentInParent<IDamageable>();
                if (target == null)
                    continue;

                if (IsFriendly(target) || !caught.Add(target))
                    continue;

                // The CANDIDATE's own collider is what gets excluded here - not directHitCollider.
                // A struck wall MUST still occlude something standing behind it (that IS the whole
                // point of this check, and directHitCollider would be that very wall) - the only
                // collider that ever needs excluding is the candidate's own, so a Building-layer
                // candidate (a second cover wall, say) does not shadow ITS OWN centre point from the
                // blast, the one case Physics.Linecast would otherwise get wrong.
                if (IsOccludedByAWall(occlusionOrigin, collider.transform.position, collider))
                    continue;

                float amount = SplashDamageAt(at, collider.transform.position);
                if (amount <= 0f)
                    continue;

                // abilityId -1: this splash always comes from a weapon's own rocket (context.Weapon
                // is read directly, never AbilityId) - see DamageInfo.AbilityId's own comment.
                target.ApplyDamage(new DamageInfo(amount, context.ShooterActorNumber,
                                                   context.ShooterTeamId, context.Weapon.Id,
                                                   DamageSource.Splash, false, at, -1));
            }

            // Raised after every splash hit above, so a visual listener cannot affect one. Also for an airburst (OnExpired calls this same method, so a
            // rocket reaching its range end or the cursor raises Detonated like a direct hit), and always with the full Splash Radius, never gated on
            // whether anything took damage - see Detonated's own comment.
            Detonated?.Invoke(at, splashRadius);
        }

        /// <summary>
        /// True when a Building-layer collider stands between the blast and "to" - walls (cover included) block splash.
        /// "ownCollider" excludes only the CANDIDATE'S OWN collider, NOT whatever the rocket directly struck. "to" is a candidate's centre, which for
        /// anything with real thickness sits INSIDE that candidate's own collider, so a linecast to it always finds that collider at the very end;
        /// without excluding it a Building-layer candidate (another wall) would read as occluded by ITSELF and could never take incidental splash. But
        /// when the STRUCK wall stands between the blast and some OTHER candidate further back, that wall is doing its job and must still count as a
        /// blocker - excluding it would let the splash leak straight through the wall to whoever stands behind it.
        /// </summary>
        private bool IsOccludedByAWall(Vector3 from, Vector3 to, Collider ownCollider)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
                return false;

            int count = Physics.RaycastNonAlloc(from, delta / distance, OcclusionBuffer, distance,
                                                buildingMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = OcclusionBuffer[i].collider;
                if (hitCollider != null && hitCollider != ownCollider)
                    return true;
            }

            return false;
        }

        /// <summary>Splash at a point, after Falloff and the shot's damage multiplier, so a rocket that gains damage over distance gains it on the blast
        /// too (ProjectileContext.DamageMultiplier). Also multiplied by FireTimeDamageMultiplier - OverPower's buff, fixed at fire time and not
        /// overwritten by DamageMultiplier's mid-flight rescaling (Bounce/Distance): this method never reads baseDamage, so the buff has to be applied
        /// here.</summary>
        private float SplashDamageAt(Vector3 blastCentre, Vector3 targetPosition)
        {
            // A zero radius would divide by zero, and reads as "no blast at all" rather than "an
            // infinitely concentrated one" - the safer of the two readings for a typo.
            if (splashRadius <= 0f)
                return 0f;

            float normalised = Mathf.Clamp01(Vector3.Distance(blastCentre, targetPosition) / splashRadius);
            return splashDamage * context.DamageMultiplier * context.FireTimeDamageMultiplier *
                   Mathf.Max(0f, falloff.Evaluate(normalised));
        }

        /// <summary>The same no-friendly-fire rule the projectile sweep and the beam use -
        /// FriendlyFire.IsSelfOrTeammate - so a rocket cannot do by exploding what it is forbidden
        /// to do by hitting. It also means a rocket detonating at your own feet does not kill
        /// you.</summary>
        private bool IsFriendly(IDamageable target)
        {
            return FriendlyFire.IsSelfOrTeammate(context.ShooterActorNumber, target.ActorNumber,
                                                  context.ShooterTeamId, target.TeamId);
        }
    }
}
