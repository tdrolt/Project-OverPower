using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Makes a projectile hit harder the further it has flown - the "Distance" leaf of the rocket
    /// path. Nothing at the muzzle, the full bonus at maximum range, in a straight line between.
    ///
    /// It is the inverse of the damage falloff most shooters have, and it is the whole character
    /// of the upgrade: this rocket wants to be fired from the far side of the lane, which is
    /// exactly where the weapon's wide aim cone is hardest to live with. The two numbers argue
    /// with each other on purpose.
    ///
    /// The bonus is applied as a MULTIPLIER on the shot rather than as extra damage dealt on
    /// impact, for two reasons. It keeps the hit a single hit, so the test range's "killed in N
    /// hits" readout still means what it says. And because splash reads the same multiplier, the
    /// whole rocket scales together - a designer reading "+50% damage at range" would not expect
    /// the direct hit to grow while the explosion stayed the same size.
    ///
    /// Execution order is pinned below the motor's so the multiplier for a frame is set BEFORE the
    /// motor moves and resolves hits in that frame. Component order within one GameObject is
    /// otherwise undefined, and an undefined order here would mean two clients could resolve the
    /// same hit at slightly different damage.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class ScaleDamageWithDistance : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Extra damage at the very end of the projectile's range, as a " +
                 "fraction of its normal damage. 0.5 means a rocket that lands at maximum range " +
                 "hits 50% harder than one that lands at the muzzle. The bonus grows in a " +
                 "straight line with distance flown, and it scales the explosion as well as the " +
                 "direct hit.")]
        private float maxBonus = 0.5f;

        private ProjectileMotor motor;
        private ProjectileContext context;

        public void OnSpawned(ProjectileMotor projectileMotor, ProjectileContext shot)
        {
            motor = projectileMotor;
            context = shot;
            ApplyBonusForDistanceSoFar(); // A shot that hits on its very first step still gets the
                                          // correct (zero) bonus rather than last shot's leftovers.
        }

        private void Update() => ApplyBonusForDistanceSoFar();

        /// Range.Fraction is the distance flown as 0 at the muzzle to 1 at maximum range, and it
        /// is exact at both ends because RangeBudget stops the projectile precisely on its range
        /// rather than somewhere past it. That precision is why this weapon cannot quietly exceed
        /// the cap its stat block promises.
        private void ApplyBonusForDistanceSoFar()
        {
            if (motor == null || context == null)
                return;

            context.SetDamageMultiplier(1f + maxBonus * motor.Range.Fraction);
        }

        /// <summary>This behaviour has no opinion about what happens after a hit. Despawn is the
        /// neutral answer - it is what a projectile with no behaviours at all does, and any
        /// sibling asking to keep flying overrides it.</summary>
        public ProjectileHitResponse OnHit(ProjectileMotor projectileMotor, ProjectileContext shot,
                                            RaycastHit hit, IDamageable victim)
        {
            return ProjectileHitResponse.Despawn;
        }

        public void OnExpired(ProjectileMotor projectileMotor, ProjectileContext shot)
        {
        }
    }
}
