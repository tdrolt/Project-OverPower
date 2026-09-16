using System;
using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;

namespace Overpower.TestRange
{
    /// <summary>
    /// A stationary stand-in for a player, so weapon damage can be MEASURED rather than asserted.
    ///
    /// Every balance number in the design derives from one figure: how long the baseline weapon
    /// takes to kill a full-health player. Working that out on paper gives an answer that quietly
    /// assumes the fire interval, the armor rule and the damage order are all what you think they
    /// are. This target prints the real number, and every later weapon is tuned against it.
    ///
    /// It takes damage through the same DamageResolver and the same ArmorState a real player does,
    /// with health and armor read from the same two config assets - so a change to either config
    /// moves the dummy and the player together, and the measurement stays honest.
    ///
    /// IT ALSO CARRIES A REAL StatusEffectState (Task 1.8), the same class PlayerStatusEffects wraps
    /// for a real player - built with the same GameplayConfig caps, so a slow or a vulnerability
    /// stacks and expires identically whichever kind of target it landed on. There is no owner guard
    /// here the way PlayerStatusEffects.Apply has one: HasLocalAuthority is always true for a dummy
    /// (see below), so every caller of ApplyStatus is already "the owner" by definition. Burn ticks
    /// itself in Update, through this class's own ApplyDamage, exactly like PlayerStatusEffects
    /// routes a real player's burn back through PlayerHealth.ApplyDamage - one funnel, whichever
    /// target is burning.
    ///
    /// Not networked, and deliberately so: it exists to be shot at in a single Editor session.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DummyTarget : MonoBehaviour, IDamageable, IStatusReceiver, IDisplaceable
    {
        [SerializeField, Tooltip("Match tuning asset. Max Health comes from here, so the dummy has " +
                 "exactly as much health as a real player.")]
        private GameplayConfig gameplayConfig;

        [SerializeField, Tooltip("Armor tiers asset. The dummy wears the tier below, using the same " +
                 "absorb value a player of that tier would have.")]
        private ArmorConfig armorConfig;

        // Deliberately ONE index into both AbsorbLevels and RechargeSeconds, unlike a player's two
        // independent upgrade paths (see ArmorConfig's class comment) - a dummy never buys
        // upgrades, so it only ever needs "the tier everyone starts on" from both arrays at once.
        [SerializeField, Tooltip("Which armor tier this dummy wears. 0 is the tier everyone starts " +
                 "a match on, which is what the baseline time-to-kill is measured against.")]
        private int armorTier = 0;

        [SerializeField, Tooltip("The team this dummy belongs to. It must be a number no real team " +
                 "ever uses, or the no-friendly-fire rule would make it unshootable for whoever " +
                 "happened to share its team. Real teams are 0, 1 and 2.")]
        private int teamId = 99;

        [SerializeField, Tooltip("Seconds to lie dead before healing back to full and becoming a " +
                 "target again, so repeated measurements need no scene reload.")]
        private float resetDelaySeconds = 2f;

        private float health;
        private ArmorState armor;
        private bool isDead;

        // The dummy's own status timers - Slow, Stun, Vulnerability, Burn - built with the same
        // caps a real player's PlayerStatusEffects uses, so a designer testing a slow or a
        // vulnerability debuff against a dummy sees exactly the number a player would take.
        private StatusEffectState statusState;

        // StatusEffectSpec carries no source actor (see PlayerStatusEffects.burnSourceActorNumber's
        // identical comment) - kill/damage credit for a burn tick needs it separately, and "most
        // recent burn owns credit for all of its damage" is exactly correct because Burn stacks
        // with StackRule.Refresh, so at most one is ever live.
        private int burnSourceActorNumber = -1;

        // Task T3 (telemetry): tracked alongside burnSourceActorNumber, same "most recent burn owns
        // credit" reasoning (Burn stacks with StackRule.Refresh, so at most one is ever live).
        private int burnAbilityId = -1;

        public bool IsStunned => statusState.IsActive(StatusKind.Stun);

        /// <summary>0..1 fraction of speed lost - read by TestRangeSpawner's Strafer, which rewrites
        /// its own position every frame and so cannot go through a speed multiplier the way a real
        /// player's PlayerMotor does.</summary>
        public float Slow => statusState.Magnitude(StatusKind.Slow);

        /// <summary>0..1 extra damage taken - Task T3 (telemetry), the same figure PlayerHealth's own
        /// Vulnerability property reports for a real player, exposed here so PlayerTelemetry's `hit`
        /// line can read it for a dummy too. Read-only: ApplyDamage below already uses
        /// statusState.Magnitude(StatusKind.Vulnerability) directly, so this adds no new behaviour.</summary>
        public float Vulnerability => statusState.Magnitude(StatusKind.Vulnerability);

        /// <summary>Task T3 (telemetry): a dummy can be made Invulnerable the same way a player can
        /// (ApplyStatus takes any StatusKind), even though ApplyDamage below does not check it - see
        /// that method's own comment. Exposed read-only for the `hit` line's `inv` field.</summary>
        public bool IsInvulnerable => statusState.IsActive(StatusKind.Invulnerability);

        /// <summary>Task T3 (telemetry): every hit any dummy in the scene takes, so PlayerTelemetry
        /// (which has no reference to any particular DummyTarget) can log a `hit` line for the test
        /// range without a per-dummy subscription. A dummy is not networked and lives in exactly one
        /// client's scene (the class comment), so the only listener that could ever see this is that
        /// same client's own local player - see PlayerTelemetry.HandleDummyDamaged.</summary>
        public static event Action<DummyTarget, DamageResult, DamageInfo> AnyDamaged;

        // The delayed reset scheduled on death. Held onto so an early reset (ResetToFull called by
        // an external caller - the F1 panel, a test harness, or a future respawn button) can cancel
        // it. Without this, a dummy reused inside the delay window would take the stale coroutine's
        // ResetToFull a few seconds later, quietly wiping mid-test state back to full health/armor.
        private Coroutine resetCoroutine;

        // Measurement state, running from the FIRST hit rather than from spawn - the clock should
        // time the kill, not however long the tester spent lining the shot up.
        private float firstHitTime;
        private int hits;

        // A static bulletin board of the most recent kill from ANY dummy in the scene, so the test
        // range panel's readout can show it without polling logs or holding a reference to whichever
        // dummy happens to die. -1 means nothing has died yet this session.
        public static float LastMeasuredTtkSeconds { get; private set; } = -1f;
        public static int LastMeasuredHits { get; private set; }

        // ---- IDisplaceable (Task 1.10a): sonic pulse knockback ---------------------------------
        //
        // A dummy has no Rigidbody the way a player does (PlayerDisplacement sweeps a Rigidbody with
        // Rigidbody.SweepTestAll) - it is plain scenery with a Collider - so this drives the same
        // "travel N metres, stop at the first wall or body" contract with a bare Physics.CapsuleCast
        // against this dummy's own CapsuleCollider instead, resolved a step at a time in Update.
        // Same blockMask as PlayerDisplacement (Default | Building) for the same reason: Default
        // carries every living player AND every other dummy, Building carries the walls and
        // deployable cover.
        private CapsuleCollider capsule;
        private int displaceBlockMask;
        private Coroutine displaceCoroutine;
        private Action<DisplaceEnd> pendingDisplaceCallback;

        // Where the CURRENT push started, kept as a field rather than a coroutine-local variable:
        // cancelling a push (a second pulse landing mid-flight) stops the coroutine from OUTSIDE it,
        // at which point a local variable is already gone - FinishDisplacement still needs this to
        // report how far that interrupted push actually moved before Displaced fires.
        private Vector3 activeDisplaceStart;

        /// <summary>True while a knockback is actively moving this dummy. TestRangeSpawner's Strafer
        /// reads this so it stops rewriting this dummy's position out from under the push, the same
        /// tug-of-war PlayerDisplacement.ExternalMotionControl exists to prevent for a real player.</summary>
        public bool IsDisplacing { get; private set; }

        /// <summary>Fired once, when a knockback finishes, with the NET world-space movement it
        /// caused. TestRangeSpawner's Strafer adds this to its own patrol centre so the patrol
        /// continues from wherever the dummy ended up instead of snapping back to the old line.</summary>
        public event Action<Vector3> Displaced;

        // Captured once, in Awake, before anything can move this dummy - the exact spot
        // TestRangeSpawner placed it at. ResetToFull restores transform.position here (review
        // finding on Task 1.10a: it already restored health/armor/status but left position
        // untouched, so a dummy pushed by a sonic pulse and then reset stayed drifted forever).
        private Vector3 spawnPosition;

        /// <summary>Fired at the end of every ResetToFull, after position/health/armor/status are
        /// all back to their spawn values - so a component that shifted its OWN idea of where this
        /// dummy belongs (TestRangeSpawner.Strafer's patrol centre, nudged by Displaced whenever a
        /// pulse pushes the dummy) can snap that back too. Not the same event as Displaced: that one
        /// fires on every push, this one only on a reset.</summary>
        public event Action ResetOccurred;

        public bool IsAlive => !isDead;
        public int TeamId => teamId;

        /// <summary>Not a real player, so it owns no actor number. -1 can never collide with a
        /// Photon actor number, which start at 1.</summary>
        public int ActorNumber => -1;

        /// <summary>A dummy has no owning client to defer to - it exists to be shot at from
        /// whichever single Editor session is running it - so it is always its own authority.</summary>
        public bool HasLocalAuthority => true;

        public float Health => health;
        public float Armor => armor.Current;

        private void Awake()
        {
            // Before anything else runs - this is the position TestRangeSpawner just placed the
            // dummy at (a plain Instantiate already sets transform.position before Awake fires).
            spawnPosition = transform.position;

            // Loud, matching PlayerHealth. A dummy quietly falling back to a hardcoded 100 health
            // would still look like it was working, and would report a time-to-kill that no longer
            // matched a real player - poisoning the one thing this object exists to measure.
            if (gameplayConfig == null)
                Debug.LogError($"[DummyTarget] {name}: GameplayConfig is not assigned - falling back to " +
                                "hardcoded health, so any measured time-to-kill is meaningless.");
            if (armorConfig == null)
                Debug.LogError($"[DummyTarget] {name}: ArmorConfig is not assigned - this dummy has no " +
                                "armor, so any measured time-to-kill is too short.");

            // Built once, before the first ResetToFull (which clears it) - same two caps
            // PlayerStatusEffects.Awake reads off the same asset.
            statusState = new StatusEffectState(
                gameplayConfig != null ? gameplayConfig.SlowCap : 0f,
                gameplayConfig != null ? gameplayConfig.VulnerabilityCap : 0f);

            capsule = GetComponent<CapsuleCollider>();
            if (capsule == null)
                Debug.LogError($"[DummyTarget] {name}: no CapsuleCollider - a sonic pulse cannot knock this dummy back.");
            displaceBlockMask = LayerMask.GetMask("Default", "Building");

            ResetToFull();
        }

        private void OnDisable()
        {
            // A disabled component's coroutines are NOT stopped automatically unless the whole
            // GameObject is deactivated - only relying on that would leave the coroutine running
            // whenever something merely sets enabled = false. Cancel explicitly either way.
            CancelPendingReset();
            CancelDisplacement();
        }

        /// <summary>
        /// Ticks every status effect currently on this dummy and pays out its burn damage, exactly
        /// the way PlayerStatusEffects.Update does for a real player - see that class's own comment
        /// for why ConsumeBurnDamage must run BEFORE Tick ages the same burn down. Not gated on
        /// isDead: a dummy killed by the tail end of a burn still needs this to run once more so the
        /// kill is reported, and ApplyDamage below already refuses a dead dummy on its own.
        /// </summary>
        private void Update()
        {
            float burn = statusState.ConsumeBurnDamage(Time.deltaTime);
            statusState.Tick(Time.deltaTime);

            if (burn > 0f)
                ApplyBurnDamage(burn);
        }

        private void ApplyBurnDamage(float burn)
        {
            Teams.TryGetTeam(burnSourceActorNumber, out int sourceTeamId);
            var info = new DamageInfo(burn, burnSourceActorNumber, sourceTeamId, -1,
                                       DamageSource.Burn, false, transform.position, burnAbilityId);
            ApplyDamage(info);
        }

        /// <summary>IStatusReceiver's entry point. No IsMine-style guard, unlike
        /// PlayerStatusEffects.Apply: HasLocalAuthority is always true for a dummy (see below), so
        /// there is no "someone else's copy" for this call to have come from.</summary>
        public void ApplyStatus(in StatusEffectSpec spec, int sourceActor)
        {
            if (spec.kind == StatusKind.Burn)
            {
                burnSourceActorNumber = sourceActor;
                burnAbilityId = spec.abilityId;
            }

            statusState.Apply(spec);
            LogStatusApplied(spec);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogStatusApplied(in StatusEffectSpec spec)
        {
            Debug.Log($"[DUMMY] {spec.kind} {spec.magnitude:0.00} {spec.duration:0.0}s");
        }

        /// <summary>
        /// IDisplaceable's entry point - Forced only, the same shape PlayerDisplacement.Displace
        /// exposes. No IsMine-style guard here either, for the identical reason ApplyStatus above has
        /// none: HasLocalAuthority is always true for a dummy, so there is no "someone else's copy"
        /// this call could have come from. A push already running is cancelled outright rather than
        /// queued - Forced always wins, matching DisplacementPriority's own rule for a real player.
        /// </summary>
        public void Displace(Vector3 direction, float distance, float speed, Action<DisplaceEnd> onEnd)
        {
            if (capsule == null)
                return; // Awake already logged why.

            if (displaceCoroutine != null)
            {
                StopCoroutine(displaceCoroutine);
                FinishDisplacement(DisplaceOutcome.Cancelled, null); // Reports the interrupted push's own partial movement.
            }

            Vector3 travelDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            activeDisplaceStart = transform.position;
            pendingDisplaceCallback = onEnd;
            displaceCoroutine = StartCoroutine(DisplaceRoutine(travelDirection, Mathf.Max(0f, distance), Mathf.Max(0.01f, speed)));
        }

        private IEnumerator DisplaceRoutine(Vector3 direction, float distance, float speed)
        {
            float remaining = distance;
            IsDisplacing = true;

            while (remaining > 0f)
            {
                float step = Mathf.Min(speed * Time.deltaTime, remaining);

                GetCapsuleWorldPoints(out Vector3 point1, out Vector3 point2, out float radius);
                RaycastHit[] hits = Physics.CapsuleCastAll(point1, point2, radius, direction, step,
                                                            displaceBlockMask, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                RaycastHit? blocked = null;
                foreach (RaycastHit hit in hits)
                {
                    if (IsDisplaceBlocker(hit))
                    {
                        blocked = hit;
                        break;
                    }
                }

                if (blocked.HasValue)
                {
                    transform.position += direction * blocked.Value.distance;
                    FinishDisplacement(DisplaceOutcome.Blocked, blocked.Value.collider);
                    yield break;
                }

                transform.position += direction * step;
                remaining -= step;
                yield return null;
            }

            FinishDisplacement(DisplaceOutcome.Completed, null);
        }

        /// <summary>Same two exclusions PlayerDisplacement.IsBlocker applies, for the same reasons:
        /// a near-vertical normal is the floor underfoot, not a wall in the way, and a dummy can
        /// never be blocked by its own collider.</summary>
        private bool IsDisplaceBlocker(RaycastHit hit)
        {
            if (hit.collider == null)
                return false;

            if (hit.normal.y > 0.5f)
                return false; // Ground underfoot, not a wall in the way.

            if (hit.collider.transform.IsChildOf(transform))
                return false; // Never blocked by our own body.

            return true;
        }

        /// <summary>World-space endpoints and radius of this dummy's own CapsuleCollider, matching
        /// its current position exactly (unlike TestRangeSpawner.Grounded, which only ever reads the
        /// PREFAB's capsule to place a dummy before it has moved).</summary>
        private void GetCapsuleWorldPoints(out Vector3 point1, out Vector3 point2, out float radius)
        {
            Vector3 center = transform.TransformPoint(capsule.center);
            float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            Vector3 up = transform.up;

            point1 = center + up * halfSegment;
            point2 = center - up * halfSegment;
            radius = capsule.radius;
        }

        private void FinishDisplacement(DisplaceOutcome outcome, Collider blocker)
        {
            IsDisplacing = false;
            displaceCoroutine = null;

            Action<DisplaceEnd> callback = pendingDisplaceCallback;
            pendingDisplaceCallback = null;

            Vector3 netMovement = transform.position - activeDisplaceStart;
            LogDisplaceOutcome(outcome, netMovement.magnitude, blocker);

            // Cancelled (superseded by a second pulse before this one finished moving) still moved
            // the dummy partway - the strafer must absorb that partial shift too, or it would snap
            // back to a patrol centre that no longer matches where the dummy actually stands.
            if (netMovement.sqrMagnitude > 0.0001f)
                Displaced?.Invoke(netMovement);

            callback?.Invoke(new DisplaceEnd(outcome, transform.position, blocker));
        }

        private void CancelDisplacement()
        {
            if (displaceCoroutine == null)
                return;

            StopCoroutine(displaceCoroutine);
            FinishDisplacement(DisplaceOutcome.Cancelled, null);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDisplaceOutcome(DisplaceOutcome outcome, float metres, Collider blocker)
        {
            Debug.Log($"[DUMMY] pushed {metres:0.0}m outcome={outcome} blocker={(blocker != null ? blocker.name : "none")}");
        }

        /// <summary>Restores the dummy to full health/armor immediately. Public so an early external
        /// reset (F1 panel, test harness) goes through the same path the delayed post-death reset
        /// uses, and cancels that delayed reset first - see resetCoroutine.</summary>
        public void ResetToFull()
        {
            CancelPendingReset();

            // Review finding, Task 1.10a: this used to restore health/armor/status but never
            // position, so a strafer pushed off its row by a sonic pulse stayed drifted forever.
            transform.position = spawnPosition;

            health = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
            armor = new ArmorState(armorConfig != null ? armorConfig.AbsorbFor(armorTier) : 0f,
                                    armorConfig != null ? armorConfig.RechargeSecondsFor(armorTier) : 6f,
                                    armorConfig != null ? armorConfig.RefillSeconds : 2.5f);
            isDead = false;
            firstHitTime = 0f;
            hits = 0;
            statusState.ClearAll(); // Awake builds statusState before ever calling this, so it is never null here.
            burnSourceActorNumber = -1;
            burnAbilityId = -1;

            // After everything above is back to spawn values, not before - a listener (the
            // Strafer's own centre) should see a fully-reset dummy, not one mid-restore.
            ResetOccurred?.Invoke();
        }

        private void CancelPendingReset()
        {
            if (resetCoroutine == null)
                return;

            StopCoroutine(resetCoroutine);
            resetCoroutine = null;
        }

        /// <summary>
        /// The same order of operations a player takes damage in, because it is literally the same
        /// resolver: vulnerability, then reduction, then armor, then health. Vulnerability now comes
        /// from this dummy's own StatusEffectState (Task 1.8) instead of a hardcoded 0, so a raybeam
        /// or a mine's own debuff measures the same extra damage on a dummy as it would on a real
        /// player. Reduction stays 0 - a dummy has no dash buff or armor upgrade to grant one.
        /// </summary>
        public DamageResult ApplyDamage(in DamageInfo info)
        {
            if (isDead)
                return default;

            if (hits == 0)
                firstHitTime = Time.time;
            hits++;

            DamageResult result = DamageResolver.Resolve(info.Amount, info.IgnoresArmor, health,
                                                          armor.Current, statusState.Magnitude(StatusKind.Vulnerability), 0f);
            armor.Absorb(result.ArmorAbsorbed);
            health -= result.HealthLost;

            NotifyLocalCombatCredit(info, result);
            AnyDamaged?.Invoke(this, result, info);

            if (result.Lethal)
            {
                isDead = true;

                // The line the whole test range exists to print.
                LastMeasuredTtkSeconds = Time.time - firstHitTime;
                LastMeasuredHits = hits;
                Debug.Log($"[TTK] killed in {LastMeasuredTtkSeconds:F2}s after {hits} hits");

                resetCoroutine = StartCoroutine(ResetAfterDelay());
            }

            return result;
        }

        /// <summary>
        /// A dummy has no owner and sends no RPC - it lives in exactly one client's scene, so it
        /// needs none of PlayerCombatCredit's networked round trip to tell the shooter what they
        /// dealt. A hit from the LOCAL player's own actor number feeds CombatEvents and
        /// NoteDealtDamage directly instead, the same information a real target's
        /// RPC_DamageCredit would eventually deliver - which is what lets armor recharge, the zip
        /// gun's cooldown reset and ultimate charge all be exercised single-client against the test
        /// range, with no second Editor session required.
        /// </summary>
        private void NotifyLocalCombatCredit(in DamageInfo info, DamageResult result)
        {
            if (result.Total <= 0f || PhotonNetwork.LocalPlayer == null ||
                info.SourceActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                return;

            CombatEvents.RaiseDamageDealt(result.Total);

            PhotonView localView = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            PlayerHealth localHealth = localView != null ? localView.GetComponent<PlayerHealth>() : null;
            localHealth?.NoteDealtDamage();

            if (result.Lethal)
                CombatEvents.RaiseTakedown(true);
        }

        private IEnumerator ResetAfterDelay()
        {
            yield return new WaitForSeconds(resetDelaySeconds);

            // Clear the handle before resetting rather than let ResetToFull's own cancel do it, so
            // this coroutine - which is, at this point, still "pending" as far as the field is
            // concerned - never asks Unity to stop itself.
            resetCoroutine = null;
            ResetToFull();
        }
    }
}
