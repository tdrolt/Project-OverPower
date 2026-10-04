using Overpower.Data;

namespace Overpower.Lobby
{
    /// <summary>The one place a game mode asset becomes the plain SeatLayout the lobby rules work on.</summary>
    public static class SeatLayoutFactory
    {
        /// <summary>The Teams array is copied, so nothing holding a layout can ever change the asset.</summary>
        public static SeatLayout From(GameModeDefinition mode) =>
            new SeatLayout((int[])mode.Teams.Clone(), mode.SeatsPerTeam, mode.SpectatorSeats);
    }
}
