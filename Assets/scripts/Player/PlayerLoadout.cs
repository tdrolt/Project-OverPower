using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Net;
using Overpower.UI;
using Overpower.Weapons;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// What this player is carrying - one weapon and up to three abilities - and the ONLY thing that
/// changes it. The shop, the test range and spawn all come through here; WeaponFiring.SetWeapon and
/// AbilityRunner.Equip only apply a change to this machine, and this class is what makes every other
/// machine agree.
///
/// A loadout is state, not an event (CODING-STANDARDS section 5, rule 2): a late joiner needs what
/// everyone holds, which an unbuffered RPC cannot tell them. So it lives in four Photon Custom
/// Properties (keys in LoadoutProperties), like PlayerLifecycle's alive state.
///
/// The flow, mirroring PlayerLifecycle.SetAlive:
///  - the OWNER applies a change locally first, so it feels instant, then publishes it;
///  - every OTHER client applies it in OnPlayerPropertiesUpdate;
///  - a LATE JOINER reads the current values in Start.
///
/// RPC_FireWeapon carries the weapon id per shot, but that alone leaves every remote copy's
/// WeaponFiring.Weapon on the starting weapon; the property is what scoreboards and kill feeds read.
///
/// Deliberately NOT IPunObservable - PlayerNetSync is the player's only observable.
/// </summary>
public class PlayerLoadout : MonoBehaviourPun, IInRoomCallbacks
{
    private static readonly AbilitySlot[] AbilitySlots =
    {
        AbilitySlot.Attachment, AbilitySlot.Ultimate, AbilitySlot.Mobility
    };

    [SerializeField, Tooltip("Match tuning asset. Free Loadout decides whether the ultimate slot " +
             "starts with the prefab's starting ultimate (free-test mode) or empty, to be bought " +
             "through the shop (the real economy). Every other starting slot is unaffected.")]
    private GameplayConfig gameplayConfig;

    private WeaponFiring weaponFiring;
    private AbilityRunner abilityRunner;
    private PlayerHealth playerHealth;

    // Captured before anything can change it; a remote copy falls back to this when a property is
    // missing or unreadable.
    private int startingWeaponId = LoadoutProperties.Empty;

    private void Awake()
    {
        weaponFiring = GetComponent<WeaponFiring>();
        abilityRunner = GetComponent<AbilityRunner>();
        playerHealth = GetComponent<PlayerHealth>();

        if (weaponFiring == null)
            Debug.LogError($"[PlayerLoadout] {name}: no WeaponFiring on the player root - the weapon cannot be replicated.");
        if (abilityRunner == null)
            Debug.LogError($"[PlayerLoadout] {name}: no AbilityRunner on the player root - abilities cannot be equipped.");
        if (playerHealth == null)
            Debug.LogError($"[PlayerLoadout] {name}: no PlayerHealth on the player root - armor upgrade levels cannot be replicated.");
        // Only a warning: a missing config fails OPEN to the always-free starting kit (see Start())
        // rather than locking every player out of their ultimate for the whole match.
        if (gameplayConfig == null)
            Debug.LogWarning($"[PlayerLoadout] {name}: GameplayConfig is not assigned - the ultimate slot will always start with the prefab's default, even with Free Loadout off.");
    }

    private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
    private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

    private void Start()
    {
        // WeaponFiring equips its starting weapon in its own Awake, which has already run.
        if (weaponFiring != null && weaponFiring.Weapon != null)
            startingWeaponId = weaponFiring.Weapon.Id;

        if (photonView.IsMine && photonView.Owner != null && photonView.Owner.HasRejoined)
        {
            // A REJOINED player keeps their loadout (D21): the room kept their Player Properties, so this is
            // the late joiner's read, never the starting-kit publish below, which would overwrite what they bought.
            ApplyFromProperties(PhotonNetwork.LocalPlayer.CustomProperties, onlyKeysPresent: false);
        }
        else if (photonView.IsMine)
        {
            // Publish what the prefab starts with, so every other client - and anyone who joins
            // later - reads the same loadout rather than guessing from their own prefab copy.
            var props = new Hashtable();
            if (startingWeaponId != LoadoutProperties.Empty)
                props[LoadoutProperties.WeaponKey] = startingWeaponId;

            // With the real economy on (Free Loadout off) the ultimate slot starts EMPTY and must be
            // bought (GDD p.18); every other slot starts at the prefab's default. A respawn never
            // re-runs Start() (same player object all match), so a bought ultimate is never lost.
            foreach (AbilitySlot slot in AbilitySlots)
            {
                int startingId = StartingAbilityId(slot);
                ApplyAbility(slot, startingId);
                props[LoadoutProperties.KeyFor(slot)] = EquippedAbilityId(slot);
            }

            // Published explicitly even though PlayerHealth starts at 0/0, so the Custom Properties
            // are self-describing rather than relying on a missing key.
            props[LoadoutProperties.ArmorAbsorbLevelKey] = playerHealth != null ? playerHealth.AbsorbLevel : 0;
            props[LoadoutProperties.ArmorRechargeLevelKey] = playerHealth != null ? playerHealth.RechargeLevel : 0;

            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            LogLoadout();
        }
        else if (photonView.Owner != null)
        {
            // A late joiner, or a copy spawned after the owner chose: read the current values, with
            // prefab defaults for anything missing.
            ApplyFromProperties(photonView.Owner.CustomProperties, onlyKeysPresent: false);
        }
    }

    /// <summary>The starting kit's id for one ability slot: the ONE definition Start and ResetForMatchStart
    /// share. The ultimate is the exception: it starts EMPTY unless the shop is free
    /// (ShopPricing.StartingUltimateHandedOut: Free Loadout or the pre-live warm-up sandbox, never a live
    /// Dominion match, whose ultimate is a break pick), when it carries the prefab's assigned starting
    /// ultimate. Every other slot starts at the prefab's default. The live reset calls this again once the
    /// shop is no longer free, so the ultimate starts empty from there on.</summary>
    private int StartingAbilityId(AbilitySlot slot)
    {
        bool ultimateStartsEmpty = !ShopPricing.StartingUltimateHandedOut(gameplayConfig);
        if (ultimateStartsEmpty && slot == AbilitySlot.Ultimate)
            return LoadoutProperties.Empty;

        return abilityRunner != null ? abilityRunner.StartingId(slot) : LoadoutProperties.Empty;
    }

    /// <summary>The fresh start at match-live puts EVERY slot back to the starter kit: the weapon and all
    /// three ability slots (to StartingAbilityId). The prefab ships every ability slot empty, so the first
    /// pick into Mobility or Attachment is free again, like a brand new player (ShopRules.AbilityPrice).
    /// Armour drops to level 0/0 too: PlayerLifecycle.ResetForMatchStart calls this BEFORE
    /// PlayerHealth.ResetForRespawn, so the capacity is already 0 when that refill decides what "full"
    /// means. One Hashtable publish, apply-then-publish like every write here. Owner only.</summary>
    public void ResetForMatchStart()
    {
        if (!photonView.IsMine)
            return;

        var props = new Hashtable();

        if (startingWeaponId != LoadoutProperties.Empty && weaponFiring != null && weaponFiring.SetWeapon(startingWeaponId))
            props[LoadoutProperties.WeaponKey] = startingWeaponId;

        foreach (AbilitySlot slot in AbilitySlots)
        {
            int startingId = StartingAbilityId(slot);
            ApplyAbility(slot, startingId);
            props[LoadoutProperties.KeyFor(slot)] = EquippedAbilityId(slot);
        }

        if (playerHealth != null)
        {
            playerHealth.SetArmorLevels(0, 0);
            props[LoadoutProperties.ArmorAbsorbLevelKey] = playerHealth.AbsorbLevel;
            props[LoadoutProperties.ArmorRechargeLevelKey] = playerHealth.RechargeLevel;
        }

        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        LogLoadout();
    }

    // ---- the owner's two writes ------------------------------------------------------------------

    /// <summary>Owner only. Swaps the weapon on this machine, then tells everyone. An unknown id
    /// changes nothing and publishes nothing.</summary>
    public void SetWeapon(int weaponId)
    {
        if (!photonView.IsMine || weaponFiring == null)
            return;

        if (!weaponFiring.SetWeapon(weaponId))
            return;

        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { LoadoutProperties.WeaponKey, weaponId } });
        LogLoadout();
    }

    /// <summary>Owner only. Puts an ability in a slot (or LoadoutProperties.Empty to clear it) on
    /// this machine, then tells everyone. A refused equip (wrong slot, unknown id) publishes
    /// nothing.</summary>
    public void SetAbility(AbilitySlot slot, int abilityId)
    {
        string key = LoadoutProperties.KeyFor(slot);
        if (!photonView.IsMine || abilityRunner == null || key == null)
            return;

        ApplyAbility(slot, abilityId);
        if (EquippedAbilityId(slot) != abilityId)
            return;

        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { key, abilityId } });
        LogLoadout();
    }

    /// <summary>Owner only. Applies new armor levels here, then tells everyone. Makes no decision of its
    /// own: the caller already ran the levels through Combat.ArmorUpgradePath. Required for correctness,
    /// not only for late joiners: a remote copy clamps synced armor to its OWN capacity
    /// (ArmorState.SetFromNetwork), which stays at level 0 until this replicates.</summary>
    public void SetArmorLevels(int absorbLevel, int rechargeLevel)
    {
        if (!photonView.IsMine || playerHealth == null)
            return;

        playerHealth.SetArmorLevels(absorbLevel, rechargeLevel);

        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { LoadoutProperties.ArmorAbsorbLevelKey, absorbLevel },
            { LoadoutProperties.ArmorRechargeLevelKey, rechargeLevel }
        });
        LogLoadout();
    }

    // ---- every other client -------------------------------------------------------------------

    public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (photonView.Owner == null || targetPlayer != photonView.Owner)
            return;

        // The owner already applied this before publishing. Photon echoes your own property writes
        // back, and reacting to the echo would repeat the swap and interrupt the freshly equipped
        // ability. Same guard as PlayerLifecycle.
        if (photonView.IsMine)
            return;

        ApplyFromProperties(changedProps, onlyKeysPresent: true);
    }

    /// <summary>
    /// Applies whichever loadout keys a property set holds. onlyKeysPresent is true for an update
    /// (a change to the weapon must not reset the abilities to their defaults) and false for the
    /// initial read, where a missing key means "never set" and the prefab default applies.
    /// </summary>
    private void ApplyFromProperties(IDictionary<object, object> props, bool onlyKeysPresent)
    {
        bool touched = false;

        if (weaponFiring != null && (!onlyKeysPresent || props.ContainsKey(LoadoutProperties.WeaponKey)))
        {
            int weaponId = LoadoutProperties.ReadInt(props, LoadoutProperties.WeaponKey, startingWeaponId);
            if (weaponId != LoadoutProperties.Empty && (weaponFiring.Weapon == null || weaponFiring.Weapon.Id != weaponId))
                weaponFiring.SetWeapon(weaponId);
            touched = true;
        }

        foreach (AbilitySlot slot in AbilitySlots)
        {
            string key = LoadoutProperties.KeyFor(slot);
            if (onlyKeysPresent && !props.ContainsKey(key))
                continue;

            int fallback = abilityRunner != null ? abilityRunner.StartingId(slot) : LoadoutProperties.Empty;
            ApplyAbility(slot, LoadoutProperties.ReadInt(props, key, fallback));
            touched = true;
        }

        // Both armor keys are published together, so either present means both are; each still falls
        // back to playerHealth's OWN current level rather than 0, so an update can never silently
        // reset the other path.
        if (playerHealth != null && (!onlyKeysPresent ||
            props.ContainsKey(LoadoutProperties.ArmorAbsorbLevelKey) ||
            props.ContainsKey(LoadoutProperties.ArmorRechargeLevelKey)))
        {
            int absorbLevel = LoadoutProperties.ReadInt(props, LoadoutProperties.ArmorAbsorbLevelKey, playerHealth.AbsorbLevel);
            int rechargeLevel = LoadoutProperties.ReadInt(props, LoadoutProperties.ArmorRechargeLevelKey, playerHealth.RechargeLevel);
            playerHealth.SetArmorLevels(absorbLevel, rechargeLevel);
            touched = true;
        }

        if (touched)
            LogLoadout();
    }

    private void ApplyAbility(AbilitySlot slot, int abilityId)
    {
        if (abilityRunner != null)
            abilityRunner.Equip(slot, abilityId);
    }

    private int EquippedAbilityId(AbilitySlot slot) =>
        abilityRunner != null ? abilityRunner.EquippedId(slot) : LoadoutProperties.Empty;

    /// <summary>One line per applied change on every client, so a two-client test can compare what each
    /// machine believes a player holds.</summary>
    private void LogLoadout()
    {
        int weaponId = weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : LoadoutProperties.Empty;
        Debug.Log($"[LOADOUT] actor={photonView.OwnerActorNr} W={weaponId} " +
                  $"E={EquippedAbilityId(AbilitySlot.Attachment)} U={EquippedAbilityId(AbilitySlot.Ultimate)} " +
                  $"M={EquippedAbilityId(AbilitySlot.Mobility)} " +
                  $"A={(playerHealth != null ? playerHealth.AbsorbLevel : 0)}/{(playerHealth != null ? playerHealth.RechargeLevel : 0)} " +
                  $"isMine={photonView.IsMine}");
    }

    // Unused IInRoomCallbacks members.
    public void OnPlayerEnteredRoom(Player newPlayer) { }
    public void OnPlayerLeftRoom(Player otherPlayer) { }
    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
    public void OnMasterClientSwitched(Player newMasterClient) { }
}
