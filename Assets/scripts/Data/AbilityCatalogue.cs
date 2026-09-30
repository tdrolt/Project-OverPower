using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every ability in the game, and the only sanctioned way to turn an ability id into an
    /// AbilityDefinition.
    ///
    /// Lookup goes through a dictionary keyed by each ability's hand-assigned id, and never through
    /// the list index - same correctness requirement as WeaponCatalogue, and for the same reason.
    /// What crosses the network is a bare int that every client resolves against its own local
    /// copy of this catalogue, so if resolution depended on list order, reordering this list in the
    /// Inspector would silently re-map every player's abilities mid-match.
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
        /// Turns an ability id into its definition, or null if no ability claims that id. Returning
        /// null rather than throwing is deliberate: ids arrive from other clients over the network,
        /// where they can be stale, malformed or hostile, and a bad packet from someone else must
        /// never be able to throw an exception on this machine. Callers check for null.
        /// </summary>
        public AbilityDefinition Resolve(int id)
        {
            // Normally built in OnEnable, which covers asset load and every domain reload. This
            // guard covers an instance created at runtime with CreateInstance, whose list is
            // populated after OnEnable has already run.
            if (byId == null)
                BuildLookup();

            return byId.TryGetValue(id, out var definition) ? definition : null;
        }

        /// <summary>
        /// The abilities that fill one slot, which is what both the loadout screen and the shop
        /// need in order to show a player their options for left mouse, right mouse, Space or
        /// Left Shift. Returns a fresh list, so a caller sorting or filtering it for display
        /// cannot disturb the catalogue.
        /// </summary>
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
        /// Reports every problem that would make this catalogue resolve ids wrongly, as one
        /// human-readable line per problem. An empty list means the catalogue is sound.
        ///
        /// Returning the messages rather than logging them keeps this usable from a test, and lets
        /// OnValidate decide how loudly to complain.
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
                    // Resolve keeps the first match, so the later ability is simply unreachable -
                    // silently, which is why this has to be reported rather than left to resolve
                    // itself quietly.
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
                // Empty slots are normal while a designer is mid-edit, and a null here would throw
                // inside OnEnable, which is a miserable place to debug from. Validate reports them.
                if (ability == null)
                    continue;

                // First id wins. Overwriting instead would make which ability you get depend on
                // list order, which is the exact thing this class exists to prevent.
                if (!byId.ContainsKey(ability.Id))
                    byId.Add(ability.Id, ability);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Rebuilds the lookup and surfaces any duplicate ids the moment the designer edits the
        /// list, so a clash shows up in the Editor rather than as a player holding the wrong
        /// ability in a playtest. The rebuild matters on its own: OnEnable does not fire again
        /// after an Inspector edit, so without this the lookup would serve stale entries for the
        /// rest of the session.
        /// </summary>
        private void OnValidate()
        {
            BuildLookup();

            foreach (var problem in Validate())
                Debug.LogError(problem, this);
        }
#endif
    }
}
