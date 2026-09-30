using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// The IProjectileBehaviour an ability-fired projectile carries instead of a weapon effect like
    /// ExplodeOnImpact - Task 1.7b's zip gun today, the stun gun later. It does exactly one thing:
    /// forward the hit to ProjectileContext.OnAbilityHit, if this copy of the shot has one.
    ///
    /// That "if" is the whole trick. Every client spawns an identical local projectile for an
    /// ability cast (see ZipGunAbility.ExecuteCast), but only the CASTER's own machine builds its
    /// context with a non-null OnAbilityHit - every other client's copy carries null. So this
    /// component runs on every machine the same way and needs no "am I the caster" check of its
    /// own; the answer already lives in which context it was handed.
    ///
    /// Always stops the shot (Despawn) - neither the zip gun nor the stun gun pierce. A future
    /// ability that needs to keep flying after notifying its caster is a new, small behaviour, not a
    /// flag added here: this class stays a plain relay on purpose, the same reason ProjectileMotor
    /// stays free of ability-specific code.
    /// </summary>
    public sealed class AbilityHitRelay : MonoBehaviour, IProjectileBehaviour
    {
        public void OnSpawned(ProjectileMotor motor, ProjectileContext context) { }

        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context,
                                            RaycastHit hit, IDamageable victim)
        {
            context.OnAbilityHit?.Invoke(new ProjectileHitInfo(hit.point, hit.normal, victim));
            return ProjectileHitResponse.Despawn;
        }

        public void OnExpired(ProjectileMotor motor, ProjectileContext context) { }
    }
}
