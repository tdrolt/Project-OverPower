using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Turns a projectile into one that bounces off walls instead of stopping on them - the
    /// "Bounce" leaf of the burst path. Every bounce adds damage, so landing a shot after it has
    /// ricocheted round a corner is worth more than a straight hit, which is the whole point of
    /// building for it.
    ///
    /// BOUNCES ARE GEOMETRY ONLY. Hitting a player still ends the shot exactly like an unmodified
    /// projectile - see OnHit below - because "bouncing off a person" is not the mechanic the
    /// designer asked for, and letting a shot ricochet off a body would make standing behind a
    /// teammate a way to redirect fire instead of block it.
    ///
    /// The damage bonus is ADDITIVE, not multiplicative, on the designer's explicit instruction:
    /// three bounces must land at +60% (1.6x), not +20% compounded three times (1.73x). Setting
    /// ProjectileContext's multiplier directly from the bounce count - never reading it back and
    /// re-multiplying - is what keeps that true.
    ///
    /// A bounced shot gets NO extra range. ProjectileMotor creates one RangeBudget in Initialize
    /// and Redirect only ever changes direction (and, since the fix below, lifts the projectile a
    /// couple of centimetres off the wall - never touches RangeBudget), so the metres already
    /// flown before a bounce stay spent and the total distance travelled is still capped at Max
    /// Range. Nothing in this file has to enforce that - it falls out of not touching
    /// ProjectileMotor's range bookkeeping at all, which is the entire point of the
    /// IProjectileBehaviour seam.
    ///
    /// THE WALL-RATTLE FIX (2026-09-21). Tudor measured bounced shots dealing LESS per trigger
    /// pull than direct ones - the opposite of this file's own "+20% per bounce" intent. The cause
    /// lived in ProjectileMotor, not here: a bullet left resting exactly on the wall it just
    /// bounced off was read as hitting that SAME wall again on the very next sweep (Unity's
    /// documented "initial overlap" signature - distance 0, normal the sweep direction reversed),
    /// so this OnHit kept reflecting about an artifact instead of a real second wall, until the
    /// bounce budget ran out on the wall it should have flown away from. The only change here is
    /// which Redirect overload is called - the one that also lifts the projectile off the surface
    /// and remembers which collider to distrust on the next sweep. See ProjectileMotor's class
    /// comment and Redirect's own comment for the mechanism.
    /// </summary>
    [DisallowMultipleComponent]
    public class BounceOffWalls : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("How many times this projectile may bounce off a wall before the " +
                 "next wall it meets ends the shot instead. The designer's number is 3.")]
        private int maxBounces = 3;

        [SerializeField, Tooltip("Extra damage for every bounce so far, as a fraction of the " +
                 "weapon's base damage, ADDED together rather than compounded - 0.2 means a shot " +
                 "that has bounced three times deals 1 + 0.2*3 = 1.6x, not 1.2 cubed.")]
        private float damagePerBounce = 0.20f;

        private int bounces;

        /// A pooled or otherwise reused GameObject must never carry a previous flight's bounce
        /// count into a new one.
        public void OnSpawned(ProjectileMotor motor, ProjectileContext shot)
        {
            bounces = 0;
        }

        /// <summary>
        /// A player ends the shot exactly like a projectile with no behaviours at all - only a
        /// wall (victim == null) bounces. Damage for THIS hit was already dealt by the motor
        /// before OnHit runs, at whatever multiplier the earlier bounces left set, so a player hit
        /// needs no extra work here.
        /// </summary>
        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext shot,
                                            RaycastHit hit, IDamageable victim)
        {
            if (victim != null)
                return ProjectileHitResponse.Despawn;

            if (bounces >= maxBounces)
                return ProjectileHitResponse.Despawn;

            bounces++;
            shot.SetDamageMultiplier(1f + damagePerBounce * bounces);
            motor.Redirect(Vector3.Reflect(motor.Direction, hit.normal), hit.normal, hit.collider);
            return ProjectileHitResponse.KeepFlying;
        }

        /// <summary>No opinion about a shot that simply runs out of range or lifetime.</summary>
        public void OnExpired(ProjectileMotor motor, ProjectileContext shot)
        {
        }
    }
}
