using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Turns a projectile into a rocket: when it stops, it detonates and everything standing near
    /// the blast takes a share of the splash damage.
    ///
    /// This is the first real test of the IProjectileBehaviour seam, and the point of the test is
    /// what is NOT here - ProjectileMotor was not touched to make rockets exist. This is a
    /// component dropped onto a projectile prefab, the motor finds it with GetComponents, and the
    /// difference between the baseline bullet and a rocket is one component and one asset.
    ///
    /// SPLASH IS APPLIED ON EVERY CLIENT, ON PURPOSE. Every machine simulates every projectile, so
    /// nine clients each run this OverlapSphere for the same rocket. That is safe, and it is safe
    /// for exactly one reason: PlayerHealth.ApplyDamage returns immediately unless
    /// photonView.IsMine, so each client can only ever damage its own player. Nine calls land as
    /// one hit on the one machine that owns the victim. Do NOT "fix" this by having only the
    /// shooter apply splash - the victim is the authority on its own health everywhere else in
    /// this project (see ProjectileMotor's note on victim-side detection) and a second rule here
    /// would be the divergence that costs an afternoon later.
    ///
    /// What every client must NOT do is spawn or destroy networked objects. That mistake is
    /// already in this codebase's history: an area-of-effect coroutine ran on all nine clients and
    /// every one of them called PhotonNetwork.Destroy, producing eight errors per cast. Nothing in
    /// this file creates a networked object; DetonateAtCursor does, and it guards accordingly.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExplodeOnImpact : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Damage dealt to everything caught in the blast, at the very " +
                 "centre of it. This is ON TOP of the weapon's Damage, which the thing actually " +
                 "struck has already taken - see the tooltip on Falloff for who gets which.")]
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

        // Not a tuning value: how many colliders one blast considers. Nine players plus scenery
        // inside a 3m sphere cannot plausibly exceed this, and a fixed shared buffer keeps the
        // detonation allocation-free - matching ProjectileMotor's HitBuffer.
        private const int MaxSplashColliders = 32;
        private static readonly Collider[] OverlapBuffer = new Collider[MaxSplashColliders];

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
            Detonate(hit.point, victim);
            return ProjectileHitResponse.Despawn;
        }

        /// <summary>
        /// The rocket has gone away for any reason - it hit something, it ran out of range, or a
        /// sibling behaviour decided it had arrived. Detonating here rather than only in OnHit is
        /// what makes a rocket that reaches the end of its flight still go off instead of quietly
        /// evaporating, and it is also how DetonateAtCursor gets its airburst without this file
        /// knowing that weapon exists.
        /// </summary>
        public void OnExpired(ProjectileMotor motor, ProjectileContext shot)
        {
            Detonate(transform.position, null);
        }

        /// <summary>
        /// One blast at one point. Everything with health inside Splash Radius takes
        /// Splash Damage scaled by Falloff, except the shooter, the shooter's team, and whatever
        /// took the direct hit.
        /// </summary>
        /// <param name="directVictim">What the projectile physically struck, or null for an
        /// airburst. It is skipped: it has already taken the weapon's full Damage, and adding the
        /// splash on top would make a direct hit worth two hits. The designer's time-to-kill
        /// budget is written against the direct damage alone.</param>
        private void Detonate(Vector3 at, IDamageable directVictim)
        {
            if (detonated || context == null)
                return;

            detonated = true;
            caught.Clear();

            int count = Physics.OverlapSphereNonAlloc(at, splashRadius, OverlapBuffer, splashMask,
                                                       QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable target = collider.GetComponentInParent<IDamageable>();
                if (target == null || ReferenceEquals(target, directVictim))
                    continue;

                if (IsFriendly(target) || !caught.Add(target))
                    continue;

                float amount = SplashDamageAt(at, collider.transform.position);
                if (amount <= 0f)
                    continue;

                target.ApplyDamage(new DamageInfo(amount, context.ShooterActorNumber,
                                                   context.ShooterTeamId, context.Weapon.Id,
                                                   DamageSource.Splash, false, at));
            }
        }

        /// <summary>Splash at a point, after Falloff and after whatever the shot's damage
        /// multiplier currently is - so a rocket that gains damage over distance gains it on the
        /// blast too, not just on the thing it hit. See ProjectileContext.DamageMultiplier.</summary>
        private float SplashDamageAt(Vector3 blastCentre, Vector3 targetPosition)
        {
            // A zero radius would divide by zero, and reads as "no blast at all" rather than "an
            // infinitely concentrated one" - the safer of the two readings for a typo.
            if (splashRadius <= 0f)
                return 0f;

            float normalised = Mathf.Clamp01(Vector3.Distance(blastCentre, targetPosition) / splashRadius);
            return splashDamage * context.DamageMultiplier * Mathf.Max(0f, falloff.Evaluate(normalised));
        }

        /// <summary>The same no-friendly-fire rule the projectile sweep uses, so a rocket cannot
        /// do by exploding what it is forbidden to do by hitting. Unknown teams fail OPEN and stay
        /// valid targets, matching ProjectileMotor.FliesThrough and Teams.AreSameTeam. It also
        /// means a rocket detonating at your own feet does not kill you.</summary>
        private bool IsFriendly(IDamageable target)
        {
            if (target.ActorNumber == context.ShooterActorNumber)
                return true;

            return context.ShooterTeamId >= 0 && target.TeamId == context.ShooterTeamId;
        }
    }
}
