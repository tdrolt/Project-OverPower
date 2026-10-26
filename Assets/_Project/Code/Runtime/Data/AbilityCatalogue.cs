using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every ability in the game; the only sanctioned way to turn an ability id into an AbilityDefinition.
    /// Lookup is by each ability's hand-assigned id, never by list index (same as WeaponCatalogue): only a
    /// bare int crosses the network and each client resolves it against its own copy, so list order must
    /// never matter or reordering in the Inspector would re-map every player's abilities mid-match.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Ability Catalogue")]
    public sealed class AbilityCatalogue : ScriptableObject
    {
        [Header("Contents")]
        [Tooltip("Every ability asset in the game, across all four slots. The order of this list " +
                 "does not matter and is safe to rearrange - abilities are found by their Id, " +
                 "never by their position here. What does matter is that every ability has an Id " +
                 "and no two share one; the Console tells you off by name if they do.")]
        [SerializeField] private List<AbilityDefinition> abilities = new List<AbilityDefinition>();

        public IReadOnlyList<AbilityDefinition> Abilities => abilities;

        private Dictionary<int, AbilityDefinition> byId;

        private void OnEnable()
        {
            BuildLookup();
        }

        /// <summary>
        /// Null when no ability claims the id, deliberately not a throw: ids arrive over the network
        /// and can be stale, malformed or hostile. Callers check for null.
        /// </summary>
        public AbilityDefinition Resolve(int id)
        {
            // OnEnable covers asset load and domain reload; this covers a CreateInstance whose list is
            // filled after OnEnable already ran.
            if (byId == null)
                BuildLookup();

            return byId.TryGetValue(id, out var definition) ? definition : null;
        }

        /// <summary>A fresh list, so a caller sorting or filtering it cannot disturb the catalogue.</summary>
        public List<AbilityDefinition> ForSlot(AbilitySlot slot)
        {
            var matches = new List<AbilityDefinition>();

            foreach (var ability in abilities)
            {
                if (ability != null && ability.Slot == slot)
                    matches.Add(ability);
            }

            return matches;
        }

        /// <summary>
        /// One human-readable line per problem that would make ids resolve wrongly; empty means sound.
        /// Returns messages rather than logging so a test can use it and OnValidate picks the volume.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var seen = new Dictionary<int, AbilityDefinition>();

            for (int i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];

                if (ability == null)
                {
                    problems.Add($"Ability Catalogue slot {i} is empty. An empty slot is skipped " +
                                 "entirely, so whatever ability belonged there is missing from the " +
                                 "game. Drop the ability asset in, or remove the slot.");
                    continue;
                }

                if (seen.TryGetValue(ability.Id, out var existing))
                {
                    // Resolve keeps the first match, so the later ability is unreachable - silently,
                    // which is why it must be reported.
                    problems.Add($"Ability Catalogue: '{ability.name}' and '{existing.name}' both " +
                                 $"use Id {ability.Id}. Ids must be unique - right now only " +
                                 $"'{existing.name}' can ever be found, and '{ability.name}' is " +
                                 "unreachable. Give one of them a different Id.");
                    continue;
                }

                seen.Add(ability.Id, ability);
            }

            return problems;
        }

        private void BuildLookup()
        {
            if (byId == null)
                byId = new Dictionary<int, AbilityDefinition>(abilities.Count);
            else
                byId.Clear();

            foreach (var ability in abilities)
            {
                // Empty slots are normal mid-edit; a null here would throw inside OnEnable.
                // Validate reports them.
                if (ability == null)
                    continue;

                // First id wins: overwriting would make the result depend on list order.
                if (!byId.ContainsKey(ability.Id))
                    byId.Add(ability.Id, ability);
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
