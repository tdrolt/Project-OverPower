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

    // Pushed into the motor only when the total actually changes, per AddSpeedMultiplier's own
    // contract - not every frame.
    private float appliedSlow;

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
            burnSourceActorNumber = sourceActorNumber;

        state.Apply(spec);
        ApplyStunToMotor(); // Freeze immediately rather than waiting for the next Update - a
                            // stun landing and the movement it blocks should read as the same frame.
    }

    /// <summary>IStatusReceiver's generic entry point, for callers that found this component
    /// through GetComponentInParent&lt;IStatusReceiver&gt; without knowing it is a
    /// PlayerStatusEffects underneath. Forwards straight to Apply.</summary>
    public void ApplyStatus(in StatusEffectSpec spec, int sourceActor) => Apply(spec, sourceActor);

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

        photonView.RPC(nameof(RPC_ApplyStatusFromPeer), photonView.Owner,
                       (byte)spec.kind, spec.duration, spec.magnitude, sourceActor);
    }

    /// <summary>
    /// RequestOnOwner's wire side. Rebuilds the spec on the receiving (owning) machine and applies
    /// it through the normal owner-only Apply above - the stacking rule still comes from `kind`
    /// alone (StatusEffectState.RuleFor), so nothing here needs to carry or guess a StackRule.
    /// </summary>
    [PunRPC]
    private void RPC_ApplyStatusFromPeer(byte kind, float duration, float magnitude, int sourceActor)
    {
        var spec = new StatusEffectSpec { kind = (StatusKind)kind, duration = duration, magnitude = magnitude };
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
        reductionStack.Clear();
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
                                   DamageSource.Burn, false, transform.position);
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
