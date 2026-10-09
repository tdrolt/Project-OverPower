using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every weapon in the game; the only sanctioned way to turn a weapon id into a WeaponDefinition.
    /// Lookup is by each weapon's hand-assigned id, never by list index: only a bare int crosses the network and
    /// each client resolves it against its own copy, so if list order mattered, a harmless-looking reorder in the
    /// Inspector would silently re-map every player's weapon mid-match (and reproduce only for a stale build).
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Weapon Catalogue")]
    public sealed class WeaponCatalogue : ScriptableObject
    {
        [Header("Contents")]
        [Tooltip("Every weapon asset in the game. The order of this list does not matter and is " +
                 "safe to rearrange - weapons are found by their Id, never by their position " +
                 "here. What does matter is that every weapon has an Id and no two share one; " +
                 "the Console tells you off by name if they do.")]
        [SerializeField] private List<WeaponDefinition> weapons = new List<WeaponDefinition>();

        public IReadOnlyList<WeaponDefinition> Weapons => weapons;

        private Dictionary<int, WeaponDefinition> byId;

        private void OnEnable()
        {
            BuildLookup();
        }

        /// <summary>
        /// Null when no weapon claims the id, deliberately not a throw: ids arrive over the network
        /// and can be stale, malformed or hostile. Callers check for null.
        /// </summary>
        public WeaponDefinition Resolve(int id)
        {
            // OnEnable covers asset load and domain reload; this covers a CreateInstance whose list is
            // filled after OnEnable already ran.
            if (byId == null)
                BuildLookup();

            return byId.TryGetValue(id, out var definition) ? definition : null;
        }

        /// <summary>
        /// One human-readable line per problem that would make ids resolve wrongly; empty means sound.
        /// Returns messages rather than logging so a test can use it and OnValidate picks the volume.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var seen = new Dictionary<int, WeaponDefinition>();

            for (int i = 0; i < weapons.Count; i++)
            {
                var weapon = weapons[i];

                if (weapon == null)
                {
                    problems.Add($"Weapon Catalogue slot {i} is empty. An empty slot is skipped " +
                                 "entirely, so whatever weapon belonged there is missing from the " +
                                 "game. Drop the weapon asset in, or remove the slot.");
                    continue;
                }

                if (seen.TryGetValue(weapon.Id, out var existing))
                {
                    // Resolve keeps the first match, so the later weapon is unreachable - silently,
                    // which is why it must be reported.
                    problems.Add($"Weapon Catalogue: '{weapon.name}' and '{existing.name}' both " +
                                 $"use Id {weapon.Id}. Ids must be unique - right now only " +
                                 $"'{existing.name}' can ever be found, and '{weapon.name}' is " +
                                 "unreachable. Give one of them a different Id.");
                    continue;
                }

                seen.Add(weapon.Id, weapon);
            }

            return problems;
        }

        private void BuildLookup()
        {
            if (byId == null)
                byId = new Dictionary<int, WeaponDefinition>(weapons.Count);
            else
                byId.Clear();

            foreach (var weapon in weapons)
            {
                // Empty slots are normal mid-edit; a null here would throw inside OnEnable.
                // Validate reports them.
                if (weapon == null)
                    continue;

                // First id wins: overwriting would make the result depend on list order.
                if (!byId.ContainsKey(weapon.Id))
                    byId.Add(weapon.Id, weapon);
            }
        }

#if UNITY_EDITOR
        // OnEnable does not fire after an Inspector edit, so without the rebuild the lookup serves stale
        // entries for the rest of the session.
        private void OnValidate()
        {
            BuildLookup();

            foreach (var problem in Validate())
                Debug.LogError(problem, this);
        }
#endif
    }
}
