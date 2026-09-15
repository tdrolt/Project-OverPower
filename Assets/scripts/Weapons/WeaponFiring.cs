using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;
using Overpower.UI;

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
                 "the test range replace it at runtime through PlayerLoadout.")]
        private WeaponDefinition startingWeapon;

        [SerializeField, Tooltip("Where projectiles leave the gun. Falls back to the player's own " +
                 "position if it is empty, which looks wrong but still fires.")]
        private Transform muzzle;

        [SerializeField, Tooltip("Where a laser's wind-up warning line gets its team colour and its " +
                 "width/alpha/material numbers from - Assets/Gameplay/Config/UiTheme.asset, shared " +
                 "with the HUD, the aim cone and every shot's trail (Task 11b). Presentation only; " +
                 "every weapon still fires with this left empty, the warning line just falls back " +
                 "to plain numbers and no material instead of the theme's.")]
        private UiTheme theme;

        [Header("Wall-hugging clearance (review finding, Task 1.9 follow-up)")]
        [SerializeField, Tooltip("Radius, in metres, of the clearance check between the player's " +
                 "body and the muzzle tip - approximately a projectile's own radius. The muzzle sits " +
                 "roughly 1.36m in front of the root (about 0.66m past a 0.7m capsule); a player " +
                 "standing flush against a wall or thin cover pushes that point INSIDE or THROUGH it, " +
                 "and Physics.Raycast/SphereCast never report a collider their own origin already " +
                 "starts inside - so a shot, beam or the flamethrower's occlusion check fired from " +
                 "the raw muzzle sailed straight through the wall it was touching (measured: the " +
                 "laser and the flamethrower leaked through a real Wall_01, and everything but the " +
                 "stun gun leaked through the thinner Deployable Cover). See SafeMuzzlePosition.")]
        private float muzzleClearanceRadius = 0.15f;

        [SerializeField, Tooltip("How far, in metres, a blocked origin is pulled back from the wall " +
                 "toward the player's own body, so the shot starts on the near side and hits the " +
                 "wall immediately instead of spawning inside or beyond it.")]
        private float muzzleClearanceSkin = 0.05f;

        // Not a design tunable: whatever layer every wall and every piece of cover already stands
        // on (CoverWall.cs's own class comment) is what a wall-hugging shot must be pulled back
        // from - the same layer FlamethrowerAbility's own occlusion check and ExplodeOnImpact's
        // splash check already use. Computed once since NameToLayer never changes at runtime.
        private int buildingMask;

        private PlayerAim aim;
        private PlayerOverheat overheat;
        private PlayerLifecycle lifecycle;
        private PlayerInputRouter input;
        private PlayerStatusEffects statusEffects;

        private WeaponDefinition weapon;
        private float nextFireTime;
        private float triggerHeldSince;

        public WeaponDefinition Weapon => weapon;

        /// <summary>Where the muzzle currently sits in world space, unclamped - the Transform's own
        /// point, used for cosmetics (muzzle flash placement point before the clearance pull-back)
        /// and by SafeMuzzlePosition below. Never the origin a shot or a cast should actually use by
        /// itself - see SafeMuzzlePosition's own comment. Does not change the muzzle's own height or
        /// its open-ground position - the shotgun spread was tuned from this exact point.</summary>
        public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

        /// <summary>
        /// THE origin every shot and every ability cast should use - AbilityRunner.CastContext.Muzzle
        /// and TryFire's own origin below both read this, never MuzzlePosition directly (Task 1.9
        /// follow-up review finding).
        ///
        /// Sweeps a small sphere from the player's own root, raised to muzzle HEIGHT, out to the raw
        /// muzzle point, against Building only (triggers ignored). In the open this hits nothing and
        /// returns the raw muzzle unchanged - the normal case, and the one the shotgun's spread and
        /// every other weapon number was tuned against. Hugging a wall or a piece of cover puts that
        /// short hop through it, so the origin is pulled back to the sweep's contact point, minus
        /// Muzzle Clearance Skin toward the body - on the NEAR side of the wall, exactly where a
        /// gun barrel actually stops when its owner is pressed up against something solid.
        ///
        /// Deliberately does not touch the muzzle's HEIGHT or its position over open ground - only
        /// the wall-hugging case is affected, so the earlier measured TTK, shotgun spread and laser
        /// range all stay valid.
        /// </summary>
        public Vector3 SafeMuzzlePosition
        {
            get
            {
                Vector3 raw = MuzzlePosition;
                Vector3 bodyAtMuzzleHeight = new Vector3(transform.position.x, raw.y, transform.position.z);
                Vector3 toMuzzle = raw - bodyAtMuzzleHeight;
                float distance = toMuzzle.magnitude;
                if (distance <= 0.0001f)
                    return raw;

                Vector3 direction = toMuzzle / distance;
                if (Physics.SphereCast(bodyAtMuzzleHeight, muzzleClearanceRadius, direction,
                                       out RaycastHit hit, distance, buildingMask, QueryTriggerInteraction.Ignore))
                {
                    float pulledBack = Mathf.Max(0f, hit.distance - muzzleClearanceSkin);
                    return bodyAtMuzzleHeight + direction * pulledBack;
                }

                return raw;
            }
        }

        private void Awake()
        {
            aim = GetComponent<PlayerAim>();
            overheat = GetComponent<PlayerOverheat>();
            lifecycle = GetComponent<PlayerLifecycle>();
            input = GetComponent<PlayerInputRouter>();
            statusEffects = GetComponent<PlayerStatusEffects>();
            buildingMask = LayerMask.GetMask("Building");

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
        ///
        /// A charging weapon is excluded here on purpose - see HandlePrimaryPressed/Released. Held
        /// automatic fire would otherwise spam a zero-charge shot on every single frame the player
        /// is trying to hold the trigger down to charge one.
        private void Update()
        {
            if (photonView.IsMine && input != null && input.PrimaryHeld &&
                (weapon == null || !weapon.CanCharge))
                TryFire();
        }

        /// <summary>A charging weapon fires nothing on press - it only starts the clock
        /// ChargeFraction() reads. See HandlePrimaryReleased for where the shot actually leaves.
        /// Everything else fires immediately on press, exactly as before charging existed.</summary>
        private void HandlePrimaryPressed()
        {
            triggerHeldSince = Time.time;

            if (weapon != null && weapon.CanCharge)
                return;

            TryFire();
        }

        /// <summary>Where a charging weapon actually fires - with whatever charge the hold reached.
        /// TryFire must run BEFORE triggerHeldSince is cleared, since ChargeFraction() below reads
        /// it to work out how long the trigger was held.</summary>
        private void HandlePrimaryReleased()
        {
            if (weapon != null && weapon.CanCharge)
                TryFire();

            triggerHeldSince = 0f;
        }

        /// <summary>
        /// Swap the active weapon by id, on THIS machine only - apply-only. PlayerLoadout is the
        /// only caller: it is what publishes the change so every other client (and a late joiner)
        /// swaps too. Calling this directly from a shop or a tool would change your own screen and
        /// nobody else's. False, with the current weapon kept, for an id the catalogue does not know.
        /// </summary>
        public bool SetWeapon(int weaponId)
        {
            WeaponDefinition next = catalogue != null ? catalogue.Resolve(weaponId) : null;
            if (next == null)
            {
                Debug.LogWarning($"[WeaponFiring] {name}: no weapon with Id {weaponId} in the catalogue - keeping the current one.");
                return false;
            }

            EquipWeapon(next);
            return true;
        }

        private void EquipWeapon(WeaponDefinition next)
        {
            weapon = next;
            if (weapon == null || aim == null)
                return;

            // Points the aim cone at this weapon's own accuracy numbers, which is what PlayerAim's
            // serialized fallback values were always placeholders for.
            aim.ConfigureCone(weapon.MinConeAngle, weapon.MaxConeAngle, weapon.BloomPerShot,
                              weapon.RecoveryPerSecond, weapon.StandingStillMultiplier,
                              weapon.MovingSpreadDegrees, weapon.MovingBloomPerSecond);
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

            // The same rule AbilityRunner gates abilities on: dead beats stunned beats silenced.
            // Routing the trigger through CastGate instead of this class's own if-chain is what
            // keeps "can this player act right now" from drifting between the weapon and whatever
            // ability checks it next - a stun that should freeze a dash must freeze the gun too.
            bool alive = lifecycle == null || lifecycle.IsAlive;
            bool stunned = statusEffects != null && statusEffects.IsStunned;
            bool silenced = overheat != null && overheat.IsSilenced;
            if (CastGate.ForActor(alive, stunned, silenced) != CastBlock.None)
                return false;

            // Resolved BEFORE nextFireTime is overwritten below. ChargeFraction() reads nextFireTime
            // as the deadline the hold had to wait out - the whole point of the c268b40 fix. Once
            // this shot's own cooldown is written to that same field it points at the NEXT shot's
            // deadline instead, which is always later than Time.time, which clamped the fraction to
            // 0 on every single release. That made c268b40 inert: the formula was right, but by the
            // time it read nextFireTime, this line had already moved the goalposts.
            float chargeFraction = ChargeFraction();

            nextFireTime = Time.time + weapon.FireInterval;

            // Heat is charged once per TRIGGER PULL, not once per projectile - see the tooltip on
            // Overheat Per Shot. A five-pellet shotgun costs the same heat as a single bullet.
            overheat?.Add(weapon.OverheatPerShot);

            // The cone this shot actually fires through is the one from BEFORE it blooms: holding
            // the trigger costs you the NEXT shot's accuracy, not this one's.
            float coneAngle = aim != null ? aim.EffectiveConeAngle : 0f;
            // SafeMuzzlePosition, not the raw muzzle - a shot fired flush against a wall must start
            // on the near side of it, or it spawns inside/through the wall and hits whatever is on
            // the other side for free. See that property's own comment (Task 1.9 follow-up finding).
            Vector3 origin = SafeMuzzlePosition;
            Vector3 direction = aim != null ? aim.AimDirection : transform.forward;

            // Where the cursor is resting on the ground, for the weapons that detonate at a POINT
            // rather than on whatever they run into. It has to be read here and sent as a
            // parameter: PlayerAim resolves it from Camera.main and the mouse, both of which
            // answer for the RECEIVER inside an RPC body. Weapons that do not aim at a point
            // carry it and ignore it, which costs one Vector3 on the wire and saves a second RPC.
            Vector3 targetPoint = aim != null ? aim.GroundPointUnderCursor
                                              : origin + direction * weapon.MaxRange;
            aim?.RegisterShot();

            int seed = Random.Range(int.MinValue, int.MaxValue);
            RefundHeatIfBeamConnects(origin, direction, targetPoint, coneAngle, seed, chargeFraction);

            photonView.RPC(nameof(RPC_FireWeapon), RpcTarget.AllViaServer, weapon.Id, origin,
                           direction, targetPoint, coneAngle, seed, chargeFraction);
            return true;
        }

        /// <summary>
        /// The laser's overheat rule from the GDD: every shot costs double heat (already added
        /// above), and half of it comes back if the beam connects with a target.
        ///
        /// The SHOOTER decides, from its own ray, the instant it fires. Heat is local state that
        /// only its owner ever reads, so nothing about the refund crosses the network. The ray is
        /// the same one every client will cast: BuildShots is fed the same seed and cone that are
        /// about to go into the RPC, so the aim-cone roll lands on the identical direction.
        ///
        /// ACCEPTED TRADEOFF: damage is decided on the victim's client, the refund on the shooter's.
        /// Under latency the two can disagree - the shooter sees the beam cross a target that, on
        /// the target's own screen, had already stepped aside - and the shooter gets the refund
        /// for a hit that dealt no damage. The alternative, waiting for the victim to confirm,
        /// needs a reply message per hit for a few points of heat. Revisit after a real-latency test.
        ///
        /// Projectile weapons are untouched: they land later, on every client, and no projectile
        /// weapon has a refund today.
        /// </summary>
        private void RefundHeatIfBeamConnects(Vector3 origin, Vector3 direction, Vector3 targetPoint,
                                              float coneAngle, int seed, float chargeFraction)
        {
            if (overheat == null || weapon.OverheatRefundOnHit <= 0f || weapon.ProjectilePrefab == null)
                return;

            Hitscan beam = weapon.ProjectilePrefab.GetComponent<Hitscan>();
            if (beam == null)
                return;

            // Outside an RPC body, so LocalPlayer really is the shooter here - these are the same
            // values RPC_FireWeapon will read from info.Sender on every other machine.
            int shooterActor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int shooterTeam);

            ProjectileContext[] shots = BuildShots(weapon, direction, targetPoint, coneAngle, seed,
                                                    shooterActor, shooterTeam, chargeFraction);
            for (int i = 0; i < shots.Length; i++)
            {
                if (beam.Resolve(origin, shots[i]).Connected)
                    overheat.Refund(weapon.OverheatRefundOnHit);
            }
        }

        /// <summary>0..1 for a charge weapon, 0 for everything else.
        ///
        /// Charge only starts accumulating once the weapon is off cooldown. Measuring from the moment
        /// of the press instead let a player hold straight through the Fire Interval they had to wait
        /// anyway, so the charge time cost nothing and a full charge was strictly better than a tap:
        /// the burst charge path measured a 1.54s kill against a 2-3s design intent. The charge time is
        /// meant to BE the price of the payoff, so it has to come after the cooldown, not inside it.</summary>
        private float ChargeFraction()
        {
            if (weapon == null || !weapon.CanCharge || weapon.MaxChargeSeconds <= 0f || triggerHeldSince <= 0f)
                return 0f;

            float chargeStart = Mathf.Max(triggerHeldSince, nextFireTime);
            return Mathf.Clamp01((Time.time - chargeStart) / weapon.MaxChargeSeconds);
        }

        /// <summary>Read-only mirror of ChargeFraction() for anything that only needs to DRAW the
        /// current charge - today AimConeView, for the range arc on a charging beam weapon - without
        /// duplicating or changing that method's own logic (its trap is documented on its own
        /// comment above: charge must be read before nextFireTime is overwritten, which this getter
        /// does not touch either way).</summary>
        public float CurrentChargeFraction => ChargeFraction();

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
                                    Vector3 targetPoint, float coneAngleDegrees, int seed,
                                    float chargeFraction, PhotonMessageInfo info)
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

            ProjectileContext[] shots = BuildShots(fired, aimDirection, targetPoint, coneAngleDegrees,
                                                    seed, shooterActor, shooterTeam, chargeFraction);

            // Task 11b (design [T] - Tudor reversed the earlier "no warning" call, see the comment on
            // IgnoreWalls.cs): a weapon with a wind-up shows its warning line(s) now and only fires
            // for real once that wait is over. Everything else fires exactly as it always has.
            if (fired.WindupSeconds > 0f)
                StartCoroutine(FireAfterWindup(fired, origin, shots, shooterTeam));
            else
                DispatchShots(fired, origin, shots);

            if (fired.MuzzleVfx != null && VFXManager.Instance != null)
                VFXManager.Instance.PlayVFX(fired.MuzzleVfx, origin);
            if (fired.FireSfx != null && AudioManager.Instance != null)
                AudioManager.Instance.Play3D(fired.FireSfx, origin);
        }

        /// <summary>What RPC_FireWeapon always did before Task 11b added the wind-up - fire every
        /// shot now, simultaneously or spaced by Sequential Delay. Pulled out on its own so a
        /// wind-up weapon and an instant one both end up calling exactly this, rather than the two
        /// paths drifting apart.</summary>
        private void DispatchShots(WeaponDefinition weapon, Vector3 origin, ProjectileContext[] shots)
        {
            if (weapon.Simultaneous)
            {
                for (int i = 0; i < shots.Length; i++)
                    Spawn(weapon, origin, shots[i]);
            }
            else
            {
                StartCoroutine(SpawnSequentially(weapon, origin, shots));
            }
        }

        /// <summary>
        /// Task 11b: shows a warning line along each shot's already-locked path, waits Windup
        /// Seconds, then fires exactly as DispatchShots always has. Runs on EVERY client, including
        /// the shooter's own - each machine counts its own wind-up from the moment IT received this
        /// RPC, so a target who steps out of the line during the wait takes no damage on ITS OWN
        /// client the instant the beam actually resolves (damage is victim-side - see Hitscan's
        /// class comment) even though the shooter's own screen already shows the beam connecting.
        ///
        /// Origin and every shot's direction are already locked - they arrived as RPC parameters,
        /// same as any other shot this class fires - so nothing here can change what the beam does;
        /// only whether the player caught in it had a chance to move first.
        ///
        /// ACCEPTED [C]: if this WeaponFiring is destroyed mid-wind-up (its player despawns), this
        /// coroutine dies with it and the beam never fires on THIS client - the same way any other
        /// coroutine on a destroyed MonoBehaviour stops. A shooter who merely dies or is stunned
        /// (without despawning) still gets their beam off once the wait ends, the same way a bullet
        /// already in flight keeps flying - this class has no hook that would stop it, and design
        /// [C] says it should not gain one.
        /// </summary>
        private IEnumerator FireAfterWindup(WeaponDefinition weapon, Vector3 origin,
                                            ProjectileContext[] shots, int shooterTeam)
        {
            LaserWarningLine[] warnings = ShowWarnings(weapon, origin, shots, shooterTeam);

            yield return new WaitForSeconds(weapon.WindupSeconds);

            for (int i = 0; i < warnings.Length; i++)
            {
                if (warnings[i] != null)
                    Destroy(warnings[i].gameObject);
            }

            DispatchShots(weapon, origin, shots);
        }

        /// <summary>One warning line per shot, along the exact path that shot will travel - drawn
        /// only for a beam weapon (Hitscan on its Projectile Prefab); a projectile weapon given a
        /// wind-up for some future design would otherwise have nothing sensible to draw a line
        /// toward, since a bullet's path is not a straight ray to a fixed stop point the way a beam's
        /// is. Nothing gives a projectile weapon a wind-up today, so this never actually happens.</summary>
        private LaserWarningLine[] ShowWarnings(WeaponDefinition weapon, Vector3 origin,
                                                ProjectileContext[] shots, int shooterTeam)
        {
            var warnings = new LaserWarningLine[shots.Length];

            Hitscan beam = weapon.ProjectilePrefab != null
                ? weapon.ProjectilePrefab.GetComponent<Hitscan>()
                : null;
            if (beam == null)
                return warnings;

            Color color = ResolveWarningColor(shooterTeam);
            for (int i = 0; i < shots.Length; i++)
            {
                float length = beam.PredictBeamLength(origin, shots[i]);
                warnings[i] = LaserWarningLine.Create(origin, shots[i].Direction, length, color,
                                                      weapon.WindupSeconds, theme);

                // Quality review finding: parented to the SHOOTER, not left as a loose root object.
                // FireAfterWindup only destroys this line after its own WaitForSeconds finishes, and
                // that coroutine dies silently (never reaching the Destroy call) if this WeaponFiring
                // is destroyed first - a player despawning (leaving the room; PUN cleans it up) mid
                // wind-up. Parenting means the warning line dies WITH the shooter's object instead of
                // being orphaned on screen at full width forever. worldPositionStays: true because
                // the line is drawn in world space (LaserWarningLine.Initialize) - reparenting must
                // not move it.
                warnings[i].transform.SetParent(transform, true);
            }

            return warnings;
        }

        // Logged once per play session, not once per shot - see ShotTeamVisuals/Hitscan's own copies
        // of this same guard for the same reason.
        private static bool warnedMissingTheme;

        private Color ResolveWarningColor(int shooterTeam)
        {
            if (theme != null)
                return theme.ShotColorFor(shooterTeam);

            if (!warnedMissingTheme)
            {
                warnedMissingTheme = true;
                Debug.LogWarning($"[WeaponFiring] {name}: no UI Theme assigned - laser warning lines " +
                                  "draw in plain white instead of the shooter's team colour.");
            }
            return Color.white;
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
        ///
        /// Charge scales two things here, both derived from the one chargeFraction parameter that
        /// already crosses the wire - no second RPC value was needed for either.
        /// </summary>
        private static ProjectileContext[] BuildShots(WeaponDefinition weapon, Vector3 aimDirection,
                                                       Vector3 targetPoint, float coneAngleDegrees,
                                                       int seed, int shooterActor, int shooterTeam,
                                                       float chargeFraction)
        {
            int count = ChargedProjectileCount(weapon, chargeFraction);
            float damage = ChargedDamage(weapon, chargeFraction);
            var rng = new System.Random(seed);

            // A cone pinned at exactly the width the shooter fired with, so the tested sampler in
            // AimConeState is reused rather than its maths re-derived here.
            var cone = new AimConeState(coneAngleDegrees, coneAngleDegrees, 0f, 0f, 1f);

            var shots = new ProjectileContext[count];
            for (int i = 0; i < count; i++)
            {
                float degrees = FanOffset(weapon, i, count) + cone.SampleOffsetDegrees(rng);
                Vector3 direction = Quaternion.AngleAxis(degrees, Vector3.up) * aimDirection;
                // Every pellet of one trigger pull shares the same target point - the cursor was
                // in one place when the trigger went down, whatever the spread did to each
                // projectile's heading afterwards.
                shots[i] = new ProjectileContext(weapon, shooterActor, shooterTeam, direction,
                                                  targetPoint, chargeFraction, damage);
            }

            return shots;
        }

        /// <summary>
        /// How many projectiles this trigger pull sends out. Weapons that cannot charge, or that
        /// charge something other than their projectile count (Charge Max Projectiles left at 0),
        /// are untouched - they always send Projectiles Per Shot.
        ///
        /// Charge Steps quantises the ramp into readable stages rather than a smooth count a
        /// player cannot react to - see the tooltip on WeaponDefinition.ChargeSteps. Two steps
        /// means three levels (0, 1, 2), which for the burst charge path reads as 3 -> 4 -> 5
        /// projectiles: a HALF charge is a real, intentional middle step, not a rounding accident.
        /// </summary>
        private static int ChargedProjectileCount(WeaponDefinition weapon, float chargeFraction)
        {
            if (!weapon.CanCharge || weapon.ChargeMaxProjectiles <= weapon.ProjectilesPerShot)
                return Mathf.Max(1, weapon.ProjectilesPerShot);

            float quantised = QuantiseChargeFraction(chargeFraction, weapon.ChargeSteps);
            return Mathf.Max(1, Mathf.RoundToInt(
                Mathf.Lerp(weapon.ProjectilesPerShot, weapon.ChargeMaxProjectiles, quantised)));
        }

        /// <summary>Snaps a continuous 0..1 hold to the nearest of Charge Steps + 1 even levels
        /// (0, 1/steps, 2/steps, ... 1), so a half-second hold on a 1-second charge lands on
        /// exactly the same level every time rather than drifting with frame timing. Steps of 0
        /// or less leaves the fraction smooth, for a weapon that charges something continuous
        /// (damage only) rather than in stages.</summary>
        private static float QuantiseChargeFraction(float chargeFraction, int steps)
        {
            float clamped = Mathf.Clamp01(chargeFraction);
            return steps > 0 ? Mathf.RoundToInt(clamped * steps) / (float)steps : clamped;
        }

        /// <summary>Damage ramps smoothly (not stepped) towards Charge Damage Multiplier, since
        /// nothing asked for readable damage stages the way the projectile count needs them - only
        /// the count is a number a player can visibly count leaving the barrel.</summary>
        private static float ChargedDamage(WeaponDefinition weapon, float chargeFraction)
        {
            if (!weapon.CanCharge)
                return weapon.Damage;

            return weapon.Damage * Mathf.Lerp(1f, weapon.ChargeDamageMultiplier, Mathf.Clamp01(chargeFraction));
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

            // A beam, not a projectile: resolved instantly on this client and nothing is spawned.
            // Checked here rather than in the RPC so a beam weapon still honours Simultaneous and
            // Sequential Delay like any other weapon. See Hitscan.
            Hitscan beam = weapon.ProjectilePrefab.GetComponent<Hitscan>();
            if (beam != null)
            {
                beam.Fire(origin, shot);
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
