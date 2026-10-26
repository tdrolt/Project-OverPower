using System;
using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;

namespace Overpower.TestRange
{
    /// <summary>
    /// A stationary stand-in for a player so weapon damage can be MEASURED: the baseline weapon's
    /// time-to-kill on a full-health player is the figure every balance number derives from.
    /// Takes damage through the same DamageResolver, ArmorState and GameplayConfig-capped
    /// StatusEffectState as a player, health and armor from the same configs, so a config change
    /// moves dummy and player together. No owner guard: HasLocalAuthority is always true, and Burn
    /// ticks in Update through this class's ApplyDamage. Not networked: one Editor session only.
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
        // independent upgrade paths (ArmorConfig): a dummy never buys upgrades.
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

        // This dummy's own marks, keyed by attacker actor: the same ledger a real player's
        // PlayerHealth keeps, since it is a real single victim even though unnetworked.
        private readonly MarkLedger marks = new MarkLedger();

        // Slow, Stun, Vulnerability, Burn timers, built with the same caps as PlayerStatusEffects so
        // a debuff measures the same on a dummy as on a player.
        private StatusEffectState statusState;

        // StatusEffectSpec carries no source actor, so burn kill/damage credit needs it separately;
        // the most recent burn owns all its credit because Burn stacks with StackRule.Refresh (at
        // most one live).
        private int burnSourceActorNumber = -1;

        // Burn source for telemetry; same "most recent burn owns credit" reasoning.
        private int burnAbilityId = -1;

        public bool IsStunned => statusState.IsActive(StatusKind.Stun);

        /// <summary>0..1 fraction of speed lost - read by TestRangeSpawner's Strafer, which rewrites
        /// its own position every frame and so cannot go through a speed multiplier the way a real
        /// player's PlayerMotor does.</summary>
        public float Slow => statusState.Magnitude(StatusKind.Slow);

        /// <summary>0..1 extra damage taken, the same figure as PlayerHealth's Vulnerability, exposed
        /// so PlayerTelemetry's `hit` line can read it for a dummy.</summary>
        public float Vulnerability => statusState.Magnitude(StatusKind.Vulnerability);

        /// <summary>A dummy can be made Invulnerable (ApplyStatus takes any StatusKind) though
        /// ApplyDamage does not check it. Exposed for the `hit` line's `inv` field.</summary>
        public bool IsInvulnerable => statusState.IsActive(StatusKind.Invulnerability);

        /// <summary>Every hit any dummy takes, so PlayerTelemetry can log a `hit` line for the test
        /// range without a per-dummy subscription. A dummy lives in one client's scene, so the only
        /// listener is that client's local player (PlayerTelemetry.HandleDummyDamaged).</summary>
        public static event Action<DummyTarget, DamageResult, DamageInfo> AnyDamaged;

        // The delayed reset scheduled on death. Held so an early ResetToFull (F1 panel, test harness)
        // can cancel it; otherwise a dummy reused inside the delay is wiped back to full by the stale
        // coroutine.
        private Coroutine resetCoroutine;

        // Measurement state, running from the FIRST hit rather than from spawn - the clock should
        // time the kill, not however long the tester spent lining the shot up.
        private float firstHitTime;
        private int hits;

        // A static bulletin board of the most recent kill from ANY dummy, so the panel readout needs
        // no reference to whichever dummy died. -1 means nothing has died yet.
        public static float LastMeasuredTtkSeconds { get; private set; } = -1f;
        public static int LastMeasuredHits { get; private set; }

        // ---- IDisplaceable: sonic pulse knockback ----
        // A dummy has no Rigidbody (PlayerDisplacement sweeps the player's capsule and judges it with
        // DisplacementSweepRule), so this drives the same "travel N metres, stop at the first wall or
        // body" contract with a bare Physics.CapsuleCast against its own CapsuleCollider, a step at a
        // time in Update. Same mask as PlayerDisplacement's forcedBlockMask (Default | Building |
        // Barrier): Default carries every living player AND every other dummy, Building the walls and
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

        // Captured once in Awake, before anything can move this dummy: the spot TestRangeSpawner
        // placed it at. ResetToFull restores it, or a dummy pushed by a sonic pulse stays drifted.
        private Vector3 spawnPosition;

        /// <summary>Fired at the end of every ResetToFull, after position/health/armor/status are
        /// back, so a component holding its own idea of where the dummy belongs (Strafer's patrol
        /// centre, nudged by Displaced) can snap back too. Displaced fires on every push, this only
        /// on a reset.</summary>
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
            displaceBlockMask = ArenaLayers.BodiesWallsAndBarriers;

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
        /// Ticks every status effect and pays out burn damage as PlayerStatusEffects.Update does:
        /// ConsumeBurnDamage must run BEFORE Tick ages the same burn down. Not gated on isDead: a
        /// dummy killed by the tail of a burn needs one more run so the kill is reported (ApplyDamage
        /// already refuses a dead dummy).
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

        /// <summary>IStatusReceiver's entry point. No IsMine-style guard (unlike
        /// PlayerStatusEffects.Apply): HasLocalAuthority is always true, so there is no "someone
        /// else's copy" for this call to have come from.</summary>
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
        /// IDisplaceable's entry point, Forced only. No IsMine-style guard, for the same reason as
        /// ApplyStatus. A push already running is cancelled, not queued: Forced always wins
        /// (DisplacementPriority).
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

        /// <summary>The two exclusions PlayerDisplacement applies through DisplacementSweepRule, for the same reasons:
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
            marks.Clear(); // A reset dummy owes nobody a mark.

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
        /// resolver: vulnerability (from this dummy's own StatusEffectState), reduction, armor, then
        /// health. Reduction stays 0 - a dummy has no dash buff or armor upgrade to grant one.
        /// </summary>
        public DamageResult ApplyDamage(in DamageInfo info)
        {
            if (isDead)
                return default;

            if (hits == 0)
                firstHitTime = Time.time;
            hits++;

            // The same rule PlayerHealth.ApplyDamage applies after its verdict, minus the verdict
            // machinery: this funnel has no self/teammate/shield concept, so every hit "lands".
            MarkOutcome mark = marks.OnLandedHit(info.SourceActorNumber, Time.time, info.MarkWindowSeconds);
            DamageInfo landed = mark == MarkOutcome.Cashed
                ? info.WithAmount(MarkLedger.ScaledAmount(info.Amount, mark, info.MarkedDamageMultiplier))
                : info;

            DamageResult result = DamageResolver.Resolve(landed.Amount, landed.IgnoresArmor, health,
                                                          armor.Current, statusState.Magnitude(StatusKind.Vulnerability), 0f)
                                                 .WithMark(mark);
            armor.Absorb(result.ArmorAbsorbed);
            health -= result.HealthLost;

            // DamageNumberView anchors this dummy's next number at the impact. Only for the LOCAL
            // player's own shots: every client simulates every shot (PlayerHealth.ApplyDamage), so a
            // remote player's fire can hit a dummy in MY copy of the scene with THEIR actor number, and
            // must not re-anchor MY next number. Only for a source whose HitPoint is a genuine impact
            // (Projectile, Splash, Contact): a burn tick's HitPoint is this dummy's own position.
            if (PhotonNetwork.LocalPlayer != null && landed.SourceActorNumber == PhotonNetwork.LocalPlayer.ActorNumber
                && landed.Source != DamageSource.Burn && landed.Source != DamageSource.Zone)
            {
                CombatEvents.RaiseImpactSeen(transform, landed.HitPoint);
            }

            // Clear BEFORE notifying, on the killing blow, so this hit's credit report
            // (NotifyLocalCombatCredit) reads an empty ledger and carries markSecondsLeft 0: the same
            // ordering as PlayerHealth's lethal block ahead of its own Died event.
            if (result.Lethal)
                marks.Clear();

            NotifyLocalCombatCredit(landed, result);
            AnyDamaged?.Invoke(this, result, landed);

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
        /// A dummy has no owner and sends no RPC, so a hit from the LOCAL player's actor number feeds
        /// CombatEvents and NoteDealtDamage directly instead of PlayerCombatCredit's round trip. That
        /// lets armor recharge, the zip gun's cooldown reset and ultimate charge be exercised
        /// single-client against the test range.
        /// </summary>
        private void NotifyLocalCombatCredit(in DamageInfo info, DamageResult result)
        {
            if (result.Total <= 0f || PhotonNetwork.LocalPlayer == null ||
                info.SourceActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                return;

            CombatEvents.RaiseDamageDealt(result.Total);
            // The same "how much" event PlayerCombatCredit.RPC_DamageCredit raises for a real victim,
            // so DamageNumberView need not know whether transform is a player or a dummy.
            CombatEvents.RaiseHitReported(transform, result.Total, result.Mark == MarkOutcome.Cashed);
            // The local player's OWN mark on this dummy, the same "how long" a real credit RPC
            // carries, so the mark diamond works identically against a dummy and a player.
            CombatEvents.RaiseMarkReported(transform, marks.SecondsLeft(PhotonNetwork.LocalPlayer.ActorNumber, Time.time));

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
            // this coroutine, still "pending" as far as the field is concerned, never asks Unity to
            // stop itself.
            resetCoroutine = null;
            ResetToFull();
        }
    }
}
