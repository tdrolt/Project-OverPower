using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

/// <summary>
/// Thin MonoBehaviour wrapper around one player's OverheatState - the resource the primary
/// weapon and every ability spend, and which locks out both once it hits max. Split out of
/// Multiplayer.cs (Task 0.10).
///
/// Heat is local to whoever generated it and nothing reads another player's heat, so this is
/// deliberately owner-only with no replication of its own.
///
/// Full overheat silences the primary weapon AND every ability - a harsher rule than a
/// weapon-only lockout, taken deliberately by the designer and accepted up to four seconds of
/// helplessness on the explicit condition that IsWarning exists at 80 heat, so the silence reads
/// as the player's own mistake rather than an arbitrary wall. See OverheatState's own doc and
/// 00-master-plan.md.
/// </summary>
public class PlayerOverheat : MonoBehaviour
{
    [SerializeField, Tooltip("Match tuning asset. Overheat max, decay timing and the warning " +
             "threshold all come from here, so retuning never means opening this script.")]
    private GameplayConfig gameplayConfig;

    private PhotonView photonView;
    private OverheatState overheat;

    // Task 2.6 (GDD p.20): OverPower "nullif[ies] the overheat mechanic" while active. Keyed the
    // same way PlayerMotor's speedMultipliers stack is (see its class comment) rather than a
    // single bool, so a second future system wanting the same lockout can add its own key without
    // needing to know whether OverPower (or anything else) already holds one - whichever key
    // clears last is the one that actually lifts the suppression.
    private readonly HashSet<object> suppressionKeys = new HashSet<object>();

    /// <summary>True while any key suppresses - Add becomes a no-op, existing heat is untouched.</summary>
    public bool IsSuppressed => suppressionKeys.Count > 0;

    public float Heat => overheat.Heat;

    /// <summary>0..1, for a HUD bar.</summary>
    public float Normalised => overheat.Normalised;

    /// <summary>True from the instant heat maxes out until it decays all the way back to zero.</summary>
    public bool IsSilenced => overheat.IsSilenced;

    /// <summary>True once heat crosses the warning threshold, but only before the silence hits -
    /// never true at the same time as IsSilenced.</summary>
    public bool IsWarning => overheat.IsWarning;

    public bool CanAct => overheat.CanAct;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();

        // A silent null here would make this player's weapon and abilities never overheat -
        // effectively free, permanent access to everything. Loud on purpose, matching PlayerHealth.
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerOverheat] {name}: GameplayConfig is not assigned - falling back to hardcoded overheat numbers.");

        overheat = new OverheatState(
            gameplayConfig != null ? gameplayConfig.OverheatMax : 100f,
            gameplayConfig != null ? gameplayConfig.OverheatDecayDelay : 1.5f,
            gameplayConfig != null ? gameplayConfig.OverheatDecayPerSecond : 25f,
            gameplayConfig != null ? gameplayConfig.OverheatWarningThreshold : 80f);
    }

    private void Update()
    {
        if (!photonView.IsMine)
            return; // Heat is local to whoever generated it - no other client ticks it.

        overheat.Tick(Time.deltaTime);
    }

    /// <summary>Does nothing while IsSuppressed (Task 2.6) - a shot fired during OverPower must
    /// cost no heat at all, not merely decay faster.</summary>
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

    /// <summary>
    /// OverPowerBuff's hook (Task 2.6, GDD p.20): keyed exactly like PlayerMotor.AddSpeedMultiplier/
    /// RemoveSpeedMultiplier (see the suppressionKeys field comment) - true adds key, false removes
    /// it. Existing heat and any silence already in progress are left alone; only future Add calls
    /// are gated. Idempotent either way (HashSet.Add/Remove no-op on a value already in/out).
    /// </summary>
    public void SetSuppressed(object key, bool suppressed)
    {
        if (suppressed)
            suppressionKeys.Add(key);
        else
            suppressionKeys.Remove(key);
    }
}
