using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>The families of game mode, so the create screen can group sizes under one name.</summary>
    public enum GameModeFamily { Conquest, Dominion }

    /// <summary>
    /// Everything the lobby needs to know about one game mode: its name, how many team and spectator seats it has,
    /// which scene it plays on and the info cards players can read. It exists as one asset per game mode so a new
    /// mode is new data, not new lobby code. Fields are [SerializeField] private with read-only properties, like the
    /// rest of the Data folder.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Game Mode", fileName = "GameMode")]
    public sealed class GameModeDefinition : ScriptableObject
    {
        /// <summary>One card on the mode's info page: a title, a short text and the colour of its edge.</summary>
        [Serializable]
        public struct InfoCard
        {
            public string title;
            [TextArea] public string text;
            public Color accent;
        }

        [Header("Identity")]
        [Tooltip("The number that names this mode inside a room. It never changes and is never reused, so old rooms and logs keep meaning the same mode.")]
        [SerializeField] private int id;

        [Tooltip("The full name shown in lobby lists and the lobby, for example Conquest 3v3v3.")]
        [SerializeField] private string displayName = "";

        [Tooltip("Which family of modes this belongs to. The create screen groups the sizes of one family together.")]
        [SerializeField] private GameModeFamily family = GameModeFamily.Conquest;

        [Tooltip("The family name shown on the create screen, for example Conquest.")]
        [SerializeField] private string familyName = "";

        [Tooltip("The size name shown on the create screen, for example 3v3v3.")]
        [SerializeField] private string sizeName = "";

        [Header("Seats")]
        [Tooltip("The team numbers that play in this mode, left to right. A mode with two teams lists two numbers and the third corner of the map stays closed.")]
        [SerializeField] private int[] teams = { 0, 1, 2 };

        [Tooltip("How many players each team can seat. A higher number lets bigger teams play.")]
        [SerializeField, Min(1)] private int seatsPerTeam = 3;

        [Tooltip("How many spectators can watch the lobby and the match. Zero means nobody can watch without a team seat.")]
        [SerializeField, Min(0)] private int spectatorSeats = 3;

        [Header("Play")]
        [Tooltip("The name of the scene this mode plays on. Empty means the map is not built yet.")]
        [SerializeField] private string sceneName = "";

        [Tooltip("When off, the mode is shown greyed out as coming soon and nobody can create it.")]
        [SerializeField] private bool available = true;

        [Tooltip("The number of teams the room says it has: 3 for three teams, 2 for two. It is how rooms already tell the two lobby modes apart.")]
        [SerializeField] private int lobbyModeValue = 3;

        [Header("Info")]
        [Tooltip("The cards on the mode's info page, shown in this order. Each has a title, a short text and an accent colour.")]
        [SerializeField] private List<InfoCard> infoCards = new List<InfoCard>();

        public int Id => id;
        public string DisplayName => displayName;
        public GameModeFamily Family => family;
        public string FamilyName => familyName;
        public string SizeName => sizeName;
        public int[] Teams => teams;
        public int SeatsPerTeam => seatsPerTeam;
        public int SpectatorSeats => spectatorSeats;
        public string SceneName => sceneName;
        public bool Available => available;
        public int LobbyModeValue => lobbyModeValue;
        public IReadOnlyList<InfoCard> InfoCards => infoCards;
    }
}
