using UnityEngine;

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
    }
}
