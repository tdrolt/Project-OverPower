using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The small numbers and wordings behind the lobby screens: how long a lobby or player name may be and how often
    /// the lobby list redraws. It exists so Tudor can tune them in the Inspector without touching code.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Lobby Config", fileName = "LobbyConfig")]
    public sealed class LobbyConfig : ScriptableObject
    {
        /// <summary>The duplicate-name format used by the asset's default and by the lobby code when no LobbyConfig is
        /// assigned: {0} is the name, {1} the number.</summary>
        public const string DefaultDuplicateNameFormat = "{0} {1}";

        /// <summary>The numbers below as they ship. The lobby code falls back to these when no LobbyConfig is assigned, so a fallback is never a second copy of a number.</summary>
        public const int DefaultLobbyNameMaxLength = 24;
        public const int DefaultNameMinLength = 4;
        public const int DefaultNameMaxLength = 10;
        public const float DefaultListRedrawSeconds = 0.5f;

        [Header("Lobby names")]
        [Tooltip("The most characters a lobby name can have. A higher number allows longer names but they may not fit the list.")]
        [SerializeField, Min(1)] private int lobbyNameMaxLength = DefaultLobbyNameMaxLength;

        [Header("Player names")]
        [Tooltip("The fewest letters and digits a player name must have before Join works.")]
        [SerializeField, Min(1)] private int nameMinLength = DefaultNameMinLength;

        [Tooltip("The most letters and digits a player name can have. A higher number allows longer names but they may not fit above a player's head.")]
        [SerializeField, Min(1)] private int nameMaxLength = DefaultNameMaxLength;

        [Tooltip("How a second player with the same name is shown: {0} is the name and {1} the number, so the default makes Tudor 2.")]
        [SerializeField] private string duplicateNameFormat = DefaultDuplicateNameFormat;

        [Header("Lobby list")]
        [Tooltip("How many seconds pass between redraws of the lobby list. A shorter time shows changes sooner but redraws more often.")]
        [SerializeField, Min(0.05f)] private float listRedrawSeconds = DefaultListRedrawSeconds;

        public int LobbyNameMaxLength => lobbyNameMaxLength;
        public int NameMinLength => nameMinLength;
        public int NameMaxLength => nameMaxLength;
        public string DuplicateNameFormat => duplicateNameFormat;
        public float ListRedrawSeconds => listRedrawSeconds;
    }
}
