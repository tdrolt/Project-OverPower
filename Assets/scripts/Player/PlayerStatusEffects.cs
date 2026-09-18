using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;

/// <summary>
/// Owns the StatusEffectState for one player - burns, slows, stuns, vulnerability and
/// invulnerability. Moved out of PlayerHealth (Task 0.10): a status is not health, and
/// PlayerHealth only ever needed to read Vulnerability back out and be told about burn damage.
///
/// Also hosts this player's DamageReductionStack (Task 1.0a) - a reduction buff is a status the
/// same way a slow is, just one StatusEffectState has no notion of ("stacks multiplicatively,
/// never expires on its own"), so it gets its own small stack alongside the timed effects rather
/// than being forced into StatusKind.
/// </summary>
public class PlayerStatusEffects : MonoBehaviour, IStatusReceiver
{
    [SerializeField, Tooltip("Match tuning asset. Caps how far Slow and Vulnerability can stack, " +
             "however many effects are landing at once.")]
    private GameplayConfig gameplayConfig;

    private PhotonView photonView;
    private PlayerHealth playerHealth;
    private PlayerMotor motor;
    private StatusEffectState state;
    private DamageReductionStack reductionStack;

    // Rework step 2 (Tudor, 2026-09-18): the Invulnerability ultimate's armed window - see
    // ReactiveInvulnerabilityState's own class comment for why this is not a StatusKind.
    private readonly ReactiveInvulnerabilityState reactiveInvulnerability = new ReactiveInvulnerabilityState();

    // Set by TryConsumeReactiveInvulnerability and read (and cleared) once by InvulnerabilityAbility's
    // next OwnerTick. A flag rather than an event: the trigger happens deep inside PlayerHealth.ApplyDamage,
    // which can itself be reached from this class's own Update (a burn tick), and an event raised from there
    // would let a subscriber re-enter the damage funnel mid-frame. One frame of latency on a cosmetic shield
    // is invisible; a re-entrant ApplyDamage is not.
    private bool reactiveInvulnerabilityJustTriggered;

    // The four tuning numbers the arm was last cast with, cached until TryConsumeReactiveInvulnerability
    // consumes it - see ArmReactiveInvulnerability's own comment for why they live here rather than on
    // PlayerHealth.
    private float cachedInvincibleSeconds;
    private float cachedStunSeconds;
    private float cachedMinimumTriggerDamage;
    private int cachedAbilityId = -1;

    // A key of its own, distinct from `this` - Slow already keys its multiplier on `this`, and a
    // stunned, slowed player needs both multipliers to compose (0 speed either way) rather than
    // one silently overwriting the other's dictionary entry.
    private static readonly object StunKey = new object();

    // Mirrors IsStunned so ApplyStunToMotor only touches the motor on the frame stun actually
    // starts or ends, not every frame it happens to still be true - the same "only when it
    // actually changes" rule ApplySlowToMotor follows for appliedSlow.
    private bool stunAppliedToMotor;

    // StatusEffectSpec deliberately carries no source actor (it is a tested type shared by every
    // status kind, most of which have no notion of a "caster"), so the source for kill credit is
    // tracked here instead. Burn stacks with StackRule.Refresh - a fresh burn always replaces the
    // old one outright, so at most one is ever live - which makes "most recent burn owns credit
    // for all of its damage" exactly correct today. It would only need revisiting if burn ever
    // became a stacking effect.
    private int burnSourceActorNumber = -1;

    // Task T3 (telemetry): the ability id the CURRENT burn was applied with, tracked the same way
    // burnSourceActorNumber is (see its own comment - "most recent burn owns credit" applies here
    // identically, since Burn stacks with StackRule.Refresh).
    private int burnAbilityId = -1;

    // Pushed into the motor only when the total actually changes, per AddSpeedMultiplier's own
    // contract - not every frame.
    private float appliedSlow;

    /// <summary>Task T3 (telemetry): raised on the victim's own client every time a status is
    /// applied through Apply below - PlayerTelemetry logs a `status` line from it. Carries the
    /// spec's own abilityId (-1 when it did not come from an ability) and BOTH duration and
    /// magnitude (T3 review item 5 - an earlier version picked only one per kind, which dropped
    /// duration for Burn/Slow/Vulnerability; T5 wants to sum seconds across every kind uniformly).</summary>
    public event System.Action<StatusKind, int, int, float, float> StatusApplied;

    public bool IsStunned => state.IsActive(StatusKind.Stun);
    public bool IsInvulnerable => state.IsActive(StatusKind.Invulnerability);

    /// <summary>0..1 - what PlayerHealth multiplies incoming damage by.</summary>
    public float Vulnerability => state.Magnitude(StatusKind.Vulnerability);

    /// <summary>0..1 fraction of speed lost.</summary>
    public float Slow => state.Magnitude(StatusKind.Slow);

    /// <summary>0..1 - what PlayerHealth.CurrentDamageReduction() reports to DamageResolver, and
    /// the one place every reduction source (dash, an armor upgrade, a future buff) is combined.</summary>
    public float CurrentDamageReduction => reductionStack.Total;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerHealth = GetComponent<PlayerHealth>();
        motor = GetComponent<PlayerMotor>();

        state = new StatusEffectState(
            gameplayConfig != null ? gameplayConfig.SlowCap : 0f,
            gameplayConfig != null ? gameplayConfig.VulnerabilityCap : 0f);
        reductionStack = new DamageReductionStack();
    }

    private void Update()
    {
        if (!photonView.IsMine)
            return; // No other client should simulate your statuses, same reasoning as PlayerHealth.

        // Order matters: ConsumeBurnDamage reports damage for the burn as it stood before this
        // step, then Tick ages every status - including that same burn - by the same deltaTime.
        float burn = state.ConsumeBurnDamage(Time.deltaTime);
        state.Tick(Time.deltaTime);
        reactiveInvulnerability.Tick(Time.deltaTime);

        // ORDER MATTERS AND IS LOAD-BEARING (rework step 2). ApplyBurnDamage below re-enters
        // PlayerHealth.ApplyDamage, which can call TryConsumeReactiveInvulnerability, which calls Apply()
        // above, which ADDS A DICTIONARY ENTRY to `state`. That is only safe because both Tick calls have
        // already returned by this line. Do not move ApplyBurnDamage above them.
        if (burn > 0f)
            ApplyBurnDamage(burn);

        ApplySlowToMotor();
        ApplyStunToMotor(); // Ticked every frame so a stun that just expired unfreezes the same frame.
    }

    /// <summary>
    /// Applies one timed status effect. sourceActorNumber only matters for Burn - see the field
    /// comment above for why tracking just the latest one is safe today - and defaults to -1
    /// (unknown) so existing call sites that do not yet have a caster keep compiling.
    ///
    /// Owner-only: every client's copy of a projectile or beam can reach the target it hit and
    /// call this, but only the target's own client should ever act on it. Applying to a remote
    /// copy used to leave that status running forever, because nothing but Update above (also
    /// owner-only) ever ticks it down - this guard is what closes that gap.
    /// </summary>
    public void Apply(in StatusEffectSpec spec, int sourceActorNumber = -1)
    {
        if (!photonView.IsMine)
            return;

        if (spec.kind == StatusKind.Burn)
        {
            burnSourceActorNumber = sourceActorNumber;
            burnAbilityId = spec.abilityId;
        }

        state.Apply(spec);
        ApplyStunToMotor(); // Freeze immediately rather than waiting for the next Update - a
                            // stun landing and the movement it blocks should read as the same frame.

        // Task T3 review (item 5): both raw numbers, not one picked per kind - an earlier version of
        // this reported only whichever of duration/magnitude was "informative" for a given kind,
        // which silently dropped duration for Burn/Slow/Vulnerability (T5 wants to sum seconds
        // uniformly across every kind).
        StatusApplied?.Invoke(spec.kind, sourceActorNumber, spec.abilityId, spec.duration, spec.magnitude);
    }

    /// <summary>IStatusReceiver's generic entry point, for callers that found this component
    /// through GetComponentInParent&lt;IStatusReceiver&gt; without knowing it is a
    /// PlayerStatusEffects underneath. Forwards straight to Apply.</summary>
    public void ApplyStatus(in StatusEffectSpec spec, int sourceActor) => Apply(spec, sourceActor);

    /// <summary>Owner only. The Invulnerability ultimate's cast - see ReactiveInvulnerabilityState.
    /// Arms nothing on any other machine, exactly like Apply above. The ability passes every number it owns,
    /// so they stay in one home on its prefab and PlayerHealth never has to know any of them.</summary>
    public void ArmReactiveInvulnerability(float armedSeconds, float invincibleSeconds, float stunSeconds,
                                           float minimumTriggerDamage, int abilityId)
    {
        if (!photonView.IsMine)
            return;

        cachedInvincibleSeconds = invincibleSeconds;
        cachedStunSeconds = stunSeconds;
        cachedMinimumTriggerDamage = minimumTriggerDamage;
        cachedAbilityId = abilityId;
        reactiveInvulnerability.Arm(armedSeconds);
    }

    /// <summary>True while a cast is waiting for a hit - for the caster's own HUD glow only.</summary>
    public bool IsReactiveInvulnerabilityArmed => reactiveInvulnerability.IsArmed;

    /// <summary>
    /// PlayerHealth.ApplyDamage's one consuming veto. True means "this hit never happened": the caller
    /// must return default() before resolving any of it, which is what nullifies the triggering hit.
    ///
    /// On a trigger this applies the ordinary Invulnerability and Stun statuses for their own spans, so
    /// everything downstream - the funnel's own IsInvulnerable check, the `status` telemetry line, the
    /// motor freeze - keeps working with no new concept at all. BOTH SPANS START HERE, AT THE HIT, never
    /// at the cast: freezing a caster during the armed window would punish a cast nobody answered.
    /// </summary>
    public bool TryConsumeReactiveInvulnerability(float damageAmount)
    {
        if (!photonView.IsMine)
            return false;

        if (!reactiveInvulnerability.TryConsume(damageAmount, cachedMinimumTriggerDamage))
            return false;

        Apply(new StatusEffectSpec { kind = StatusKind.Invulnerability, duration = cachedInvincibleSeconds, abilityId = cachedAbilityId },
              photonView.OwnerActorNr);
        if (cachedStunSeconds > 0f)
            Apply(new StatusEffectSpec { kind = StatusKind.Stun, duration = cachedStunSeconds, abilityId = cachedAbilityId },
                  photonView.OwnerActorNr);

        reactiveInvulnerabilityJustTriggered = true;
        return true;
    }

    /// <summary>Owner only, read once. InvulnerabilityAbility's OwnerTick asks this so it can send the
    /// shield's phase to every client - the one thing about this ability the other machines cannot work
    /// out for themselves.</summary>
    public bool ConsumeReactiveInvulnerabilityTrigger()
    {
        bool triggered = reactiveInvulnerabilityJustTriggered;
        reactiveInvulnerabilityJustTriggered = false;
        return triggered;
    }

    /// <summary>
    /// The one-caster-decides path: for an effect that only ONE client resolves and tells the
    /// victim about, unlike every status above, which every client resolves for itself off a
    /// shared seed. Sonic pulse's player-into-player collision stun (Task 1.10) is the first user -
    /// only the pulse owner's physics sees that collision, so only that client can know it happened.
    ///
    /// Applies locally with no network trip when this IS the local player (self-inflicted or
    /// already running on the right machine); otherwise RPCs the one owner who is allowed to apply
    /// it to themselves. Never RpcTarget.All: every other client would then also try to apply a
    /// status meant for one specific victim.
    /// </summary>
    public void RequestOnOwner(in StatusEffectSpec spec, int sourceActor)
    {
        if (photonView.IsMine)
        {
            Apply(spec, sourceActor);
            return;
        }

        // abilityId appended LAST (T3 review, item 13): appending an RPC parameter does not touch
        // the RpcList (it indexes method NAMES, not signatures - see WeaponFiring.RPC_FireWeapon's
        // own comment on the identical pattern), so this carries the sonic pulse's ability id across
        // the wire without adding a new RPC. Every client must be running the same build for the
        // extra parameter to line up.
        photonView.RPC(nameof(RPC_ApplyStatusFromPeer), photonView.Owner,
                       (byte)spec.kind, spec.duration, spec.magnitude, sourceActor, spec.abilityId);
    }

    /// <summary>
    /// RequestOnOwner's wire side. Rebuilds the spec on the receiving (owning) machine and applies
    /// it through the normal owner-only Apply above - the stacking rule still comes from `kind`
    /// alone (StatusEffectState.RuleFor), so nothing here needs to carry or guess a StackRule.
    /// </summary>
    [PunRPC]
    private void RPC_ApplyStatusFromPeer(byte kind, float duration, float magnitude, int sourceActor, int abilityId)
    {
        var spec = new StatusEffectSpec { kind = (StatusKind)kind, duration = duration, magnitude = magnitude, abilityId = abilityId };
        Apply(spec, sourceActor);
    }

    /// <summary>Adds or replaces one source of damage reduction - a dash buff, an armor upgrade.
    /// Owner-only, like ApplyDamage: only your own client should decide how much less damage you
    /// take.</summary>
    public void AddDamageReduction(object key, float fraction)
    {
        if (!photonView.IsMine)
            return;

        reductionStack.Set(key, fraction);
    }

    /// <summary>Removes one source of damage reduction - the buff ending.</summary>
    public void RemoveDamageReduction(object key)
    {
        if (!photonView.IsMine)
            return;

        reductionStack.Remove(key);
    }

    public void ClearAll()
    {
        state.ClearAll();
        burnSourceActorNumber = -1;
        burnAbilityId = -1;
        reductionStack.Clear();
        reactiveInvulnerability.Clear(); // Death and respawn must never carry an armed shield forward.
        reactiveInvulnerabilityJustTriggered = false;
        ApplySlowToMotor(); // Slow is now 0 - make sure the motor's multiplier is dropped with it.
        ApplyStunToMotor(); // Same for stun - a death or respawn must not leave the freeze behind.
    }

    public float Remaining(StatusKind kind) => state.Remaining(kind);

    /// <summary>Burn damage goes through the one damage funnel, never straight off health - the
    /// whole point of that funnel existing is that nothing bypasses it.</summary>
    private void ApplyBurnDamage(float burn)
    {
        if (playerHealth == null)
            return;

        Teams.TryGetTeam(burnSourceActorNumber, out int sourceTeamId);
        var info = new DamageInfo(burn, burnSourceActorNumber, sourceTeamId, -1,
                                   DamageSource.Burn, false, transform.position, burnAbilityId);
        playerHealth.ApplyDamage(info);
    }

    private void ApplySlowToMotor()
    {
        float slow = Slow;
        if (Mathf.Approximately(slow, appliedSlow) || motor == null)
            return;

        appliedSlow = slow;

        if (slow > 0f)
            motor.AddSpeedMultiplier(this, 1f - slow);
        else
            motor.RemoveSpeedMultiplier(this);
    }

    /// <summary>
    /// Stun freezes movement the same way Slow throttles it - through a keyed multiplier, never a
    /// direct write to speed - but under StunKey rather than `this`, so the two compose instead of
    /// fighting over one dictionary entry: a stunned AND slowed player still reads as stunned
    /// (0 speed) the instant the stun lands, and correctly resumes at their slowed speed, not full
    /// speed, once it lifts.
    /// </summary>
    private void ApplyStunToMotor()
    {
        bool stunned = IsStunned;
        if (stunned == stunAppliedToMotor || motor == null)
            return;

        stunAppliedToMotor = stunned;

        if (stunned)
            motor.AddSpeedMultiplier(StunKey, 0f);
        else
            motor.RemoveSpeedMultiplier(StunKey);
    }
}
