namespace Overpower.Lobby
{
    /// <summary>The Room Property keys a lobby puts on its Photon room, in one place (like TelemetryKeys), so the list, the
    /// create call and the seat code never retype a key. The short names keep the lobby-visible properties small.</summary>
    public static class LobbyKeys
    {
        /// <summary>The lobby's display name, as typed on the create screen.</summary>
        public const string Name = "lN";
        /// <summary>The game mode's id (GameModeDefinition.Id).</summary>
        public const string Mode = "lM";
        /// <summary>The stage: 0 lobby, 1 warm-up, 2 in match (LobbyStage).</summary>
        public const string Stage = "lS";
        /// <summary>The host's nickname (the master client).</summary>
        public const string Host = "lH";
        /// <summary>The seat fill counts, written by the seat code (lobby Task 3).</summary>
        public const string Fill = "lF";

        /// <summary>The properties the lobby list can see without joining the room.</summary>
        public static readonly string[] ForLobby = { Name, Mode, Stage, Host, Fill };
    }
}
