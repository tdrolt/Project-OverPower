using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;

namespace Overpower.Data
{
    /// <summary>
    /// The four slots a player fills, and the key each one is bound to. The slot an ability
    /// occupies is fixed data rather than something chosen at runtime, so the loadout UI and the
    /// shop can both filter by it without either of them owning the mapping.
    /// </summary>
    public enum AbilitySlot
    {
        /// <summary>Left mouse button.</summary>
        Primary,

        /// <summary>Right mouse button.</summary>
        Equipment,

        /// <summary>Space.</summary>
        Ultimate,

        /// <summary>Left Shift.</summary>
        Mobility
    }

    /// <summary>
    /// A catalogue entry for one ability - deliberately thin, and deliberately not a stat block
    /// like WeaponDefinition.
    ///
    /// Weapons all share one set of numbers, which is why a single stat block drives all thirteen
    /// of them. Abilities do not: a dash, a mine and a healing field have almost nothing numeric
    /// in common, so a shared stat block would be mostly empty fields that mean nothing for most
    /// abilities - and empty fields that sometimes matter are worse for a designer than no fields
    /// at all. Each ability's numbers therefore live on its own module prefab, where they are the
    /// only numbers in sight. This asset carries only what the shop and the network need in order
    /// to talk about the ability without loading it.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Ability")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("The number this ability is known by across the network. It must be unique within " +
                 "the Ability Catalogue, and it must never change once a build is out, because " +
                 "clients look abilities up by this number - never by position in the catalogue " +
                 "list. A new asset starts at 0, which means you have not set it yet.")]
        [SerializeField] private int id;
        public int Id => id;

        [Tooltip("The ability's name as players read it in the shop and on the HUD.")]
        [SerializeField] private string displayName = "";
        public string DisplayName => displayName;

        [Tooltip("One line shown when a player hovers this in the loadout screen. Say what it " +
                 "does, not its numbers - numbers are shown next to it automatically.")]
        [SerializeField, TextArea(1, 3)] private string description = "";
        public string Description => description;

        [Tooltip("Which of the four slots this ability occupies, and therefore which key fires it: " +
                 "Primary is left mouse, Equipment is right mouse, Ultimate is Space, Mobility is " +
                 "Left Shift. A player can carry one ability per slot.")]
        [SerializeField] private AbilitySlot slot = AbilitySlot.Primary;
        public AbilitySlot Slot => slot;

        [Tooltip("The image shown for this ability in the shop, the loadout screen and the HUD.")]
        [SerializeField] private Sprite icon;
        public Sprite Icon => icon;

        [Tooltip("Gold a player spends to buy this ability.")]
        [SerializeField] private int goldCost;
        public int GoldCost => goldCost;

        [Header("Behaviour")]
        [Tooltip("The prefab that actually does the ability, and that holds all of its numbers - " +
                 "cooldowns, damage, radius, duration. To retune this ability, open this prefab; " +
                 "nothing numeric about it lives on this asset.")]
        [SerializeField] private GameObject modulePrefab;
        public GameObject ModulePrefab => modulePrefab;

        /// <summary>
        /// Every reason this ability could not work in a match, one readable line each; empty means
        /// sound. Returned rather than logged, like AbilityCatalogue.Validate, so a test can call it.
        ///
        /// Each check is a trap that fails silently in play rather than loudly here:
        ///  - no AbilityModule: the runner has nothing to equip, and the key just does nothing;
        ///  - a PhotonView: every client creates its own copy of the module locally, so a view on it
        ///    would claim a network id nobody allocated;
        ///  - an IPunObservable: the player's PhotonView searches its children for observables, and a
        ///    second one would quietly add bytes to every network update (PlayerNetSync is the only one);
        ///  - a Collider: modules sit under the player, whose whole hierarchy moves to the DeadPlayer
        ///    layer on death - and a module's collider would block its own player's shots besides;
        ///  - slot Primary: left mouse is the weapon, and the ability runner has no Primary slot.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (slot == AbilitySlot.Primary)
                problems.Add($"Ability '{name}': Slot is Primary (left mouse), which belongs to the weapon. " +
                             "Choose Equipment, Ultimate or Mobility.");

            if (modulePrefab == null)
            {
                problems.Add($"Ability '{name}': Module Prefab is empty, so this ability does nothing when " +
                             "cast. Assign the prefab that holds its AbilityModule.");
                return problems;
            }

            if (modulePrefab.GetComponent<AbilityModule>() == null)
                problems.Add($"Ability '{name}': Module Prefab '{modulePrefab.name}' has no AbilityModule " +
                             "component on its top object, so there is nothing to cast.");
            if (modulePrefab.GetComponentInChildren<PhotonView>(true) != null)
                problems.Add($"Ability '{name}': Module Prefab '{modulePrefab.name}' contains a PhotonView. " +
                             "Ability modules are created locally on every client and must not have one - " +
                             "send later moments of a cast with SendPhase instead.");
            if (modulePrefab.GetComponentInChildren<IPunObservable>(true) != null)
                problems.Add($"Ability '{name}': Module Prefab '{modulePrefab.name}' contains an " +
                             "IPunObservable. PlayerNetSync must stay the player's only one - remove it.");
            if (modulePrefab.GetComponentInChildren<Collider>(true) != null)
                problems.Add($"Ability '{name}': Module Prefab '{modulePrefab.name}' contains a Collider. " +
                             "Modules live inside the player and must have none - spawn a separate object " +
                             "into the world for anything that needs to be hit or touched.");

            return problems;
        }

#if UNITY_EDITOR
        /// <summary>Surfaces the problems above the moment the asset is edited or loaded, naming the
        /// asset, instead of as a key that silently does nothing in a playtest.</summary>
        private void OnValidate()
        {
            foreach (string problem in Validate())
                Debug.LogError(problem, this);
        }
#endif
    }
}
