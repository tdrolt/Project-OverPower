using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

/// <summary>
/// Thin owner-only wrapper around UltimateChargeState for one player - the meter every ultimate
/// module reads through AbilityModule.IsReady and spends through TryBuildCast (Task 1.11 addendum's
/// own "fit with 1.0": IsReady => Owner.UltimateCharge.IsFull, TryBuildCast calls
/// Owner.UltimateCharge.Spend()). Lives on the player root beside PlayerHealth, so AbilityOwner can
/// cache it exactly like every other owner reference.
///
/// OWNER-ONLY STATE, LIKE STUN OR OVERHEAT: only your own machine should ever accrue your charge.
/// CombatEvents fires only on the machine that actually earned the damage or the takedown (see its
/// own class comment) - a remote copy of THIS player subscribing to it would be listening to events
/// raised by whichever OTHER player is local on that machine, and would silently add that player's
/// damage and kills onto this player's meter. Every subscription below is therefore gated on
/// photonView.IsMine, the same guard PlayerHealth and PlayerStatusEffects already use for their own
/// owner-only state.
///
/// NOT RESET ON RESPAWN (Task 1.11's own words) - PlayerLifecycle.HandleAliveChanged refills
/// cooldowns on death and respawn but deliberately never touches this: dying should not cost you the
/// ultimate you were building toward.
///
/// NOT REPLICATED - PlayerNetSync stays the player's only observable (see its class comment), and
/// nothing here needs to be seen by anyone but its own owner: every ultimate's readiness is decided
/// entirely on the caster's machine, the same trust model as every other cast.
/// </summary>
public class UltimateCharge : MonoBehaviour
{
    [Header("Charge sources - the GDD's own list: damage dealt, damage taken, kills/assists")]
    [SerializeField, Tooltip("Total charge needed before an ultimate is ready. Claude's number - " +
             "the GDD names the sources but not a scale for them. Can be changed while playing; " +
             "Current is clamped down to a lowered cap, never reset.")]
    private float maxCharge = 1000f;

    [SerializeField, Tooltip("Charge earned per point of damage YOU deal - armor + health actually " +
             "removed from the victim, the same figure the zip gun's reset and armor recharge " +
             "already key off (CombatEvents.LocalDamageDealt). Claude's number.")]
    private float chargePerDamageDealt = 1f;

    [SerializeField, Tooltip("Charge earned per point of damage YOU take - armor + health actually " +
             "removed from you (PlayerHealth.Damaged). Half the dealt rate on purpose: taking hits " +
             "should help less than landing them, or turtling would out-charge fighting. Claude's number.")]
    private float chargePerDamageTaken = 0.5f;

    [SerializeField, Tooltip("Charge earned for landing the killing blow (CombatEvents.LocalTakedown" +
             "(true)). Claude's number.")]
    private float chargePerKill = 150f;

    [SerializeField, Tooltip("Charge earned for an assist - damage within the assist window, but " +
             "someone else finished it (CombatEvents.LocalTakedown(false)). Claude's number.")]
    private float chargePerAssist = 75f;

    private PhotonView photonView;
    private PlayerHealth playerHealth;
    private UltimateChargeState state;

    public float Current => state.Current;
    public float Max => state.Max;

    /// <summary>The one gate every ultimate's IsReady reads.</summary>
    public bool IsFull => state.IsFull;

    /// <summary>0..1, for the HUD meter.</summary>
    public float Normalised => state.Normalised;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerHealth = GetComponent<PlayerHealth>();
        state = new UltimateChargeState(maxCharge, chargePerDamageDealt, chargePerDamageTaken,
                                        chargePerKill, chargePerAssist);

        // A silent null here would leave every ultimate permanently unreachable (IsReady always
        // false) with no clue in the console why - matches PlayerHealth/AbilityRunner's own pattern.
        if (photonView == null)
            Debug.LogError($"[UltimateCharge] {name}: no PhotonView on the player root - cannot tell which machine owns this meter.");
        if (playerHealth == null)
            Debug.LogError($"[UltimateCharge] {name}: no PlayerHealth on the player root - damage-taken charge cannot work.");
    }

    private void OnEnable()
    {
        // Owner-only - see the class comment. Every subscription below stays off entirely on a
        // remote copy of this player.
        if (photonView == null || !photonView.IsMine)
            return;

        if (playerHealth != null)
            playerHealth.Damaged += HandleDamaged;
        CombatEvents.LocalDamageDealt += HandleDamageDealt;
        CombatEvents.LocalTakedown += HandleTakedown;
    }

    private void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.Damaged -= HandleDamaged;
        CombatEvents.LocalDamageDealt -= HandleDamageDealt;
        CombatEvents.LocalTakedown -= HandleTakedown;
    }

    /// <summary>Owner only. Refused unless the meter is full - called by an ultimate's own
    /// TryBuildCast, never anywhere else.</summary>
    public bool Spend() => state.Spend();

    /// <summary>Owner only. F1's "Fill Ultimate" - see TestRangePanel.</summary>
    public void Fill() => state.Fill();

    /// <summary>2.7b Decision 6: the fresh start at match-live empties the meter built up during the match
    /// (warm-up combat included) - the one call to UltimateChargeState.Clear(). Owner only, like Fill/Spend
    /// above; a remote copy has nothing of its own to clear.</summary>
    public void ResetForMatchStart()
    {
        if (photonView == null || !photonView.IsMine)
            return;

        state.Clear();
    }

    private void HandleDamaged(DamageResult result, DamageInfo info) => state.AddDamageTaken(result.Total);
    private void HandleDamageDealt(float amount) => state.AddDamageDealt(amount);

    private void HandleTakedown(bool isKill)
    {
        if (isKill)
            state.AddKill();
        else
            state.AddAssist();
    }

    private void OnValidate()
    {
        maxCharge = Mathf.Max(0f, maxCharge);
        chargePerDamageDealt = Mathf.Max(0f, chargePerDamageDealt);
        chargePerDamageTaken = Mathf.Max(0f, chargePerDamageTaken);
        chargePerKill = Mathf.Max(0f, chargePerKill);
        chargePerAssist = Mathf.Max(0f, chargePerAssist);

        // Only a live component has a state to retune yet - the prefab asset itself also runs
        // OnValidate, before Awake has ever built one (AbilityModule.OnValidate's own guard).
        if (state != null)
            state.Retune(maxCharge, chargePerDamageDealt, chargePerDamageTaken, chargePerKill, chargePerAssist);
    }
}
