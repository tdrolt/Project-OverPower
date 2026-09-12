using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;

/// <summary>
/// Owns the StatusEffectState for one player - burns, slows, stuns, vulnerability and
/// invulnerability. Moved out of PlayerHealth (Task 0.10): a status is not health, and
/// PlayerHealth only ever needed to read Vulnerability back out and be told about burn damage.
/// </summary>
public class PlayerStatusEffects : MonoBehaviour
{
    [SerializeField, Tooltip("Match tuning asset. Caps how far Slow and Vulnerability can stack, " +
             "however many effects are landing at once.")]
    private GameplayConfig gameplayConfig;

    private PhotonView photonView;
    private PlayerHealth playerHealth;
    private PlayerMotor motor;
    private StatusEffectState state;

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

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerHealth = GetComponent<PlayerHealth>();
        motor = GetComponent<PlayerMotor>();

        state = new StatusEffectState(
            gameplayConfig != null ? gameplayConfig.SlowCap : 0f,
            gameplayConfig != null ? gameplayConfig.VulnerabilityCap : 0f);
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
    }

    /// <summary>
    /// Applies one timed status effect. sourceActorNumber only matters for Burn - see the field
    /// comment above for why tracking just the latest one is safe today - and defaults to -1
    /// (unknown) so existing call sites that do not yet have a caster keep compiling.
    /// </summary>
    public void Apply(in StatusEffectSpec spec, int sourceActorNumber = -1)
    {
        if (spec.kind == StatusKind.Burn)
            burnSourceActorNumber = sourceActorNumber;

        state.Apply(spec);
    }

    public void ClearAll()
    {
        state.ClearAll();
        burnSourceActorNumber = -1;
        ApplySlowToMotor(); // Slow is now 0 - make sure the motor's multiplier is dropped with it.
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
}
