using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The ordered list of every game mode the lobby knows. The create screen shows them in this order, and a room
    /// stores only a mode's id, so adding a mode is adding an asset to this list.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Game Mode Catalogue", fileName = "GameModeCatalogue")]
    public sealed class GameModeCatalogue : ScriptableObject
    {
        [Tooltip("Every game mode, in the order the create screen shows them. A mode missing from this list cannot be picked.")]
        [SerializeField] private List<GameModeDefinition> modes = new List<GameModeDefinition>();

        public IReadOnlyList<GameModeDefinition> Modes => modes;

        /// <summary>The mode with this id, or null when no mode has it (an old room, a corrupt value).</summary>
        public GameModeDefinition ById(int id)
        {
            for (int i = 0; i < modes.Count; i++)
                if (modes[i] != null && modes[i].Id == id) return modes[i];
            return null;
        }
    }
}
