using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;
using Overpower.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Owns the StatusEffectState for one player - burns, slows, stuns, vulnerability and
/// invulnerability. A status is not health: PlayerHealth only reads Vulnerability back out and is
/// told about burn damage.
///
/// Also hosts this player's DamageReductionStack: a reduction buff stacks multiplicatively and never
/// expires on its own, which StatusEffectState has no notion of, so it gets its own small stack
/// rather than being forced into StatusKind.
/// </summary>
public class PlayerStatusEffects : MonoBehaviour, IStatusReceiver, IArmedShield
{
    [SerializeField, Tooltip("Match tuning asset. Caps how far Slow and Vulnerability can stack, " +
             "however many effects are landing at once.")]
    private GameplayConfig gameplayConfig;

    private PhotonView photonView;
    private PlayerHealth playerHealth;
    private PlayerLifecycle lifecycle;
    private PlayerMotor motor;
    private Overpower.Dominion.IEffectShield effectShield; // respawn shield: stops an enemy's status while up (null without Dominion)
    private StatusEffectState state;
    private DamageReductionStack reductionStack;

    // The Invulnerability ultimate's armed window; see ReactiveInvulnerabilityState for why this is not a StatusKind.
    private readonly ReactiveInvulnerabilityState reactiveInvulnerability = new ReactiveInvulnerabilityState();

    // Set by TryConsumeReactiveInvulnerability, read (and cleared) once by InvulnerabilityAbility's next
    // OwnerTick. A flag, not an event: the trigger happens deep inside PlayerHealth.ApplyDamage, which can
    // itself be reached from this class's Update (a burn tick), and an event raised there would let a
    // subscriber re-enter the damage funnel mid-frame. One frame of latency on a cosmetic shield is
    // invisible; a re-entrant ApplyDamage is not.
    private bool reactiveInvulnerabilityJustTriggered;

    // The tuning numbers the arm was last cast with, cached until TryConsumeReactiveInvulnerability
    // consumes it (see ArmReactiveInvulnerability); they live here, not on PlayerHealth.
    private float cachedInvincibleSeconds;
    private float cachedStunSeconds;
    private float cachedMinimumTriggerDamage;
    private int cachedAbilityId = -1;

    // A key of its own, distinct from `this`: Slow keys its multiplier on `this`, and a stunned, slowed
    // player needs both to compose (0 speed either way) rather than one overwriting the other's entry.
    private static readonly object StunKey = new object();

    // Mirrors IsStunned so ApplyStunToMotor only touches the motor on the frame stun starts or ends,
    // like ApplySlowToMotor with appliedSlow.
    private bool stunAppliedToMotor;

    // StatusEffectSpec carries no source actor (a tested type shared by every kind, most with no
    // "caster"), so the source for kill credit is tracked here. Burn stacks with StackRule.Refresh, so
    // at most one is live and "most recent burn owns credit" is exactly right; revisit if burn ever stacks.
    private int burnSourceActorNumber = -1;

    // The ability id the CURRENT burn was applied with; same "most recent burn owns it" reasoning.
    private int burnAbilityId = -1;

    // The server ms the CURRENT burn was last applied or refreshed. Its damage carries it
    // (DamageInfo.EffectPlacedMs), so a burn lit before its owner's respawn does not end the new shield,
    // while one re-lit after the respawn does (A26).
    private int burnAppliedMs;

    // Pushed into the motor only when the total changes, per AddSpeedMultiplier's contract.
    private float appliedSlow;

    /// <summary>Raised on the victim's own client every time a status is applied through Apply;
    /// PlayerTelemetry logs a `status` line from it. Carries the spec's abilityId (-1 when it did not come
    /// from an ability) and BOTH duration and magnitude for every kind, so seconds can be summed uniformly.</summary>
    public event System.Action<StatusKind, int, int, float, float> StatusApplied;

    // STUNNED / SLOWED label (D18). Statuses live only on the victim's client, so it publishes the label it
    // wears as a Player Property (StatusLabelProperty) and every other client reads that; the label over the
    // head is built on every copy, real state feeding the owner's and the property feeding the rest.
    private const int LabelEndToleranceMs = 120;
    private StatusLabelOverhead overheadLabel;
    private float stunTotal;
    private float slowTotal;
    private StatusLabel publishedLabel;
    private int publishedEndMs;
    private int publishedTotalMs;
    private bool publishedOnce;

    public bool IsStunned => state.IsActive(StatusKind.Stun);
    public bool IsInvulnerable => state.IsActive(StatusKind.Invulnerability);

    /// <summary>0..1 - what PlayerHealth multiplies incoming damage by.</summary>
    public float Vulnerability => state.Magnitude(StatusKind.Vulnerability);

    /// <summary>0..1 fraction of speed lost.</summary>
    public float Slow => state.Magnitude(StatusKind.Slow);

    /// <summary>0..1 - what PlayerHealth.CurrentDamageReduction() reports to DamageResolver; the one
    /// place every reduction source is combined.</summary>
    public float CurrentDamageReduction => reductionStack.Total;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerHealth = GetComponent<PlayerHealth>();
        lifecycle = GetComponent<PlayerLifecycle>();
        motor = GetComponent<PlayerMotor>();
        effectShield = GetComponent<Overpower.Dominion.IEffectShield>();

        state = new StatusEffectState(
            gameplayConfig != null ? gameplayConfig.SlowCap : 0f,
            gameplayConfig != null ? gameplayConfig.VulnerabilityCap : 0f);
        reductionStack = new DamageReductionStack();
    }

    private void Start()
    {
        overheadLabel = StatusLabelOverhead.TryCreate(this,
            playerHealth != null ? playerHealth.Theme : null,
            playerHealth != null ? playerHealth.OverheadCanvas : null);
    }

    /// <summary>The label this player wears, its time left and its starting length (for the bar). The
    /// owner reads its real status; every other copy reads what the owner published. Stun beats slow;
    /// None when neither runs and while dead (IsAlive is replicated, so every client hides the label at
    /// once; death does not clear the statuses, only the respawn does).</summary>
    public void TryGetStatusLabel(out StatusLabel label, out float remaining, out float total)
    {
        if (lifecycle != null && !lifecycle.IsAlive)
        {
            label = StatusLabel.None;
            remaining = 0f;
            total = 0f;
            return;
        }

        if (photonView.IsMine)
        {
            float stunLeft = state.Remaining(StatusKind.Stun);
            float slowLeft = state.Remaining(StatusKind.Slow);
            label = StatusLabelRule.Choose(stunLeft, slowLeft);
            remaining = label == StatusLabel.Stunned ? stunLeft : label == StatusLabel.Slowed ? slowLeft : 0f;
            total = label == StatusLabel.Stunned ? stunTotal : label == StatusLabel.Slowed ? slowTotal : 0f;
            return;
        }

        label = StatusLabel.None;
        remaining = 0f;
        total = 0f;
        Photon.Realtime.Player owner = photonView.Owner;
        if (owner == null || owner.CustomProperties == null
            || !owner.CustomProperties.TryGetValue(StatusLabelProperty.Key, out object raw)
            || !StatusLabelProperty.TryDecode(raw, out int publishedKind, out int endMs, out int durationMs))
            return;

        remaining = StatusLabelRule.SecondsLeft(endMs, PhotonNetwork.ServerTimestamp);
        if (remaining <= 0f)
            return;

        label = (StatusLabel)publishedKind;
        total = durationMs / 1000f;
    }

    /// <summary>Owner only: publishes the label when it or its end changes (a refresh), never every frame.</summary>
    private void PublishStatusLabel()
    {
        stunTotal = StatusLabelRule.TrackedTotal(stunTotal, state.Remaining(StatusKind.Stun));
        slowTotal = StatusLabelRule.TrackedTotal(slowTotal, state.Remaining(StatusKind.Slow));

        if (!PhotonNetwork.InRoom || photonView.Owner == null)
            return;

        TryGetStatusLabel(out StatusLabel label, out float remaining, out float total);
        int endMs = label == StatusLabel.None ? 0 : unchecked(PhotonNetwork.ServerTimestamp + Mathf.RoundToInt(remaining * 1000f));
        int totalMs = Mathf.RoundToInt(total * 1000f);

        if (!StatusLabelRule.ShouldPublish(publishedOnce, publishedLabel, publishedEndMs, publishedTotalMs,
                                           label, endMs, totalMs, LabelEndToleranceMs))
            return;

        publishedOnce = true;
        publishedLabel = label;
        publishedEndMs = endMs;
        publishedTotalMs = totalMs;
        photonView.Owner.SetCustomProperties(new Hashtable
        {
            { StatusLabelProperty.Key, label == StatusLabel.None ? StatusLabelProperty.None() : StatusLabelProperty.Encode((int)label, endMs, totalMs) },
        });
    }

    private void Update()
    {
        if (!photonView.IsMine)
        {
            overheadLabel?.Tick(); // a remote copy only shows the label the owner published
            return; // no other client simulates your statuses, as PlayerHealth
        }

        // Order matters: ConsumeBurnDamage reports the burn as it stood before this step, then Tick
        // ages every status, that burn included, by the same deltaTime.
        float burn = state.ConsumeBurnDamage(Time.deltaTime);
        state.Tick(Time.deltaTime);
        reactiveInvulnerability.Tick(Time.deltaTime);

        // ORDER IS LOAD-BEARING. ApplyBurnDamage re-enters PlayerHealth.ApplyDamage, which can call
        // TryConsumeReactiveInvulnerability, which calls Apply(), which ADDS A DICTIONARY ENTRY to
        // `state`. That is only safe because both Tick calls have already returned. Do not move
        // ApplyBurnDamage above them.
        if (burn > 0f)
            ApplyBurnDamage(burn);

        ApplySlowToMotor();
        ApplyStunToMotor(); // every frame, so a stun that just expired unfreezes the same frame
        PublishStatusLabel();
        overheadLabel?.Tick();
    }

    /// <summary>
    /// Applies one timed status effect. sourceActorNumber only matters for Burn (see burnSourceActorNumber)
    /// and defaults to -1 (unknown).
    ///
    /// Owner-only: every client's copy of a projectile or beam can reach the target and call this, but
    /// only the target's own client may act. A status applied to a remote copy would run forever, since
    /// only Update (also owner-only) ticks it down.
    /// </summary>
    public void Apply(in StatusEffectSpec spec, int sourceActorNumber = -1)
    {
        if (!photonView.IsMine)
        {
            // This client simulates its own effect on its copy of someone else; if it hits a living enemy,
            // our respawn shield hears of it (A25). Nothing is applied here, only the victim's client does.
            Overpower.Dominion.RespawnShield.NoteMyEffectOnCopy(photonView, sourceActorNumber, spec.effectPlacedMs);
            return;
        }

        // A respawned player is untouchable: no enemy stun, slow, burn or vulnerability while the bubble
        // is up (A24). Own and teammate statuses land as always.
        if (effectShield != null && effectShield.StopsEnemyEffectFrom(sourceActorNumber))
            return;

        if (spec.kind == StatusKind.Burn)
        {
            burnSourceActorNumber = sourceActorNumber;
            burnAbilityId = spec.abilityId;
            burnAppliedMs = PhotonNetwork.ServerTimestamp;
        }

        state.Apply(spec);
        ApplyStunToMotor(); // freeze at once so the stun and the movement it blocks read as the same frame

        // Both raw numbers for every kind, not one picked per kind.
        StatusApplied?.Invoke(spec.kind, sourceActorNumber, spec.abilityId, spec.duration, spec.magnitude);
    }

    /// <summary>IStatusReceiver's generic entry point, for callers that found this through
    /// GetComponentInParent&lt;IStatusReceiver&gt;. Forwards to Apply.</summary>
    public void ApplyStatus(in StatusEffectSpec spec, int sourceActor) => Apply(spec, sourceActor);

    /// <summary>Owner only, like Apply. The Invulnerability ultimate's cast (ReactiveInvulnerabilityState).
    /// The ability passes every number it owns, so they stay in one home on its prefab.</summary>
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
    /// must return default() before resolving any of it.
    ///
    /// On a trigger this applies the ordinary Invulnerability status for its own span, so the funnel's
    /// IsInvulnerable check, the `status` telemetry line and the motor freeze keep working unchanged.
    /// Stun is NOT part of that by default: only applied when cachedStunSeconds is above 0
    /// (InvulnerabilityAbility's stunSeconds is where a designer dials a drawback back in). Whichever
    /// spans apply start HERE, AT THE HIT, never at the cast: freezing a caster during the armed window
    /// would punish a cast nobody answered.
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

    /// <summary>HitVerdictRule.Classify's IArmedShield: the damage funnel asks through the interface so it
    /// can be tested with a fake. Explicit so ordinary callers use the named method above.</summary>
    bool IArmedShield.TryConsume(float damageAmount) => TryConsumeReactiveInvulnerability(damageAmount);

    /// <summary>Owner only, read once. InvulnerabilityAbility's OwnerTick sends the shield's phase to
    /// every client, the one thing the other machines cannot work out for themselves.</summary>
    public bool ConsumeReactiveInvulnerabilityTrigger()
    {
        bool triggered = reactiveInvulnerabilityJustTriggered;
        reactiveInvulnerabilityJustTriggered = false;
        return triggered;
    }

    /// <summary>
    /// Clears the arm AND any pending trigger, but leaves an immunity that has ALREADY started alone: by
    /// then it is an ordinary StatusKind.Invulnerability running on its own timer. Disarming matters for
    /// an UNANSWERED cast: swap the ultimate away inside the armed window and, without this, a later hit
    /// was still nullified with no sphere on any screen, and the stale trigger flag could draw a sphere on
    /// a player who is not immune once Invulnerability is re-equipped. Cutting a running immunity short
    /// would be a new way to lose the shield's protection, so it runs on purpose.
    ///
    /// Owner only. Called from InvulnerabilityAbility's Interrupt(Unequipped), which fires on EVERY
    /// client (PlayerLoadout applies the same synced slot change everywhere). Harmless on a remote copy:
    /// only the owner ever arms, so a remote copy's arm is already clear.
    /// </summary>
    public void DisarmReactiveInvulnerability()
    {
        if (!photonView.IsMine)
            return;

        reactiveInvulnerability.Clear();
        reactiveInvulnerabilityJustTriggered = false;
    }

    /// <summary>
    /// The one-caster-decides path: for an effect that only ONE client resolves and tells the victim
    /// about, unlike every status above, which every client resolves for itself off a shared seed.
    /// Sonic pulse's player-into-player collision stun is the first user: only the pulse owner's
    /// physics sees that collision.
    ///
    /// Applies locally when this IS the local player; otherwise RPCs the one owner who may apply it.
    /// Never RpcTarget.All: every other client would also try to apply a status meant for one victim.
    /// </summary>
    public void RequestOnOwner(in StatusEffectSpec spec, int sourceActor)
    {
        if (photonView.IsMine)
        {
            Apply(spec, sourceActor);
            return;
        }

        // abilityId is appended LAST: appending an RPC parameter does not touch the RpcList (it indexes
        // method NAMES, see WeaponFiring.RPC_FireWeapon), but every client must run the same build for
        // the extra parameter to line up.
        photonView.RPC(nameof(RPC_ApplyStatusFromPeer), photonView.Owner,
                       (byte)spec.kind, spec.duration, spec.magnitude, sourceActor, spec.abilityId);
    }

    /// <summary>
    /// RequestOnOwner's wire side. Rebuilds the spec on the owning machine and applies it through
    /// Apply; the stacking rule comes from `kind` alone (StatusEffectState.RuleFor).
    /// </summary>
    [PunRPC]
    private void RPC_ApplyStatusFromPeer(byte kind, float duration, float magnitude, int sourceActor, int abilityId)
    {
        var spec = new StatusEffectSpec { kind = (StatusKind)kind, duration = duration, magnitude = magnitude, abilityId = abilityId };
        Apply(spec, sourceActor);
    }

    /// <summary>Adds or replaces one source of damage reduction. Owner-only, like ApplyDamage.</summary>
    public void AddDamageReduction(object key, float fraction)
    {
        if (!photonView.IsMine)
            return;

        reductionStack.Set(key, fraction);
    }

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
        burnAppliedMs = 0;
        reductionStack.Clear();
        DisarmReactiveInvulnerability(); // death and respawn must never carry an armed shield or stale trigger forward
        ApplySlowToMotor(); // slow is now 0: drop the motor's multiplier
        ApplyStunToMotor(); // same for stun: no freeze left behind
        if (photonView != null && photonView.IsMine)
            PublishStatusLabel(); // a respawn wipes the label off every client at once (death is covered by TryGetStatusLabel's alive check)
    }

    public float Remaining(StatusKind kind) => state.Remaining(kind);

    /// <summary>Burn goes through the one damage funnel, never straight off health.</summary>
    private void ApplyBurnDamage(float burn)
    {
        if (playerHealth == null)
            return;

        Teams.TryGetTeam(burnSourceActorNumber, out int sourceTeamId);
        playerHealth.ApplyDamage(PlacedEffects.StatusBurn(burn, burnSourceActorNumber, sourceTeamId, transform.position, burnAbilityId, burnAppliedMs));
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
    /// Stun freezes movement like Slow throttles it, through a keyed multiplier, but under StunKey
    /// rather than `this`, so the two compose: a stunned AND slowed player is frozen the instant the
    /// stun lands and resumes at the slowed speed, not full speed, once it lifts.
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
