using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Net;
using Overpower.Weapons;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// What this player is carrying - one weapon and up to three abilities - and the ONLY thing that
/// changes it. The shop, the test range and spawn all come through here; WeaponFiring.SetWeapon and
/// AbilityRunner.Equip only apply a change to this machine, and this class is what makes every other
/// machine agree.
///
/// A loadout is state, not an event (CODING-STANDARDS section 5, rule 2): a player joining in five
/// minutes needs to know what everyone is holding, and an unbuffered RPC cannot tell them. So it
/// lives in four Photon Custom Properties (keys in LoadoutProperties), the same way PlayerLifecycle
/// keeps alive state.
///
/// The flow, mirroring PlayerLifecycle.SetAlive:
///  - the OWNER applies a change locally first, so it feels instant, then publishes it;
///  - every OTHER client applies it in OnPlayerPropertiesUpdate;
///  - a LATE JOINER reads the current values in Start.
///
/// Before this existed the weapon id was never replicated at all - RPC_FireWeapon carries it per
/// shot, so firing worked, but every remote copy's WeaponFiring.Weapon stayed on the starting weapon
/// forever. Anything that shows another player's weapon (a scoreboard, a kill feed) now reads true.
///
/// Deliberately NOT IPunObservable - PlayerNetSync is the player's only observable.
/// </summary>
public class PlayerLoadout : MonoBehaviourPun, IInRoomCallbacks
{
    private static readonly AbilitySlot[] AbilitySlots =
    {
        AbilitySlot.Equipment, AbilitySlot.Ultimate, AbilitySlot.Mobility
    };

    private WeaponFiring weaponFiring;
    private AbilityRunner abilityRunner;

    // The prefab's own starting weapon id, captured before anything can change it - a remote copy
    // falls back to this when a property is missing or unreadable.
    private int startingWeaponId = LoadoutProperties.Empty;

    private void Awake()
    {
        weaponFiring = GetComponent<WeaponFiring>();
        abilityRunner = GetComponent<AbilityRunner>();

        if (weaponFiring == null)
            Debug.LogError($"[PlayerLoadout] {name}: no WeaponFiring on the player root - the weapon cannot be replicated.");
        if (abilityRunner == null)
            Debug.LogError($"[PlayerLoadout] {name}: no AbilityRunner on the player root - abilities cannot be equipped.");
    }

    private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
    private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

    private void Start()
    {
        // WeaponFiring equips its starting weapon in its own Awake, which has already run.
        if (weaponFiring != null && weaponFiring.Weapon != null)
            startingWeaponId = weaponFiring.Weapon.Id;

        if (photonView.IsMine)
        {
            // Publish what the prefab starts with, so every other client - and anyone who joins
            // later - reads the same loadout rather than guessing from their own prefab copy.
            var props = new Hashtable();
            if (startingWeaponId != LoadoutProperties.Empty)
                props[LoadoutProperties.WeaponKey] = startingWeaponId;

            foreach (AbilitySlot slot in AbilitySlots)
            {
                ApplyAbility(slot, abilityRunner != null ? abilityRunner.StartingId(slot) : LoadoutProperties.Empty);
                props[LoadoutProperties.KeyFor(slot)] = EquippedAbilityId(slot);
            }

            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            LogLoadout();
        }
        else if (photonView.Owner != null)
        {
            // A late joiner, or a copy spawned after the owner already chose something: read the
            // current values, falling back to the prefab's defaults for anything missing.
            ApplyFromProperties(photonView.Owner.CustomProperties, onlyKeysPresent: false);
        }
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

    // ---- every other client -------------------------------------------------------------------

    public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (photonView.Owner == null || targetPlayer != photonView.Owner)
            return;

        // The owner already applied this before publishing it. Photon echoes your own property
        // writes back to you, and reacting to the echo would repeat the swap - and interrupt the
        // freshly equipped ability - for nothing. Same guard as PlayerLifecycle.
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

    /// <summary>One line per applied change, on every client, so a two-client test can compare what
    /// each machine believes a player is holding.</summary>
    private void LogLoadout()
    {
        int weaponId = weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : LoadoutProperties.Empty;
        Debug.Log($"[LOADOUT] actor={photonView.OwnerActorNr} W={weaponId} " +
                  $"E={EquippedAbilityId(AbilitySlot.Equipment)} U={EquippedAbilityId(AbilitySlot.Ultimate)} " +
                  $"M={EquippedAbilityId(AbilitySlot.Mobility)} isMine={photonView.IsMine}");
    }

    // Unused IInRoomCallbacks members.
    public void OnPlayerEnteredRoom(Player newPlayer) { }
    public void OnPlayerLeftRoom(Player otherPlayer) { }
    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
    public void OnMasterClientSwitched(Player newMasterClient) { }
}
