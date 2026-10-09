using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;
using Overpower.UI;
using Overpower.Vision;

namespace Overpower.Weapons
{
    /// <summary>
    /// One player's trigger. THE RULE: anything the sender meant travels as an RPC PARAMETER or comes from PhotonMessageInfo.Sender. Never read
    /// transform, Camera.main, Input.mousePosition or PhotonNetwork.LocalPlayer inside an RPC body and expect the sender's values - they resolve
    /// on the RECEIVING machine, so shots visibly diverge between clients.
    /// Projectiles are simulated locally on every client and are NOT networked objects (PhotonNetwork.Instantiate per bullet would be dozens of
    /// networked objects a second for nine players); the fire RPC carries everything a receiver needs to build the identical shot.
    /// Deliberately NOT IPunObservable: the player's PhotonView uses AutoFindAll and would silently absorb a second observable. PlayerNetSync is the only one.
    /// </summary>
    public class WeaponFiring : MonoBehaviourPun
    {
        [SerializeField, Tooltip("Every weapon in the game. Weapon ids arriving over the network " +
                 "are resolved through this, never by position in its list.")]
        private WeaponCatalogue catalogue;

        [SerializeField, Tooltip("The weapon this player starts the match holding. The shop and " +
                 "the test range replace it at runtime through PlayerLoadout.")]
        private WeaponDefinition startingWeapon;

        // The muzzle's one home is its Transform on Assets/Resources/Multiplayer Player.prefab - tune
        // it by moving that child. It sits just inside the player's body capsule; the shot logic
        // ignores the shooter, so that is safe.
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
                 "just inside the body capsule, but the check still reaches past the capsule's surface; " +
                 "a player standing flush against a wall or thin cover pushes that point INSIDE or THROUGH it, " +
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

        // Not a design tunable: the layer every wall and piece of cover stands on (CoverWall.cs), which a wall-hugging shot is pulled back from - the
        // same layer FlamethrowerAbility's occlusion check and ExplodeOnImpact's splash check use. Computed once since NameToLayer never changes at runtime.
        private int buildingMask;

        private PlayerAim aim;
        private PlayerOverheat overheat;
        private PlayerLifecycle lifecycle;
        private PlayerInputRouter input;
        private PlayerStatusEffects statusEffects;

        private WeaponDefinition weapon;
        private float nextFireTime;
        private float triggerHeldSince;

        /// <summary>True only when input.PrimaryHeld was ALSO true on the immediately preceding Update tick, never on the tick a press or re-click starts
        /// a hold. Update's held-fire path feeds it into FireScheduleRule.IsContinuingHold so a fresh click is never mistaken for a continuing hold.</summary>
        private bool heldLastFrame;

        // OverPower's comeback buff (GDD p.20) scales this player's own damage, fire rate and range while active. Owner state only, set through
        // SetStatMultipliers: a shot's damage and range must still be IDENTICAL on every client, so they cross the wire as RPC_FireWeapon parameters
        // rather than being read locally by a receiver. 1 = unchanged, the buff's "off" state.
        private float damageMultiplier = 1f;
        private float fireRateMultiplier = 1f;
        private float rangeMultiplier = 1f;

        public WeaponDefinition Weapon => weapon;

        /// <summary>Raised on the shooter's own client the moment a shot is actually committed: once for a simultaneous (shotgun-style) pull with its full
        /// pellet count, or once per round for a burst/sequential weapon, so an interrupted burst only counts the rounds that left the gun (see
        /// DispatchShots/SpawnSequentially).
        /// newPull is true exactly once per TRIGGER PULL: always for a Simultaneous weapon, only for round 0 of a burst. Without it PlayerTelemetry's
        /// "pulls" counter counted every burst round as its own pull. PlayerTelemetry is the only subscriber today.</summary>
        public event System.Action<int, int, bool> Fired;

        /// <summary>Guarded on IsMine so this never fires with no possible correct listener - every
        /// client runs RPC_FireWeapon (and so DispatchShots) for every shot it hears about, the
        /// shooter's own included, but only the shooter's own copy of this event means anything.</summary>
        private void RaiseFired(int weaponId, int projectileCount, bool newPull)
        {
            if (photonView.IsMine)
                Fired?.Invoke(weaponId, projectileCount, newPull);
        }

        /// <summary>The shooter's own current range multiplier (1 = unchanged) - AimConeView reads
        /// this to draw the SAME multiplied range a real shot would travel, the same way it already
        /// reads CurrentChargeFraction for a charging beam's arc.</summary>
        public float CurrentRangeMultiplier => rangeMultiplier;

        /// <summary>
        /// OverPowerBuff's hook (GDD p.20): while the comeback buff is active this player's primary fires harder, faster and further; when it ends every
        /// multiplier goes back to 1 (= unchanged for all three). Owner-only state, like triggerHeldSince: a remote copy never runs TryFire
        /// (photonView.IsMine guards it) and has no OverPowerBuff of its own driving this player's stats.
        /// </summary>
        public void SetStatMultipliers(float damage, float fireRate, float range)
        {
            // Clamped the same defensive way ArmorState guards a zero refillSeconds, though nothing calls this with a negative today: a negative damage or
            // range multiplier would read as healing or a shot travelling backwards, and a zero or negative fire-rate multiplier would divide nextFireTime's
            // interval by zero or flip the cooldown negative.
            damageMultiplier = Mathf.Max(0f, damage);
            fireRateMultiplier = Mathf.Max(0.0001f, fireRate);
            rangeMultiplier = Mathf.Max(0f, range);
        }

        /// <summary>Where the muzzle currently sits in world space, unclamped: for cosmetics and as SafeMuzzlePosition's input. Never the origin a shot or
        /// cast should use by itself - see SafeMuzzlePosition. Muzzle height and open-ground position are unchanged; the shotgun spread was tuned from
        /// this exact point.</summary>
        public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

        /// <summary>
        /// THE origin every shot and every ability cast should use - AbilityRunner.CastContext.Muzzle and TryFire both read this, never MuzzlePosition directly.
        /// Sweeps a small sphere from the player's own root, raised to muzzle HEIGHT, out to the raw muzzle point, against Building only (triggers
        /// ignored). In the open it hits nothing and returns the raw muzzle unchanged - the case the shotgun's spread and every other weapon number were
        /// tuned against. Hugging a wall or cover puts that hop through it, so the origin is pulled back to the sweep's contact point minus Muzzle
        /// Clearance Skin toward the body: the NEAR side of the wall, where a gun barrel actually stops when its owner is pressed against something solid.
        /// Muzzle height and open-ground position are never touched, so the measured TTK, shotgun spread and laser range stay valid.
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
            // PlayerLifecycle disables this component on death (weaponFiring.enabled = alive). Without this reset, a charge weapon held right up to a
            // death kept triggerHeldSince set and respawn re-enabled the component with that stale value, so AimConeView's range arc
            // (CurrentChargeFraction) read a leftover, possibly full, charge the player never held on the new life. Cleared before the null-check
            // since it must happen whether or not input ever resolved.
            triggerHeldSince = 0f;

            // Same reasoning as triggerHeldSince above: a stale true here would let re-enabling
            // (e.g. on respawn) treat the very first frame as a continuing hold before the trigger
            // has actually been held across a frame boundary on the new life.
            heldLastFrame = false;

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
            // Opening a tool (P for the loadout screen, F1 for the test range) while holding a charge weapon's trigger sets InputSuppressed, which
            // swallows PrimaryReleased like every other router event, so HandlePrimaryReleased - the only other place triggerHeldSince is cleared -
            // never runs. Left alone, ChargeFraction() (and CurrentChargeFraction, which AimConeView reads for the range arc) kept reporting a growing
            // charge the player no longer held. Cleared here every frame input stays suppressed, WITHOUT calling TryFire: a real release fires a
            // charge weapon, a suppressed one must not.
            if (photonView.IsMine && input != null && input.InputSuppressed && triggerHeldSince != 0f)
                triggerHeldSince = 0f;

            // heldLastFrame is read here before it is overwritten below: TryFire is told whether the trigger was ALSO held on the PREVIOUS tick, not just
            // "this weapon can't charge", so a fresh click's own first held-fire tick is never mistaken for a continuing hold (FireScheduleRule.IsContinuingHold).
            bool heldNow = photonView.IsMine && input != null && input.PrimaryHeld &&
                          (weapon == null || !weapon.CanCharge);
            if (heldNow)
                TryFire(continuingHold: heldLastFrame);
            heldLastFrame = heldNow;
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

        /// <summary>Where a charging weapon actually fires - with whatever charge the hold reached. TryFire must run BEFORE triggerHeldSince is
        /// cleared, since ChargeFraction() reads it to work out how long the trigger was held.
        /// PlayerInputRouter deliberately does NOT pointer-gate the release event the way it gates the press (see EmitPointerGated: gating release
        /// risked a stuck-held weapon), so releasing the mouse over the "Loadout (P)" button still reaches here though the matching PRESS was blocked
        /// and never ran HandlePrimaryPressed. Without the triggerHeldSince > 0f guard below, that pair fired an uncharged shot through the button on
        /// every click. triggerHeldSince is 0 whenever the matching press never ran (HandlePrimaryPressed is the only place that sets it; this method's
        /// own clear and Update's InputSuppressed clear both leave it at 0), so the guard is exactly "did a real press start this hold".</summary>
        private void HandlePrimaryReleased()
        {
            if (weapon != null && weapon.CanCharge && triggerHeldSince > 0f)
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

            // Points the aim cone at this weapon's own accuracy numbers (PlayerAim's serialized values are only placeholders).
            aim.ConfigureCone(weapon.MinConeAngle, weapon.MaxConeAngle, weapon.BloomPerShot,
                              weapon.RecoveryPerSecond, weapon.StandingStillMultiplier,
                              weapon.MovingSpreadDegrees, weapon.MovingBloomPerSecond);
        }

        /// <summary>
        /// Fires if the weapon is ready, this player owns it, is alive and is not overheated. Public so the test range drives the exact same path a
        /// mouse click does - there is no second firing route. Always a fresh, non-continuing call (a click is never a continuing hold); Update's
        /// held-fire path calls the private overload below with whatever heldLastFrame actually is.
        /// </summary>
        public bool TryFire() => TryFire(continuingHold: false);

        private bool TryFire(bool continuingHold)
        {
            if (!photonView.IsMine || weapon == null || Time.time < nextFireTime)
                return false;

            // Re-read every tick (cheap) so a fire-rate buff that changes mid-hold is reflected
            // immediately in both the blocked-tick clamp below and the real fire decision further
            // down, rather than only from the next full cycle.
            float interval = weapon.FireInterval / fireRateMultiplier;

            // The same rule AbilityRunner gates abilities on: dead beats stunned beats silenced.
            // Routing the trigger through CastGate instead of this class's own if-chain is what
            // keeps "can this player act right now" from drifting between the weapon and whatever
            // ability checks it next - a stun that should freeze a dash must freeze the gun too.
            bool alive = lifecycle == null || lifecycle.IsAlive;
            bool stunned = statusEffects != null && statusEffects.IsStunned;
            bool silenced = overheat != null && overheat.IsSilenced;
            if (CastGate.ForActor(alive, stunned, silenced) != CastBlock.None)
            {
                // Without this, a blocked player's stale nextFireTime sat wherever it was left when the block started, and the first tick after the block
                // lifted could misread as "still mid-cadence" and fire a shot only a sliver of an interval later - see FireScheduleRule.
                nextFireTime = FireScheduleRule.NextFireTime(nextFireTime, Time.time, interval,
                                                              triggerHeldContinuously: false, blockedThisTick: true);
                return false;
            }

            // Resolved BEFORE nextFireTime is overwritten below: ChargeFraction() reads nextFireTime as the deadline the hold had to wait out. Once this
            // shot's own cooldown is written there it points at the NEXT shot's deadline, always later than Time.time, which clamps the fraction to 0 on
            // every single release.
            float chargeFraction = ChargeFraction();

            // Carries the previous shot's schedule forward instead of rebasing off Time.time while the trigger is genuinely held continuously (not a
            // charge weapon's one-off release - see FireScheduleRule). TryFire only runs once per rendered frame, so rebasing capped a buffed fast weapon
            // at once a frame (weapon 09's buffed interval is shorter than a frame): "now" already carries however late THIS frame's shot landed. Carrying
            // the deadline forward by exactly one interval keeps the schedule at the true buffed rate even though any one frame only catches up to where
            // it sits; averaged over many shots the rate matches the multiplier (fire_driver_tpl.cs, weapon 09).
            // triggerHeldContinuously comes from IsContinuingHold, true only when continuingHold (this call's heldLastFrame, always false for a fresh
            // click - see the public TryFire() wrapper) says the trigger was ALSO held last frame. "!weapon.CanCharge" alone read a re-click less than one
            // interval late as mid-cadence, carried the OLD deadline forward, and the held-fire path then fired again almost at once.
            nextFireTime = FireScheduleRule.NextFireTime(nextFireTime, Time.time, interval,
                                                          triggerHeldContinuously: FireScheduleRule.IsContinuingHold(continuingHold, weapon.CanCharge),
                                                          blockedThisTick: false);

            // Heat is charged once per TRIGGER PULL, not once per projectile - see the tooltip on
            // Overheat Per Shot. A five-pellet shotgun costs the same heat as a single bullet.
            overheat?.Add(weapon.OverheatPerShot);

            // The cone this shot actually fires through is the one from BEFORE it blooms: holding
            // the trigger costs you the NEXT shot's accuracy, not this one's.
            float coneAngle = aim != null ? aim.EffectiveConeAngle : 0f;
            // SafeMuzzlePosition, not the raw muzzle - a shot fired flush against a wall must start on the near side of it, or it spawns inside/through
            // the wall and hits whatever is on the other side for free.
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
            RefundHeatIfBeamConnects(origin, direction, targetPoint, coneAngle, seed, chargeFraction,
                                      damageMultiplier, rangeMultiplier);

            // damageMultiplier/rangeMultiplier are appended AFTER chargeFraction and BEFORE PhotonMessageInfo. Appending RPC parameters does not touch the
            // RpcList (it indexes method NAMES, not signatures - see PhotonServerSettings.RpcList), but every client must run this same build for the
            // extra parameters to line up, so rebuild every Player before a two-client check that exercises this RPC.
            photonView.RPC(nameof(RPC_FireWeapon), RpcTarget.AllViaServer, weapon.Id, origin,
                           direction, targetPoint, coneAngle, seed, chargeFraction,
                           damageMultiplier, rangeMultiplier);
            return true;
        }

        /// <summary>
        /// The laser's overheat rule from the GDD: every shot costs double heat (already added above), and half of it comes back if the beam connects.
        /// The SHOOTER decides, from its own ray, the instant it fires. Heat is local state only its owner reads, so nothing crosses the network. The
        /// ray is the same one every client will cast: BuildShots is fed the same seed and cone that are about to go into the RPC.
        /// ACCEPTED TRADEOFF: damage is decided on the victim's client, the refund on the shooter's. Under latency the two can disagree - the shooter
        /// sees the beam cross a target that, on the target's own screen, had already stepped aside - and is refunded for a hit that dealt no damage.
        /// Waiting for the victim to confirm would need a reply message per hit for a few points of heat. Revisit after a real-latency test.
        /// WIND-UP: this runs from TryFire at the press, BEFORE the RPC is sent and before FireAfterWindup's wait, so for a Windup Seconds weapon the
        /// refund is locked in against the target's position at the PRESS and the same mismatch happens with ZERO latency: the target has the whole
        /// warning line to step out of it. Left as is on purpose; the telegraph is the player-facing fairness fix, the shooter's heat bookkeeping was not
        /// asked to change.
        /// Projectile weapons are untouched: they land later, on every client, and none has a refund today.
        /// </summary>
        private void RefundHeatIfBeamConnects(Vector3 origin, Vector3 direction, Vector3 targetPoint,
                                              float coneAngle, int seed, float chargeFraction,
                                              float damageMultiplier, float rangeMultiplier)
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
                                                    shooterActor, shooterTeam, chargeFraction,
                                                    damageMultiplier, rangeMultiplier);
            for (int i = 0; i < shots.Length; i++)
            {
                if (beam.Resolve(origin, shots[i]).Connected)
                    overheat.Refund(weapon.OverheatRefundOnHit);
            }
        }

        /// <summary>0..1 for a charge weapon, 0 for everything else. Charge only starts accumulating once the weapon is off cooldown: measuring from
        /// the press let a player hold straight through the Fire Interval they had to wait anyway, so charging cost nothing and a full charge was
        /// strictly better than a tap (the burst charge path killed faster than the design intent). The charge time is meant to BE the price of the
        /// payoff, so it comes after the cooldown, not inside it.</summary>
        private float ChargeFraction()
        {
            if (weapon == null || !weapon.CanCharge || weapon.MaxChargeSeconds <= 0f || triggerHeldSince <= 0f)
                return 0f;

            float chargeStart = Mathf.Max(triggerHeldSince, nextFireTime);
            return Mathf.Clamp01((Time.time - chargeStart) / weapon.MaxChargeSeconds);
        }

        /// <summary>Read-only mirror of ChargeFraction() for anything that only needs to DRAW the current charge (AimConeView's range arc on a charging
        /// beam weapon). It touches neither that method's logic nor its trap (charge must be read before nextFireTime is overwritten).</summary>
        public float CurrentChargeFraction => ChargeFraction();

        /// <summary>True while this player is holding a charging weapon's trigger - owner-only state, exactly like
        /// CurrentChargeFraction above. ChargeRingView is the only reader: the ring has to appear the moment the
        /// trigger goes down, not only once the charge is above zero, because ChargeFraction() reports 0 for the whole
        /// Fire Interval a hold has to wait out first (see its own comment). An empty ring during that wait is the
        /// point - it is what tells the player the gun is not charging yet.</summary>
        public bool ChargeHeld => weapon != null && weapon.CanCharge && triggerHeldSince > 0f;

        /// <summary>
        /// Builds this shot on EVERY client, including the shooter's own. Every value it needs arrives as a parameter or from info.Sender; nothing in this
        /// body reads local state that would differ per machine.
        /// coneAngleDegrees is load-bearing: the seed alone cannot make the spread identical everywhere. PlayerAim ticks its cone only for its owner
        /// (Update early-returns on !IsMine) and the standing-still bonus depends on movement no other client knows about, so receivers would roll the
        /// same random number against a DIFFERENT cone width and the shots would diverge. The shooter's cone width is something the sender meant, so it
        /// travels as a parameter.
        /// </summary>
        [PunRPC]
        private void RPC_FireWeapon(int weaponId, Vector3 origin, Vector3 aimDirection,
                                    Vector3 targetPoint, float coneAngleDegrees, int seed,
                                    float chargeFraction, float damageMultiplier, float rangeMultiplier,
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

            // damageMultiplier/rangeMultiplier are the SHOOTER's OverPower state (GDD p.20) at the moment it fired, carried as RPC parameters so every
            // client - including the shooter's own - builds an identical shot. Never re-read locally: a receiver cannot know another player's OverPower state.
            ProjectileContext[] shots = BuildShots(fired, aimDirection, targetPoint, coneAngleDegrees,
                                                    seed, shooterActor, shooterTeam, chargeFraction,
                                                    damageMultiplier, rangeMultiplier);

            // A weapon with a wind-up shows its warning line(s) now and only fires for real once that wait is over (design [T]). Everything else fires at once.
            if (fired.WindupSeconds > 0f)
                StartCoroutine(FireAfterWindup(fired, origin, shots, shooterTeam));
            else
                DispatchShots(fired, origin, shots);

            // A hidden shooter's muzzle flash is not shown (own team's always); the fire sound below is untouched. (D2)
            if (fired.MuzzleVfx != null && VFXManager.Instance != null && TeamSight.ShotShownAt(shooterTeam, origin))
                VFXManager.Instance.PlayVFX(fired.MuzzleVfx, origin);
            if (fired.FireSfx != null && AudioManager.Instance != null)
                AudioManager.Instance.Play3D(fired.FireSfx, origin);
        }

        /// <summary>Fires every shot now, simultaneously or spaced by Sequential Delay - what RPC_FireWeapon does for a weapon without a wind-up. Its own
        /// method so a wind-up weapon and an instant one both end up calling exactly this instead of the two paths drifting apart.</summary>
        /// paths drifting apart.</summary>
        private void DispatchShots(WeaponDefinition weapon, Vector3 origin, ProjectileContext[] shots)
        {
            if (weapon.Simultaneous)
            {
                for (int i = 0; i < shots.Length; i++)
                    Spawn(weapon, origin, shots[i]);

                // Shotgun case: "one pull, N projectiles". Every pellet spawns in this same synchronous loop, so unlike a burst's coroutine there is no
                // partial-completion case. newPull true: this one call is the whole pull.
                RaiseFired(weapon.Id, shots.Length, newPull: true);
            }
            else
            {
                StartCoroutine(SpawnSequentially(weapon, origin, shots));
            }
        }

        /// <summary>
        /// Shows a warning line along each shot's already-locked path, waits Windup Seconds, then fires exactly as DispatchShots always has. Runs on
        /// EVERY client, including the shooter's own - each machine counts its own wind-up from when IT received the RPC, so a target who steps out of
        /// the line during the wait takes no damage on ITS OWN client when the beam resolves (damage is victim-side - see Hitscan) even though the
        /// shooter's own screen shows the beam connecting.
        /// Origin and every shot's direction are already locked (RPC parameters), so nothing here can change what the beam does, only whether the
        /// player caught in it had a chance to move first.
        /// ACCEPTED [C]: if this WeaponFiring is destroyed mid-wind-up (its player despawns) the coroutine dies with it and the beam never fires on THIS
        /// client. A shooter who merely dies or is stunned still gets their beam off once the wait ends, like a bullet already in flight; this class has
        /// no hook that would stop it, and design [C] says it should not gain one.
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

        /// <summary>One warning line per shot, along the exact path that shot will travel - drawn only for a beam weapon (Hitscan on its Projectile
        /// Prefab): a bullet's path is not a straight ray to a fixed stop point, so a projectile weapon given a wind-up would have nothing sensible to
        /// draw a line toward. Nothing gives a projectile weapon a wind-up today.</summary>
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
                // The warning line of a shot from the fog is drawn only while it crosses my team's sight. (D2)
                if (!TeamSight.ShotShownAlong(shooterTeam, origin, origin + shots[i].Direction.normalized * length))
                    warnings[i].GetComponent<LineRenderer>().enabled = false;

                // Parented to the SHOOTER, not left as a loose root object: FireAfterWindup only destroys this line after its WaitForSeconds, and that
                // coroutine dies silently if this WeaponFiring is destroyed first (a player despawning mid wind-up; PUN cleans it up), which would orphan the
                // line on screen at full width forever. worldPositionStays: true because the line is drawn in world space (LaserWarningLine.Initialize).
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
        /// The directions for one trigger pull, worked out up front so a burst cannot have its random sequence disturbed by anything between its shots.
        /// Two angles combine: Spread Degrees is the weapon's FIXED shotgun fan, the same on every client by construction; the aim cone is the player's
        /// accuracy, rolled from a System.Random seeded by a number that crossed the wire, so every client rolls the identical spread (random spread was
        /// the designer's explicit choice, and the shared seed is what makes it fair rather than chaotic).
        /// Charge scales two things here, both from the one chargeFraction parameter that already crosses the wire.
        /// damageMultiplier/rangeMultiplier are the shooter's OverPower state, 1 when nothing has boosted it: damage reaches the projectile through
        /// ProjectileContext.FireTimeDamageMultiplier, on top of any charge scaling; range goes to ProjectileContext's constructor (RangeMultiplier)
        /// rather than being applied a second time here.
        /// </summary>
        private static ProjectileContext[] BuildShots(WeaponDefinition weapon, Vector3 aimDirection,
                                                       Vector3 targetPoint, float coneAngleDegrees,
                                                       int seed, int shooterActor, int shooterTeam,
                                                       float chargeFraction, float damageMultiplier,
                                                       float rangeMultiplier)
        {
            int count = ChargedProjectileCount(weapon, chargeFraction);
            // NOT pre-multiplied by damageMultiplier: folding OverPower's bonus into baseDamage worked for a direct hit, but a rocket's splash (its own
            // damage figure, never derived from baseDamage) never saw it. damageMultiplier travels into ProjectileContext's FireTimeDamageMultiplier
            // instead, which both Damage and ExplodeOnImpact.SplashDamageAt read.
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
                                                  targetPoint, chargeFraction, damage, rangeMultiplier,
                                                  damageMultiplier);
            }

            return shots;
        }

        /// <summary>
        /// How many projectiles this trigger pull sends out. Weapons that cannot charge, or that charge something other than their projectile count
        /// (Charge Max Projectiles left at 0), are untouched - they always send Projectiles Per Shot.
        /// The arithmetic lives in ChargeCountRule so the charge ring on the ground marks the same steps. Runs on every client from the chargeFraction
        /// RPC parameter, so every client must be on the same build to agree on the count - see RPC_FireWeapon.
        /// </summary>
        private static int ChargedProjectileCount(WeaponDefinition weapon, float chargeFraction)
        {
            if (!weapon.CanCharge)
                return Mathf.Max(1, weapon.ProjectilesPerShot);

            return ChargeCountRule.Rounds(weapon.ProjectilesPerShot, weapon.ChargeMaxProjectiles,
                                           weapon.ChargeSteps, chargeFraction);
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
                // Raised per round ACTUALLY spawned, not once for the whole burst up front. newPull is true only for round 0 - see Fired.
                // A stun or death does NOT stop this coroutine, only this WeaponFiring being destroyed (its player despawning) does, matching FireAfterWindup.
                // So every round fires and is counted, including any fired after this player has since died or been stunned: the shot was already committed.
                RaiseFired(weapon.Id, 1, newPull: i == 0);
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
            // An enemy's shot is drawn only while it is inside my team's sight, so one from the fog appears as it crosses the fog's edge (own team's
            // always). Visuals only: the shot flies and hits as before. (D2)
            VisibleWhenSeen.Attach(projectile, shot.ShooterTeamId);
        }
    }
}
