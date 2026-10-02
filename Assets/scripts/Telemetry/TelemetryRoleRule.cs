namespace Overpower.Telemetry
{
    /// <summary>Lobby Task 6: who writes a match log. The log's first line makes the player a row of the report, so a spectator (no body, no
    /// team) writes none, and nobody writes one while still in the lobby with no role: the file opens when the player is on a team.</summary>
    public static class TelemetryRoleRule
    {
        public static bool MayOpenFile(bool spectator, bool onATeam) => onATeam && !spectator;
    }
}
