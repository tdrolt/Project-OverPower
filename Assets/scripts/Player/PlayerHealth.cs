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
/// second one here would silently add a second serialization block. Multiplayer.cs stays the
/// sole observable and reads/writes through the members below.
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private GameplayConfig gameplayConfig;
    [SerializeField] private ArmorConfig armorConfig;
    [SerializeField] private Slider healthBar;

    private PhotonView photonView;
    private PlayerDashWithBuff dashBuff;

    private float health;
    private ArmorState armor;
    private PlayerStatusEffects statusEffects; // A status is not health - see PlayerStatusEffects.cs.
    private int armorTier = 0; // Everyone starts on tier 0 armor, not on none.
    private float secondsSinceCombat;
    private bool isDead;

    // ArmorState exposes no way to set Current to an arbitrary value (only Absorb, which only
    // decreases it, SetTier and Clear), and a non-owner never ticks its own (see Update). So a
    // remote client mirrors the owner's reported value here instead of writing through it.
    private float mirroredArmor;

    // Each blocked-damage reason logs once per match, not per hit - a teamfight would otherwise
    // fill a log that writes synchronously to disk in a build.
    private bool loggedSelfHitBlocked;
    private bool loggedFriendlyFireBlocked;

    public float Health => health;
    public float Armor => photonView.IsMine ? armor.Current : mirroredArmor;
    public int ArmorTier => armorTier;
    public float SecondsSinceCombat => secondsSinceCombat;
    public bool IsOutOfCombat => gameplayConfig != null && secondsSinceCombat >= gameplayConfig.OutOfCombatSeconds;
    public bool IsAlive => !isDead;
    public int TeamId => Teams.TryGetTeam(photonView.Owner, out int teamId) ? teamId : -1;
    public int ActorNumber => photonView.OwnerActorNr;
    public event System.Action<DamageResult, DamageInfo> Damaged;
    public event System.Action<DamageInfo> Died;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        dashBuff = GetComponent<PlayerDashWithBuff>();
        statusEffects = GetComponent<PlayerStatusEffects>();

        // A silent null here would make this player un-damageable - the worst failure mode.
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerHealth] {name}: GameplayConfig is not assigned - cannot take damage.");
        if (armorConfig == null)
            Debug.LogError($"[PlayerHealth] {name}: ArmorConfig is not assigned - cannot take damage.");

        health = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
        armor = new ArmorState(armorConfig != null ? armorConfig.AbsorbFor(armorTier) : 0f,
                                armorConfig != null ? armorConfig.RechargeSecondsFor(armorTier) : 6f);

        if (healthBar != null)
            healthBar.value = health;
    }

    private void Update()
    {
        if (!photonView.IsMine)
            return; // No other client should simulate your health or tick your armor recharge.

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
            armor.Clear();   // Per ArmorState's own doc: earned back post-respawn, not kept.
            sourcePlayer?.AddScore(1);
            Died?.Invoke(info);
        }

        return result;
    }

    /// Bridges to the old dash damage-reduction buff and converts its percent into the 0..1
    /// fraction DamageResolver expects. This single location is the actual fix this task exists
    /// for - the query used to live only on the bullet path and was silently absent from AoE. A
    /// later task replaces this with a real status-effect lookup.
    private float CurrentDamageReduction()
        => dashBuff != null && dashBuff.IsBuffActive() ? dashBuff.damageReductionPercent / 100f : 0f;

    /// Transitional: a later task replaces this Rigidbody bullet collision with swept
    /// spherecast projectiles that build a DamageInfo and call ApplyDamage directly.
    private void OnCollisionEnter(Collision collision)
    {
        if (!photonView.IsMine || !collision.gameObject.CompareTag("Bullet"))
            return;

        MultiplayerBulletController bullet = collision.gameObject.GetComponent<MultiplayerBulletController>();
        if (bullet == null || bullet.owner == null)
            return;

        Teams.TryGetTeam(bullet.owner, out int sourceTeam);
        ApplyDamage(new DamageInfo(bullet.damage, bullet.owner.ActorNumber, sourceTeam, -1,
                                    DamageSource.Projectile, false, collision.GetContact(0).point));
    }

    /// Kept with exactly this signature - Assets/scripts/Player/Aoe effect.cs calls it. This is
    /// where the divergence this task exists to fix goes away: AoE now runs through the same
    /// funnel as bullets, so dash damage reduction applies to it for the first time.
    public void ApplyAoEDamage(float damage, Photon.Realtime.Player caster)
    {
        if (!photonView.IsMine || caster == null)
            return;

        Teams.TryGetTeam(caster, out int sourceTeam);
        ApplyDamage(new DamageInfo(damage, caster.ActorNumber, sourceTeam, -1,
                                    DamageSource.Zone, false, transform.position));
    }

    public void SetArmorTier(int tier)
    {
        armorTier = tier;
        if (armorConfig != null)
            armor.SetTier(armorConfig.AbsorbFor(tier), armorConfig.RechargeSecondsFor(tier));
    }

    public void ResetForRespawn()
    {
        health = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
        statusEffects?.ClearAll();
        secondsSinceCombat = 0f;
        isDead = false;

        if (healthBar != null)
            healthBar.value = health;
    }

    /// Call when this player deals damage, so dealing it keeps you "in combat" the same way
    /// taking it does.
    public void NoteDealtDamage() => secondsSinceCombat = 0f;

    /// Called by the non-owner's read of OnPhotonSerializeView - see the mirroredArmor field
    /// comment above for why armor is stored separately rather than written into this player's
    /// own ArmorState, which only the owner ever runs.
    public void SetHealthFromNetwork(float health, float armor)
    {
        this.health = health;
        mirroredArmor = armor;

        if (healthBar != null)
            healthBar.value = health;
    }
}
