using Photon.Pun;
using Photon.Pun.UtilityScripts;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;

/// <summary>
/// One player's health, armor and damage funnel. Replaces two copies that used to live in
/// Multiplayer.cs (TakeDamage for bullets, ApplyAoEDamage for area damage) and had already
/// diverged - only the bullet path applied the dash damage-reduction buff. ApplyDamage below is
/// now the only place damage maths happens.
///
/// Deliberately NOT IPunObservable: the PhotonView auto-finds observables on children too, so a
/// second one here would silently add a second serialization block. PlayerNetSync is the sole
/// observable and reads/writes through the members below.
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private GameplayConfig gameplayConfig;
    [SerializeField] private ArmorConfig armorConfig;
    [SerializeField] private Slider healthBar;

    private PhotonView photonView;

    private float health;
    private ArmorState armor;
    private PlayerStatusEffects statusEffects; // A status is not health - see PlayerStatusEffects.cs.

    // The two independent armor upgrade paths (Combat/ArmorUpgradePath), each an index into
    // ArmorConfig's own array. Everyone starts at level 0 on both, not on no armor. These survive
    // death - only the current fill of the pool (armor.Clear(), below) resets - because an upgrade
    // is bought, not lent.
    private int absorbLevel = 0;
    private int rechargeLevel = 0;
    private float secondsSinceCombat;
    private bool isDead;

    // Each blocked-damage reason logs once per match, not per hit - a teamfight would otherwise
    // fill a log that writes synchronously to disk in a build.
    private bool loggedSelfHitBlocked;
    private bool loggedFriendlyFireBlocked;

    public float Health => health;
    // A single ArmorState per client now, owner and remote alike - a remote client's copy is
    // written by SetHealthFromNetwork below through ArmorState.SetFromNetwork instead of a
    // separately mirrored field, so Armor means the same thing regardless of whose client reads it.
    public float Armor => armor.Current;
    public float ArmorCapacity => armor.Capacity;
    public int AbsorbLevel => absorbLevel;
    public int RechargeLevel => rechargeLevel;
    public float SecondsSinceCombat => secondsSinceCombat;
    public bool IsOutOfCombat => gameplayConfig != null && secondsSinceCombat >= gameplayConfig.OutOfCombatSeconds;
    public bool IsAlive => !isDead;
    public int TeamId => Teams.TryGetTeam(photonView.Owner, out int teamId) ? teamId : -1;
    public int ActorNumber => photonView.OwnerActorNr;

    /// <summary>True only on the machine this player belongs to - the same guard ApplyDamage
    /// already enforces, exposed so a mine (Task 1.8) can ask before it decides to trigger.</summary>
    public bool HasLocalAuthority => photonView.IsMine;
    public event System.Action<DamageResult, DamageInfo> Damaged;
    public event System.Action<DamageInfo> Died;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        statusEffects = GetComponent<PlayerStatusEffects>();

        // A silent null here would make this player un-damageable - the worst failure mode.
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerHealth] {name}: GameplayConfig is not assigned - cannot take damage.");
        if (armorConfig == null)
            Debug.LogError($"[PlayerHealth] {name}: ArmorConfig is not assigned - cannot take damage.");

        health = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
        armor = new ArmorState(armorConfig != null ? armorConfig.AbsorbFor(absorbLevel) : 0f,
                                armorConfig != null ? armorConfig.RechargeSecondsFor(rechargeLevel) : 6f,
                                armorConfig != null ? armorConfig.RefillSeconds : 2.5f);

        if (healthBar != null)
            healthBar.value = health;
    }

    private void Update()
    {
        // No other client should simulate your health or tick your armor recharge. isDead is
        // checked too: without it, armor kept climbing on a corpse (Update never used to look at
        // isDead), so how much armor you respawned with silently depended on how long the respawn
        // timer happened to take. ResetForRespawn resets this clock and decides the respawn armor
        // outright (ArmorConfig.RespawnWithFullArmor), so there is nothing useful to tick while dead.
        if (!photonView.IsMine || isDead)
            return;

        secondsSinceCombat += Time.deltaTime;

        // Burn is ticked by PlayerStatusEffects now, which routes it back through ApplyDamage below.
        armor.Tick(Time.deltaTime, secondsSinceCombat);
    }

    /// The one funnel every damage source goes through - see the class comment.
    public DamageResult ApplyDamage(in DamageInfo info)
    {
        // Bug 1.1: the victim is the sole authority on its own health, or every client would
        // subtract from its own copy and the owner's next serialization would fight it back.
        if (!photonView.IsMine || isDead)
            return default;

        // The Invulnerability ultimate depends on this: the funnel is the only place that can stop a hit for everyone.
        if (statusEffects != null && statusEffects.IsInvulnerable)
            return default;

        Photon.Realtime.Player sourcePlayer = PhotonNetwork.CurrentRoom?.GetPlayer(info.SourceActorNumber);

        // Your own damage cannot hurt you (e.g. dashing into your own shot).
        if (sourcePlayer != null && sourcePlayer == photonView.Owner)
        {
            if (!loggedSelfHitBlocked)
            {
                loggedSelfHitBlocked = true;
                Debug.Log("[DMG] blocked self-damage (reported once per match)");
            }
            return default;
        }

        // No friendly fire. AreSameTeam deliberately fails OPEN: an unknown team must never
        // silently make someone invulnerable.
        if (Teams.AreSameTeam(sourcePlayer, photonView.Owner))
        {
            if (!loggedFriendlyFireBlocked)
            {
                loggedFriendlyFireBlocked = true;
                Debug.Log($"[DMG] blocked friendly fire from {sourcePlayer?.NickName} (reported once per match)");
            }
            return default;
        }

        float vulnerability = statusEffects != null ? statusEffects.Vulnerability : 0f;
        DamageResult result = DamageResolver.Resolve(info.Amount, info.IgnoresArmor, health,
                                                       armor.Current, vulnerability, CurrentDamageReduction());
        armor.Absorb(result.ArmorAbsorbed);
        health -= result.HealthLost;
        secondsSinceCombat = 0f;

        if (healthBar != null)
            healthBar.value = health;

        Damaged?.Invoke(result, info);

        if (result.Lethal)
        {
            isDead = true;   // Latched before raising Died so a re-entrant hit cannot double-kill.
            armor.Clear();   // A corpse has no armor; ResetForRespawn decides what comes back.
            sourcePlayer?.AddScore(1);
            Died?.Invoke(info);
        }

        return result;
    }

    /// The one place damage reduction is read, as a 0..1 fraction for DamageResolver. That single
    /// location is the point of the damage funnel: the reduction used to be applied on the bullet
    /// path only and was silently absent from AoE, so the same buff did two different things
    /// depending on what hit you.
    ///
    /// Reads PlayerStatusEffects.CurrentDamageReduction (Task 1.0a), which combines every source -
    /// a dash buff, an armor upgrade - the same way Vulnerability above already does. Do not
    /// inline this away - every future source of damage reduction reports through here.
    private float CurrentDamageReduction() => statusEffects != null ? statusEffects.CurrentDamageReduction : 0f;

    // The OnCollisionEnter that used to sit here read damage off a Rigidbody bullet that had
    // collided with this player. Task 0.13 deleted it along with those bullets: projectiles are now
    // swept spherecasts that build a DamageInfo and call ApplyDamage above directly, so there is no
    // longer a physics collision to react to. Nothing replaced it, which is the point - there is
    // one way in.

    /// <summary>
    /// Applies a new pair of armor upgrade levels - the sink every client calls when
    /// PlayerLoadout replicates a purchase (or a late joiner reads one for the first time). Refills
    /// the pool to the new capacity immediately: this is a purchase, not a recharge, the same
    /// distinction ArmorState.SetTier documents.
    /// </summary>
    public void SetArmorLevels(int newAbsorbLevel, int newRechargeLevel)
    {
        // Clamped against the config's own array lengths, not just >= 0: a stale or malformed
        // replicated level (this arrives over the network via PlayerLoadout) must never let
        // absorbLevel/rechargeLevel sit out of range, which would otherwise misreport
        // ArmorUpgradePath.TotalUpgrades and could block every future upgrade for this player.
        if (armorConfig != null)
        {
            newAbsorbLevel = Mathf.Clamp(newAbsorbLevel, 0, Mathf.Max(0, armorConfig.AbsorbLevelCount - 1));
            newRechargeLevel = Mathf.Clamp(newRechargeLevel, 0, Mathf.Max(0, armorConfig.RechargeLevelCount - 1));
        }

        absorbLevel = newAbsorbLevel;
        rechargeLevel = newRechargeLevel;
        if (armorConfig != null)
            armor.SetTier(armorConfig.AbsorbFor(absorbLevel), armorConfig.RechargeSecondsFor(rechargeLevel));
    }

    public void ResetForRespawn()
    {
        health = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
        statusEffects?.ClearAll();
        secondsSinceCombat = 0f;
        isDead = false;

        // Controller decision [C]: respawn with full armor by default. RespawnWithFullArmor off
        // means a respawning player earns their armor back through the out-of-combat timer like
        // anyone else, the original design before this setting existed. A missing config fails
        // toward Clear() rather than assuming the field's true default, same as every other
        // missing-config fallback in this class.
        if (armorConfig != null && armorConfig.RespawnWithFullArmor)
            armor.RefillToFull();
        else
            armor.Clear();

        if (healthBar != null)
            healthBar.value = health;
    }

    /// Call when this player deals damage, so dealing it keeps you "in combat" the same way
    /// taking it does.
    public void NoteDealtDamage() => secondsSinceCombat = 0f;

    /// Called by PlayerNetSync's receive side on a non-owner. Health is written directly since
    /// only the owner ever simulates it; armor goes through ArmorState.SetFromNetwork rather than
    /// a plain assignment, since ArmorState has no public setter for Current by design (see its
    /// class comment) and clamping here keeps a stale or out-of-order packet from handing a
    /// remote client more armor than the pool can hold.
    public void SetHealthFromNetwork(float health, float armor)
    {
        this.health = health;
        this.armor.SetFromNetwork(armor);

        if (healthBar != null)
            healthBar.value = health;
    }
}
