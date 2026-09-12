using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;

namespace Overpower.Weapons
{
    /// <summary>
    /// One player's trigger. Replaces PlayerShooting, which had three problems this class exists to
    /// fix: it read input with Input.GetMouseButton instead of the input router, it hardcoded its
    /// fire rate and damage instead of reading a weapon asset, and - the live bug - its Fire RPC
    /// took no parameters and computed its direction from `transform.rotation` INSIDE the RPC body.
    /// Inside an RPC, transform resolves on the RECEIVING machine, so every remote client fired
    /// along its own interpolated copy of the shooter's rotation rather than where the shooter
    /// actually aimed, and shots visibly diverged between clients.
    ///
    /// The rule that replaces it: anything the sender meant travels as an RPC PARAMETER or comes
    /// from PhotonMessageInfo.Sender. Never read transform, Camera.main, Input.mousePosition or
    /// PhotonNetwork.LocalPlayer inside an RPC body and expect the sender's values.
    ///
    /// Projectiles are simulated locally on every client and are NOT networked objects.
    /// PhotonNetwork.Instantiate per bullet would create dozens of networked objects a second for
    /// nine players. The fire RPC carries everything a receiver needs to build the identical shot.
    ///
    /// Deliberately NOT IPunObservable: the player's PhotonView uses AutoFindAll and would silently
    /// absorb a second observable. PlayerNetSync is the only one.
    /// </summary>
    public class WeaponFiring : MonoBehaviourPun
    {
        [SerializeField, Tooltip("Every weapon in the game. Weapon ids arriving over the network " +
                 "are resolved through this, never by position in its list.")]
        private WeaponCatalogue catalogue;

        [SerializeField, Tooltip("The weapon this player starts the match holding. The shop and " +
                 "the test range replace it at runtime through SetWeapon.")]
        private WeaponDefinition startingWeapon;

        [SerializeField, Tooltip("Where projectiles leave the gun. Falls back to the player's own " +
                 "position if it is empty, which looks wrong but still fires.")]
        private Transform muzzle;

        private PlayerAim aim;
        private PlayerOverheat overheat;
        private PlayerLifecycle lifecycle;
        private PlayerInputRouter input;

        private WeaponDefinition weapon;
        private float nextFireTime;
        private float triggerHeldSince;

        public WeaponDefinition Weapon => weapon;

        private void Awake()
        {
            aim = GetComponent<PlayerAim>();
            overheat = GetComponent<PlayerOverheat>();
            lifecycle = GetComponent<PlayerLifecycle>();
            input = GetComponent<PlayerInputRouter>();

            // A silent null here would leave this player unable to fire at all, or - worse - able
            // to fire a weapon nobody can resolve on the other clients. Loud, matching PlayerHealth.
            if (catalogue == null)
                Debug.LogError($"[WeaponFiring] {name}: Weapon Catalogue is not assigned - this player cannot shoot.");
            if (startingWeapon == null)
                Debug.LogError($"[WeaponFiring] {name}: Starting Weapon is not assigned - this player cannot shoot.");

            EquipWeapon(startingWeapon);
        }

        private void OnEnable()
        {
            if (input == null)
                return;

            input.PrimaryPressed += HandlePrimaryPressed;
            input.PrimaryReleased += HandlePrimaryReleased;
        }

        private void OnDisable()
        {
            if (input == null)
                return;

            input.PrimaryPressed -= HandlePrimaryPressed;
            input.PrimaryReleased -= HandlePrimaryReleased;
        }

        /// Held is polled rather than evented because that is what automatic fire is: the press
        /// event gives the first shot with no delay, and this keeps them coming.
        private void Update()
        {
            if (photonView.IsMine && input != null && input.PrimaryHeld)
                TryFire();
        }

        private void HandlePrimaryPressed()
        {
            triggerHeldSince = Time.time;
            TryFire();
        }

        private void HandlePrimaryReleased() => triggerHeldSince = 0f;

        /// <summary>Swap the active weapon by id - what the shop and the test range call.</summary>
        public void SetWeapon(int weaponId)
        {
            WeaponDefinition next = catalogue != null ? catalogue.Resolve(weaponId) : null;
            if (next == null)
            {
                Debug.LogWarning($"[WeaponFiring] {name}: no weapon with Id {weaponId} in the catalogue - keeping the current one.");
                return;
            }

            EquipWeapon(next);
        }

        private void EquipWeapon(WeaponDefinition next)
        {
            weapon = next;
            if (weapon == null || aim == null)
                return;

            // Points the aim cone at this weapon's own accuracy numbers, which is what PlayerAim's
            // serialized fallback values were always placeholders for.
            aim.ConfigureCone(weapon.MinConeAngle, weapon.MaxConeAngle, weapon.BloomPerShot,
                              weapon.RecoveryPerSecond, weapon.StandingStillMultiplier);
        }

        /// <summary>
        /// Fires if the weapon is ready, this player owns it, is alive and is not overheated.
        /// Public so the test range can drive the exact same path a mouse click does - there is no
        /// second firing route that could behave differently.
        /// </summary>
        public bool TryFire()
        {
            if (!photonView.IsMine || weapon == null || Time.time < nextFireTime)
                return false;
            if (lifecycle != null && !lifecycle.IsAlive)
                return false;
            if (overheat != null && !overheat.CanAct)
                return false;

            nextFireTime = Time.time + weapon.FireInterval;

            // Heat is charged once per TRIGGER PULL, not once per projectile - see the tooltip on
            // Overheat Per Shot. A five-pellet shotgun costs the same heat as a single bullet.
            overheat?.Add(weapon.OverheatPerShot);

            // The cone this shot actually fires through is the one from BEFORE it blooms: holding
            // the trigger costs you the NEXT shot's accuracy, not this one's.
            float coneAngle = aim != null ? aim.EffectiveConeAngle : 0f;
            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            Vector3 direction = aim != null ? aim.AimDirection : transform.forward;
            aim?.RegisterShot();

            photonView.RPC(nameof(RPC_FireWeapon), RpcTarget.AllViaServer, weapon.Id, origin,
                           direction, coneAngle, Random.Range(int.MinValue, int.MaxValue),
                           ChargeFraction());
            return true;
        }

        /// <summary>0..1 for a charge weapon, 0 for everything else. The baseline cannot charge, so
        /// this is plumbing only: the value crosses the wire and scales nothing yet.</summary>
        private float ChargeFraction()
        {
            if (weapon == null || !weapon.CanCharge || weapon.MaxChargeSeconds <= 0f || triggerHeldSince <= 0f)
                return 0f;

            return Mathf.Clamp01((Time.time - triggerHeldSince) / weapon.MaxChargeSeconds);
        }

        /// <summary>
        /// Builds this shot on EVERY client, including the shooter's own. Every value it needs
        /// arrives as a parameter or from info.Sender; nothing in this body reads local state that
        /// would differ per machine.
        ///
        /// coneAngleDegrees is a deviation from the six-parameter signature the task specified, and
        /// it is load-bearing. The seed alone cannot make the spread identical everywhere:
        /// PlayerAim ticks its cone only for its owner (Update early-returns on !IsMine) and the
        /// standing-still bonus depends on movement no other client knows about, so receivers would
        /// roll the same random number against a DIFFERENT cone width and the shots would diverge -
        /// the very bug this class was written to fix. The shooter's cone width is something the
        /// sender meant, so by this file's own rule it travels as a parameter.
        /// </summary>
        [PunRPC]
        private void RPC_FireWeapon(int weaponId, Vector3 origin, Vector3 aimDirection,
                                    float coneAngleDegrees, int seed, float chargeFraction,
                                    PhotonMessageInfo info)
        {
            // By id through the catalogue, never by list position: an id that resolved by order
            // would silently re-map everyone's weapon the moment the list was tidied up.
            WeaponDefinition fired = catalogue != null ? catalogue.Resolve(weaponId) : null;
            if (fired == null)
            {
                Debug.LogWarning($"[WeaponFiring] received a shot with unknown weapon Id {weaponId} - ignoring it.");
                return;
            }

            // From the SENDER. PhotonNetwork.LocalPlayer here would be the receiver, which hands
            // kill credit to the victim - see DamageInfo.SourceActorNumber.
            int shooterActor = info.Sender != null ? info.Sender.ActorNumber : -1;
            Teams.TryGetTeam(info.Sender, out int shooterTeam);

            ProjectileContext[] shots = BuildShots(fired, aimDirection, coneAngleDegrees, seed,
                                                    shooterActor, shooterTeam, chargeFraction);

            if (fired.Simultaneous)
            {
                for (int i = 0; i < shots.Length; i++)
                    Spawn(fired, origin, shots[i]);
            }
            else
            {
                StartCoroutine(SpawnSequentially(fired, origin, shots));
            }

            if (fired.MuzzleVfx != null && VFXManager.Instance != null)
                VFXManager.Instance.PlayVFX(fired.MuzzleVfx, origin);
            if (fired.FireSfx != null && AudioManager.Instance != null)
                AudioManager.Instance.Play3D(fired.FireSfx, origin);
        }

        /// <summary>
        /// The directions for one trigger pull, worked out up front so a burst cannot have its
        /// random sequence disturbed by anything happening between its shots.
        ///
        /// Two separate angles combine here. Spread Degrees is the weapon's FIXED shotgun fan and
        /// is the same on every client by construction. The aim cone is the player's accuracy and
        /// is rolled from a System.Random seeded by a number that crossed the wire, so every client
        /// rolls the identical spread - random spread was the designer's explicit choice, and the
        /// shared seed is what makes it fair rather than chaotic.
        /// </summary>
        private static ProjectileContext[] BuildShots(WeaponDefinition weapon, Vector3 aimDirection,
                                                       float coneAngleDegrees, int seed,
                                                       int shooterActor, int shooterTeam, float chargeFraction)
        {
            int count = Mathf.Max(1, weapon.ProjectilesPerShot);
            var rng = new System.Random(seed);

            // A cone pinned at exactly the width the shooter fired with, so the tested sampler in
            // AimConeState is reused rather than its maths re-derived here.
            var cone = new AimConeState(coneAngleDegrees, coneAngleDegrees, 0f, 0f, 1f);

            var shots = new ProjectileContext[count];
            for (int i = 0; i < count; i++)
            {
                float degrees = FanOffset(weapon, i, count) + cone.SampleOffsetDegrees(rng);
                Vector3 direction = Quaternion.AngleAxis(degrees, Vector3.up) * aimDirection;
                shots[i] = new ProjectileContext(weapon, shooterActor, shooterTeam, direction,
                                                  chargeFraction, weapon.Damage);
            }

            return shots;
        }

        /// <summary>Where this pellet sits in the shotgun fan: evenly spaced across Spread Degrees,
        /// centred on the aim. Only meaningful for a simultaneous weapon; a burst fans by nothing
        /// and separates its shots in time instead.</summary>
        private static float FanOffset(WeaponDefinition weapon, int index, int count)
        {
            if (!weapon.Simultaneous || count <= 1 || weapon.SpreadDegrees <= 0f)
                return 0f;

            return -weapon.SpreadDegrees * 0.5f + weapon.SpreadDegrees * index / (count - 1);
        }

        private IEnumerator SpawnSequentially(WeaponDefinition weapon, Vector3 origin, ProjectileContext[] shots)
        {
            for (int i = 0; i < shots.Length; i++)
            {
                Spawn(weapon, origin, shots[i]);
                if (i + 1 < shots.Length)
                    yield return new WaitForSeconds(weapon.SequentialDelay);
            }
        }

        /// A plain local Instantiate, NOT PhotonNetwork.Instantiate - so assigning to the returned
        /// object through Initialize configures the one object that exists. See ProjectileContext.
        private void Spawn(WeaponDefinition weapon, Vector3 origin, ProjectileContext shot)
        {
            if (weapon.ProjectilePrefab == null)
            {
                Debug.LogError($"[WeaponFiring] weapon '{weapon.name}' has no Projectile Prefab - nothing was fired.");
                return;
            }

            GameObject projectile = Instantiate(weapon.ProjectilePrefab, origin,
                                                 Quaternion.LookRotation(shot.Direction));
            ProjectileMotor motor = projectile.GetComponent<ProjectileMotor>();
            if (motor == null)
            {
                Debug.LogError($"[WeaponFiring] projectile prefab '{weapon.ProjectilePrefab.name}' has no " +
                                "ProjectileMotor - destroying it rather than leaking it.");
                Destroy(projectile);
                return;
            }

            motor.Initialize(shot);
        }
    }
}
