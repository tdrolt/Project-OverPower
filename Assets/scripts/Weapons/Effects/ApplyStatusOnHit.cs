using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Applies one status effect to whatever this projectile hits - the stun gun's whole reason for
    /// existing (Task 1.9: Stun, 2.5s, on the first enemy hit), and reusable as-is for any later
    /// ability projectile that should land a status instead of, or alongside, damage: the kind,
    /// duration and magnitude are Inspector fields on the PROJECTILE PREFAB, not hardcoded here, so a
    /// second projectile carrying a Slow or a Vulnerability needs no new script.
    ///
    /// STATUS APPLICATION IS VICTIM-SIDE, EXACTLY LIKE DAMAGE (IStatusReceiver's own class comment).
    /// Every client simulates every projectile and calls ApplyStatus on whatever it hit;
    /// PlayerStatusEffects.Apply and DummyTarget.ApplyStatus already guard on "is this my own copy"
    /// the same way PlayerHealth.ApplyDamage does, so calling this on every client is correct, not a
    /// race - nothing here checks IsMine because nothing here needs to.
    ///
    /// LOOKED UP SEPARATELY FROM IDamageable, on purpose: a real player's IDamageable (PlayerHealth)
    /// and its IStatusReceiver (PlayerStatusEffects) are two different components on the same root -
    /// only DummyTarget happens to be both at once. GetComponentInParent finds whichever one the
    /// struck collider actually has; a wall or a structure with none simply takes no status, the same
    /// silent no-op Mine.cs's own `(target as IStatusReceiver)?.ApplyStatus(...)` falls back to.
    ///
    /// Always stops the shot (Despawn), matching AbilityHitRelay - neither ability projectile built
    /// so far pierces.
    /// </summary>
    public sealed class ApplyStatusOnHit : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Which status this projectile applies on a hit. The stun gun uses Stun.")]
        private StatusKind kind = StatusKind.Stun;

        [SerializeField, Tooltip("How many seconds the status lasts. The stun gun's spec: 2.5s.")]
        private float duration = 2.5f;

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
                // abilityId -1: this is a weapon projectile's own behaviour (the stun gun), not an
                // ability cast - see StatusEffectSpec.abilityId's own comment.
                receiver?.ApplyStatus(new StatusEffectSpec { kind = kind, duration = duration, magnitude = magnitude, abilityId = -1 },
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
