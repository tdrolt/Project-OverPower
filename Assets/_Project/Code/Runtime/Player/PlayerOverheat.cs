using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

/// <summary>
/// Thin MonoBehaviour wrapper around one player's OverheatState - the resource the primary weapon
/// and every ability spend, and which locks out both once it hits max.
///
/// Heat is local to whoever generated it and nothing reads another player's heat: owner-only, no
/// replication.
///
/// Full overheat silences the primary weapon AND every ability, a deliberate designer rule accepted
/// on the condition that IsWarning exists (see OverheatState and 00-master-plan.md), so the
/// silence reads as the player's own mistake.
/// </summary>
public class PlayerOverheat : MonoBehaviour
{
    [SerializeField, Tooltip("Match tuning asset. Overheat max, decay timing and the warning " +
             "threshold all come from here, so retuning never means opening this script.")]
    private GameplayConfig gameplayConfig;

    private PhotonView photonView;
    private OverheatState overheat;
    private PlayerInputRouter input;

    // OverPower "nullif[ies] the overheat mechanic" while active (GDD p.20). Keyed like PlayerMotor's
    // speedMultipliers rather than a bool, so another system can add its own key without knowing who
    // else holds one; the last key to clear lifts the suppression.
    private readonly HashSet<object> suppressionKeys = new HashSet<object>();

    /// <summary>True while any key suppresses - Add becomes a no-op, existing heat is untouched.</summary>
    public bool IsSuppressed => suppressionKeys.Count > 0;

    public float Heat => overheat.Heat;

    /// <summary>0..1, for a HUD bar.</summary>
    public float Normalised => overheat.Normalised;

    /// <summary>True from the instant heat maxes out until it decays all the way back to zero.</summary>
    public bool IsSilenced => overheat.IsSilenced;

    /// <summary>True once heat crosses the warning threshold, before the silence hits; never together with IsSilenced.</summary>
    public bool IsWarning => overheat.IsWarning;

    public bool CanAct => overheat.CanAct;

    /// <summary>False when ventWindow &lt;= 0 (Vent off), so the HUD band hides instead of reading a
    /// stale Missed (OverheatState.VentEnabled/VentBandLookRule).</summary>
    public bool VentEnabled => overheat.VentEnabled;

    public bool IsVentWindowOpen => overheat.IsVentWindowOpen;

    public VentOutcome VentOutcome => overheat.Outcome;

    /// <summary>Heat fraction the fill sits at when the vent window opens: the band's upper edge.</summary>
    public float VentBandHighFraction => overheat.VentBandHighFraction;

    /// <summary>Heat fraction the fill sits at when the vent window closes: the band's lower edge.</summary>
    public float VentBandLowFraction => overheat.VentBandLowFraction;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        input = GetComponent<PlayerInputRouter>();

        // Loud on purpose: a silent null would make weapon and abilities never overheat.
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerOverheat] {name}: GameplayConfig is not assigned - falling back to hardcoded overheat numbers.");

        overheat = new OverheatState(
            gameplayConfig != null ? gameplayConfig.OverheatMax : 100f,
            gameplayConfig != null ? gameplayConfig.OverheatDecayDelay : 1.5f,
            gameplayConfig != null ? gameplayConfig.OverheatDecayPerSecond : 25f,
            gameplayConfig != null ? gameplayConfig.OverheatWarningThreshold : 80f,
            gameplayConfig != null ? gameplayConfig.VentDelay : 2f,
            gameplayConfig != null ? gameplayConfig.VentWindow : 0.8f,
            gameplayConfig != null && gameplayConfig.VentRandomTiming,
            gameplayConfig != null ? gameplayConfig.VentRandomDelayMin : 1.5f,
            gameplayConfig != null ? gameplayConfig.VentRandomDelayMax : 3f,
            // Heat is owner-only, so UnityEngine.Random is as local as everything else here.
            // OverheatState never calls UnityEngine itself; this is the source injected into it.
            () => UnityEngine.Random.value);
    }

    private void Update()
    {
        if (!photonView.IsMine)
            return; // no other client ticks it

        overheat.Tick(Time.deltaTime);
    }

    private void OnEnable()
    {
        // Safe unconditionally: a remote copy's PlayerInputRouter has its gameplayMap disabled and
        // never raises VentPressed.
        if (input != null)
            input.VentPressed += HandleVentPressed;
    }

    private void OnDisable()
    {
        if (input != null)
            input.VentPressed -= HandleVentPressed;
    }

    private void HandleVentPressed() => TryVent();

    /// <summary>Does nothing while IsSuppressed: a shot fired during OverPower must cost no heat at
    /// all, not merely decay faster.</summary>
    public void Add(float amount)
    {
        if (IsSuppressed)
            return;

        overheat.Add(amount);
    }

    /// <summary>The laser's half-cost refund when a shot connects.</summary>
    public void Refund(float amount) => overheat.Refund(amount);

    /// <summary>On death: zero the bar and lift any silence with it.</summary>
    public void Clear() => overheat.Clear();

    /// <summary>Vent's button (R). Public so Play Mode verification can call it directly: the Input
    /// System does not update while the Editor is unfocused.</summary>
    public VentResult TryVent() => overheat.TryVent();

    /// <summary>
    /// OverPowerBuff's hook, keyed like PlayerMotor.AddSpeedMultiplier/RemoveSpeedMultiplier. Existing
    /// heat and any silence in progress are left alone; only future Add calls are gated. Idempotent.
    /// </summary>
    public void SetSuppressed(object key, bool suppressed)
    {
        if (suppressed)
            suppressionKeys.Add(key);
        else
            suppressionKeys.Remove(key);
    }
}
