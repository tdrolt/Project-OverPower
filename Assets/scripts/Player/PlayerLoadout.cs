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

    [SerializeField, Tooltip("Match tuning asset. Free Loadout decides whether the ultimate slot " +
             "starts with the prefab's starting ultimate (free-test mode) or empty, to be bought " +
             "through the shop (the real economy). Every other starting slot is unaffected.")]
    private GameplayConfig gameplayConfig;

    private WeaponFiring weaponFiring;
    private AbilityRunner abilityRunner;
    private PlayerHealth playerHealth;

    // The prefab's own starting weapon id, captured before anything can change it - a remote copy
    // falls back to this when a property is missing or unreadable.
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
        // Not a hard error like the three above: a missing config fails OPEN to the old always-
        // free starting kit (see Start()) rather than silently locking every player out of their
        // ultimate for the whole match, so a warning is enough.
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

        if (photonView.IsMine)
        {
            // Publish what the prefab starts with, so every other client - and anyone who joins
            // later - reads the same loadout rather than guessing from their own prefab copy.
            var props = new Hashtable();
            if (startingWeaponId != LoadoutProperties.Empty)
                props[LoadoutProperties.WeaponKey] = startingWeaponId;

            // Task 2.5a: with the real economy on (Free Loadout off), the ultimate slot starts
            // EMPTY and must be bought through the shop (GDD p.18) - every other slot still
            // starts at the prefab's free default. A late joiner reads whichever id this publish
            // ends up writing below, and a respawn never re-runs Start() (this component lives on
            // the same player object for the whole match), so a bought ultimate is never lost.
            foreach (AbilitySlot slot in AbilitySlots)
            {
                int startingId = StartingAbilityId(slot);
                ApplyAbility(slot, startingId);
                props[LoadoutProperties.KeyFor(slot)] = EquippedAbilityId(slot);
            }

            // PlayerHealth already starts at level 0/0 in its own Awake - published explicitly so
            // the room's Custom Properties are self-describing rather than relying on a missing
            // key silently meaning the same thing.
            props[LoadoutProperties.ArmorAbsorbLevelKey] = playerHealth != null ? playerHealth.AbsorbLevel : 0;
            props[LoadoutProperties.ArmorRechargeLevelKey] = playerHealth != null ? playerHealth.RechargeLevel : 0;

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

    /// <summary>The starting kit's own id for one ability slot - Start (above) publishes this at spawn, and
    /// ResetForMatchStart (2.7b step 4) puts it back at the fresh start, so there is exactly ONE definition of
    /// "what the starting kit looks like" for both callers to share. Task 2.5a: the ultimate is the one
    /// exception - it starts EMPTY under the real economy (Free Loadout off) and only carries the prefab's own
    /// assigned starting ultimate (if any) under Free Loadout, a testing convenience; every other slot always
    /// starts at the prefab's default. [2.7b step 5b will read ShopPricing.IsFreeNow here instead of
    /// gameplayConfig.FreeLoadout directly, so the warm-up sandbox gets the same convenience - not built yet.]</summary>
    private int StartingAbilityId(AbilitySlot slot)
    {
        bool ultimateStartsEmpty = gameplayConfig != null && !gameplayConfig.FreeLoadout;
        if (ultimateStartsEmpty && slot == AbilitySlot.Ultimate)
            return LoadoutProperties.Empty;

        return abilityRunner != null ? abilityRunner.StartingId(slot) : LoadoutProperties.Empty;
    }

    /// <summary>2.7b Decision 6 (Tudor answer 2, amended): the fresh start at match-live puts EVERY slot back
    /// to the starter kit - the weapon, AND all three ability slots (Mobility, Equipment, Ultimate) to
    /// StartingAbilityId, not weapon+armour only as the pre-amendment plan text said. The prefab ships every
    /// ability slot empty, so this is "back to empty" for Mobility and Equipment too: the first pick into
    /// either is free again, exactly like a brand new player (ShopRules.AbilityPrice). Armour drops to level
    /// 0/0 here too - PlayerLifecycle.ResetForMatchStart calls this BEFORE PlayerHealth.ResetForRespawn, so the
    /// capacity is already at 0 when that refill decides what "full" means. One Hashtable publish, the same
    /// apply-then-publish shape as every other write in this class. Owner only.</summary>
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

    /// <summary>Owner only. Applies new armor upgrade levels on this machine, then tells everyone -
    /// same apply-locally-then-publish pattern as SetWeapon/SetAbility. Called by the F1 panel's
    /// upgrade buttons today (a future shop calls it once gold is spent), never with a decision of
    /// its own: the caller already ran the levels through Combat.ArmorUpgradePath and is only
    /// asking to make the result official. Required for correctness, not only for late joiners - a
    /// remote copy clamps synced armor to its OWN capacity (ArmorState.SetFromNetwork), which stays
    /// at level 0 until this replicates.</summary>
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

        // Both armor keys are always published together (SetArmorLevels writes them in one
        // Hashtable), so either one present means both are - but each still falls back to
        // playerHealth's OWN current level rather than 0, so an update carrying only, say, a
        // property refresh for another key can never silently reset the other path.
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

    /// <summary>One line per applied change, on every client, so a two-client test can compare what
    /// each machine believes a player is holding.</summary>
    private void LogLoadout()
    {
        int weaponId = weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : LoadoutProperties.Empty;
        Debug.Log($"[LOADOUT] actor={photonView.OwnerActorNr} W={weaponId} " +
                  $"E={EquippedAbilityId(AbilitySlot.Equipment)} U={EquippedAbilityId(AbilitySlot.Ultimate)} " +
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
