namespace Overpower.Telemetry
{
    /// <summary>Lobby Task 6 (+ review fixes): who writes a match log, and when. Nobody writes one in the lobby before the game starts
    /// (stage 0), and nobody without a role. A player on a team writes one (it makes them a row of the report). A spectator writes none -
    /// unless they are the host: the master-only lines (match identity, go-live phases, eliminations, ownership, capture, bounty, under
    /// attack, markers) are the report's territory timeline and are logged only by the master, so a spectator host's file carries them
    /// (its session line is marked as a spectator so the report counts no player row for it).</summary>
    public static class TelemetryRoleRule
    {
        public static bool MayOpenFile(bool spectator, bool onATeam, bool isMaster, int lobbyStage)
        {
            if (lobbyStage < 1) return false;
            return spectator ? isMaster : onATeam;
        }
    }
}
