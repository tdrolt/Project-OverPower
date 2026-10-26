using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Applies one status effect to whatever this projectile hits - the stun gun's whole job, and reusable as-is for any ability projectile that
    /// should land a status instead of, or alongside, damage: kind, duration and magnitude are Inspector fields on the PROJECTILE PREFAB, so a
    /// second projectile carrying a Slow or a Vulnerability needs no new script.
    /// STATUS APPLICATION IS VICTIM-SIDE, EXACTLY LIKE DAMAGE (IStatusReceiver's own class comment). Every client simulates every projectile and calls
    /// ApplyStatus on whatever it hit; PlayerStatusEffects.Apply and DummyTarget.ApplyStatus already guard on "is this my own copy" the way
    /// PlayerHealth.ApplyDamage does, so nothing here checks IsMine because nothing here needs to.
    /// LOOKED UP SEPARATELY FROM IDamageable, on purpose: a real player's IDamageable (PlayerHealth) and its IStatusReceiver (PlayerStatusEffects)
    /// are two different components on the same root - only DummyTarget is both. GetComponentInParent finds whichever the struck collider has; a wall
    /// or structure with none takes no status, the same silent no-op Mine.cs's `(target as IStatusReceiver)?.ApplyStatus(...)` falls back to.
    /// Always stops the shot (Despawn), matching AbilityHitRelay.
    /// </summary>
    public sealed class ApplyStatusOnHit : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Which status this projectile applies on a hit. The stun gun uses Stun.")]
        private StatusKind kind = StatusKind.Stun;

        [SerializeField, Tooltip("How many seconds the status lasts. The stun gun's spec: 2.5s.")]
        private float duration = 2.5f;

        /// <summary>Duration in seconds, read-only - the shop's pop-up shows it.</summary>
        public float Duration => duration;

        [SerializeField, Tooltip("The status's strength: ignored for Stun, 0..1 speed loss for Slow, " +
                 "0..1 extra damage for Vulnerability, damage per second for Burn. The stun gun " +
                 "leaves this at 0 - Stun does not read it.")]
        private float magnitude = 0f;

        public void OnSpawned(ProjectileMotor motor, ProjectileContext context) { }

        /// <summary>victim is null for level geometry (a wall) - nothing to apply a status to, so this
        /// is skipped rather than wasting a GetComponentInParent call on a collider that was never
        /// going to have an IStatusReceiver.</summary>
        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context,
                                            RaycastHit hit, IDamageable victim)
        {
            if (victim != null)
            {
                IStatusReceiver receiver = hit.collider.GetComponentInParent<IStatusReceiver>();
                // Reports context.AbilityId: this projectile is not always a weapon's - the stun gun (the only user today) is StunGunAbility's own ability
                // shot, built with ProjectileContext(Definition.Id, ...) - so that is the real id (-1 for a genuine weapon projectile).
                receiver?.ApplyStatus(new StatusEffectSpec { kind = kind, duration = duration, magnitude = magnitude, abilityId = context.AbilityId },
                                       context.ShooterActorNumber);
            }

            return ProjectileHitResponse.Despawn;
        }

        public void OnExpired(ProjectileMotor motor, ProjectileContext context) { }

        private void OnValidate()
        {
            duration = Mathf.Max(0f, duration);
            magnitude = Mathf.Max(0f, magnitude);
        }
    }
}
