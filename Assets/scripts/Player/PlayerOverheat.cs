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

    public void Add(float amount) => overheat.Add(amount);

    /// <summary>The laser's half-cost refund when a shot connects.</summary>
    public void Refund(float amount) => overheat.Refund(amount);

    /// <summary>On death: zero the bar and lift any silence with it.</summary>
    public void Clear() => overheat.Clear();
}
