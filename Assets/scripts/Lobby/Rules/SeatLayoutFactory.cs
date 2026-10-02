using Overpower.Data;

namespace Overpower.Lobby
{
    /// <summary>The one place a game mode asset becomes the plain SeatLayout the lobby rules work on.</summary>
    public static class SeatLayoutFactory
    {
        public static SeatLayout From(GameModeDefinition mode) =>
            new SeatLayout(mode.Teams, mode.SeatsPerTeam, mode.SpectatorSeats);
    }
}
