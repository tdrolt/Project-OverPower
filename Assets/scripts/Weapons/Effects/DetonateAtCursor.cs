using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Makes a rocket detonate where the shooter's CURSOR was rather than on whatever it runs into, and leaves a patch of fire on the ground where it
    /// goes off - the "Cursor Fire" leaf of the rocket path.
    /// The rocket still stops on anything solid it meets first: that needs no code here, since the sibling ExplodeOnImpact detonates on contact and
    /// this behaviour never gets to say "arrived". Its whole job is the OTHER case - reaching the cursor with nothing in the way: it works out the
    /// cursor distance when the rocket is born, watches the distance flown, and on arrival spends the rest of the range budget, so the motor ends
    /// the shot down the same path as one that ran out of range and ExplodeOnImpact's OnExpired supplies the airburst. ProjectileMotor is untouched.
    /// THE CURSOR POINT CANNOT BE READ HERE. Camera.main and Input.mousePosition inside the fire RPC answer for whoever RECEIVED it, so every client
    /// would detonate at its own player's mouse. It travels as an RPC parameter into ProjectileContext.TargetPoint (WeaponFiring's class comment).
    /// </summary>
    [DisallowMultipleComponent]
    public class DetonateAtCursor : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("The patch of fire left burning where this rocket goes off. How " +
                 "long it lasts, how big it is and how much it hurts are all on that prefab, not " +
                 "here. Leave it empty for a cursor rocket that detonates but leaves nothing " +
                 "behind. It must live in a Resources folder, because that is how Photon finds a " +
                 "prefab it has to spawn on every machine.")]
        private GameObject fireFieldPrefab;

        private ProjectileMotor motor;
        private ProjectileContext context;

        /// How far this rocket flies before going off by itself, in metres.
        private float targetDistance;

        private bool arrived;

        public void OnSpawned(ProjectileMotor projectileMotor, ProjectileContext shot)
        {
            motor = projectileMotor;
            context = shot;
            arrived = false;
            targetDistance = DistanceToCursorPoint();
        }

        /// <summary>How far this rocket has to fly to reach the cursor - AimConeView draws the same distance for this weapon's aim lines/arc through
        /// the static helper below, so the two can never disagree about how far a cursor rocket reaches.
        /// context.MaxRange, not context.Weapon.MaxRange: the former already carries OverPower's range multiplier (see ProjectileContext.MaxRange), so
        /// the buff cannot extend how far this rocket FLIES without extending how far it may detonate at the cursor - a second, disagreeing range.</summary>
        private float DistanceToCursorPoint() =>
            ClampedDistanceToTarget(transform.position, context.TargetPoint, context.MaxRange);

        /// <summary>
        /// How far a shot from origin toward targetPoint travels before it would reach that point,
        /// clamped to maxRange - the one place this maths lives, called by DistanceToCursorPoint
        /// above and by AimConeView's aim-line drawing for this weapon.
        ///
        /// Flat because the cursor point is resolved against the player's own ground plane while
        /// the rocket leaves a muzzle somewhere above it, so the straight-line distance between
        /// the two would be slightly long and the rocket would drift past the mark.
        ///
        /// Clamped to maxRange for the obvious reason: a rocket cannot arrive somewhere it is not
        /// allowed to fly to, and one that expired short of an uncapped target would still
        /// detonate - just wherever the range ran out. Clamping makes "aimed too far" behave as
        /// "aimed at the edge of my range", which is the reading a player expects.
        /// </summary>
        public static float ClampedDistanceToTarget(Vector3 origin, Vector3 targetPoint, float maxRange)
        {
            targetPoint.y = origin.y;
            return Mathf.Min(Vector3.Distance(origin, targetPoint), maxRange);
        }

        private void Update()
        {
            if (arrived || motor == null)
                return;

            if (motor.Range.Travelled < targetDistance)
                return;

            arrived = true;

            // Arrived. Spend whatever range is left so the motor retires the rocket on its next step, through the same expiry path a shot that simply ran
            // out of range takes - which fires ExplodeOnImpact.OnExpired and produces the airburst. Consume clamps to what remains, so asking for the whole
            // range drains exactly the remainder and moves the rocket nowhere.
            // The cost of not adding a "stop now" call to the motor is that the rocket can overshoot the cursor by up to one frame of travel (about 0.3m at
            // 18 m/s, against a 3m blast radius). Worth it to leave the motor alone.
            motor.Range.Consume(motor.Range.MaxRange);
        }

        /// <summary>Hit something on the way. Nothing to do - ExplodeOnImpact detonates on contact
        /// and OnExpired below still drops the fire wherever that turned out to be. Despawn is the
        /// neutral answer, the same one a projectile with no behaviours gives.</summary>
        public ProjectileHitResponse OnHit(ProjectileMotor projectileMotor, ProjectileContext shot,
                                            RaycastHit hit, IDamageable victim)
        {
            return ProjectileHitResponse.Despawn;
        }

        /// <summary>
        /// The rocket is gone, however it went, so light the fire where it ended up.
        ///
        /// ONLY THE SHOOTER'S CLIENT SPAWNS IT. Every client simulates every projectile, so this
        /// runs on all nine machines for the same rocket - and unlike damage, which is filtered
        /// harmlessly by the victim's own ownership check, PhotonNetwork.Instantiate would
        /// genuinely create nine fire fields. One client decides; Photon tells the rest.
        /// </summary>
        public void OnExpired(ProjectileMotor projectileMotor, ProjectileContext shot)
        {
            if (fireFieldPrefab == null || context == null)
                return;

            if (PhotonNetwork.LocalPlayer == null ||
                PhotonNetwork.LocalPlayer.ActorNumber != context.ShooterActorNumber)
                return;

            // The field goes on the FLOOR below the burst, not at the rocket's own muzzle-height position - found here, on the shooter's own client, with
            // the floor finder every ability visual uses (GroundSnap). PhotonNetwork.Instantiate's position argument replicates whatever this client
            // decided to every other machine, so no new RPC is needed for every client to agree on where the field sits. Falls back to the rocket's own
            // position over a void, like every other GroundSnap caller.
            Vector3 spawnPosition = transform.position;
            if (GroundSnap.TryFindGroundY(transform.position, out float groundY))
                spawnPosition = new Vector3(transform.position.x, groundY, transform.position.z);

            // The combined multiplier (behaviour-scaling * OverPower's fire-time bonus), the same product Damage and SplashDamageAt apply, so the burning
            // ground this rocket leaves scales with the shot exactly like its direct hit and its splash do.
            FireField.Spawn(fireFieldPrefab, spawnPosition, context.Weapon.Id,
                             context.DamageMultiplier * context.FireTimeDamageMultiplier);
        }
    }
}
