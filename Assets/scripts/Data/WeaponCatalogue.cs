using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every weapon in the game, and the only sanctioned way to turn a weapon id into a
    /// WeaponDefinition.
    ///
    /// Lookup goes through a dictionary keyed by each weapon's hand-assigned id, and never through
    /// the list index. That is a correctness requirement, not a style preference. What actually
    /// crosses the network is a bare int, which every client resolves against its own local copy
    /// of this catalogue. If resolution depended on list order, then reordering this list in the
    /// Inspector - a harmless-looking tidy-up - would silently re-map every player's weapon in the
    /// middle of a match, and it would only ever reproduce for whoever had a stale build. This
    /// project already has a live bug of exactly that shape elsewhere, so it is a demonstrated
    /// failure mode rather than a hypothetical one.
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
        /// Turns a weapon id into its definition, or null if no weapon claims that id. Returning
        /// null rather than throwing is deliberate: ids arrive from other clients over the network,
        /// where they can be stale, malformed or hostile, and a bad packet from someone else must
        /// never be able to throw an exception on this machine. Callers check for null.
        /// </summary>
        public WeaponDefinition Resolve(int id)
        {
            // The lookup is normally built in OnEnable, which covers asset load and every domain
            // reload. This guard covers the remaining case: an instance created at runtime with
            // CreateInstance, whose list is populated after OnEnable has already run.
            if (byId == null)
                BuildLookup();

            return byId.TryGetValue(id, out var definition) ? definition : null;
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
                    // Resolve keeps the first match, so the later weapon is simply unreachable -
                    // silently, which is why this has to be reported rather than left to resolve
                    // itself quietly.
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
                // Empty slots are normal while a designer is mid-edit, and a null here would
                // throw inside OnEnable, which is a miserable place to debug from. Validate is
                // what reports them.
                if (weapon == null)
                    continue;

                // First id wins. Overwriting instead would make which weapon you get depend on
                // list order, which is the exact thing this class exists to prevent.
                if (!byId.ContainsKey(weapon.Id))
                    byId.Add(weapon.Id, weapon);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Rebuilds the lookup and surfaces any duplicate ids the moment the designer edits the
        /// list, so a clash shows up in the Editor rather than as a player holding the wrong
        /// weapon in a playtest. The rebuild matters on its own: OnEnable does not fire again
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
