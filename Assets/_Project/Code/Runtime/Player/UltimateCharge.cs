using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

/// <summary>
/// Thin owner-only wrapper around UltimateChargeState: the meter every ultimate module reads in
/// AbilityModule.IsReady (IsFull) and spends in TryBuildCast (Spend).
///
/// OWNER-ONLY STATE, LIKE STUN OR OVERHEAT: this body's CombatFeed (its own actor's) is raised only on
/// the machine that simulates it, so every subscription is gated on photonView.IsMine. The feed is
/// per actor because the host simulates its human and every bot: one shared event would charge them
/// all from each other's hits.
///
/// NOT RESET ON RESPAWN: PlayerLifecycle.HandleAliveChanged refills cooldowns but deliberately
/// never touches this; dying must not cost the ultimate you were building toward.
///
/// NOT REPLICATED: PlayerNetSync stays the only observable; readiness is decided on the caster's
/// machine, the same trust model as every other cast.
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
             "already key off (CombatFeed.DamageDealt). Claude's number.")]
    private float chargePerDamageDealt = 1f;

    [SerializeField, Tooltip("Charge earned per point of damage YOU take - armor + health actually " +
             "removed from you (PlayerHealth.Damaged). Half the dealt rate on purpose: taking hits " +
             "should help less than landing them, or turtling would out-charge fighting. Claude's number.")]
    private float chargePerDamageTaken = 0.5f;

    [SerializeField, Tooltip("Charge earned for landing the killing blow (CombatFeed.Takedown" +
             "(true)). Claude's number.")]
    private float chargePerKill = 150f;

    [SerializeField, Tooltip("Charge earned for an assist - damage within the assist window, but " +
             "someone else finished it (CombatFeed.Takedown(false)). Claude's number.")]
    private float chargePerAssist = 75f;

    private PhotonView photonView;
    private PlayerHealth playerHealth;
    private UltimateChargeState state;
    private CombatFeed feed; // the one OnEnable subscribed to, so OnDisable leaves that same one

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

        // A silent null would leave every ultimate permanently unready with no clue why.
        if (photonView == null)
            Debug.LogError($"[UltimateCharge] {name}: no PhotonView on the player root - cannot tell which machine owns this meter.");
        if (playerHealth == null)
            Debug.LogError($"[UltimateCharge] {name}: no PlayerHealth on the player root - damage-taken charge cannot work.");
    }

    private void OnEnable()
    {
        // Owner-only: stays off entirely on a remote copy (see the class comment).
        if (photonView == null || !photonView.IsMine)
            return;

        if (playerHealth != null)
            playerHealth.Damaged += HandleDamaged;
        feed = CombatFeed.For(photonView.OwnerActorNr);
        feed.DamageDealt += HandleDamageDealt;
        feed.Takedown += HandleTakedown;
    }

    private void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.Damaged -= HandleDamaged;
        if (feed != null)
        {
            feed.DamageDealt -= HandleDamageDealt;
            feed.Takedown -= HandleTakedown;
            feed = null;
        }
    }

    /// <summary>Owner only. Refused unless full; called by an ultimate's own TryBuildCast only.</summary>
    public bool Spend() => state.Spend();

    /// <summary>Owner only. An ultimate charge pack's share of the meter.</summary>
    public void AddFraction(float fraction) => state.AddFraction(fraction);

    /// <summary>Owner only. F1's "Fill Ultimate" (TestRangePanel).</summary>
    public void Fill() => state.Fill();

    /// <summary>Owner only. The fresh start at match-live empties the meter built up in warm-up combat.</summary>
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

        // The prefab asset also runs OnValidate, before Awake has built a state.
        if (state != null)
            state.Retune(maxCharge, chargePerDamageDealt, chargePerDamageTaken, chargePerKill, chargePerAssist);
    }
}
