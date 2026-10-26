namespace Overpower.Lobby
{
    /// <summary>
    /// The shape of a lobby's seats: which teams exist, how many seats each has and how many spectator seats there
    /// are. The rules take this small plain value instead of a game mode asset so they can be tested with literal
    /// numbers and never depend on Tudor's tuning.
    /// </summary>
    public readonly struct SeatLayout
    {
        public readonly int[] Teams;
        public readonly int SeatsPerTeam;
        public readonly int SpectatorSeats;

        public SeatLayout(int[] teams, int seatsPerTeam, int spectatorSeats)
        {
            Teams = teams ?? new int[0];
            SeatsPerTeam = seatsPerTeam;
            SpectatorSeats = spectatorSeats;
        }
    }
}
