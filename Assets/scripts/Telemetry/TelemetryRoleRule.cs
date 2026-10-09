namespace Overpower.Telemetry
{
    /// <summary>Who writes a match log, and when. Nobody writes one in the lobby before the game starts
    /// (stage 0), and nobody without a role. A player on a team writes one (it makes them a row of the report). A spectator writes none -
    /// unless they are the host: the master-only lines (match identity, go-live phases, eliminations, ownership, capture, bounty, under
    /// attack, markers) are the report's territory timeline and are logged only by the master, so a spectator host's file carries them
    /// (its session line is marked as a spectator so the report counts no player row for it).</summary>
    public static class TelemetryRoleRule
    {
        /// <summary>Whether a change of a player's properties should make the local client try to open its file again. The local player's team
        /// (a player's file opens on their team) or spectator flag (a spectator HOST's file opens once the flag has arrived: it comes after the
        /// stage edge) changing both qualify.</summary>
        public static bool RetriesOpenOnChange(bool isLocal, bool teamChanged, bool spectatorChanged) =>
            isLocal && (teamChanged || spectatorChanged);

        public static bool MayOpenFile(bool spectator, bool onATeam, bool isMaster, int lobbyStage)
        {
            if (lobbyStage < 1) return false;
            return spectator ? isMaster : onATeam;
        }
    }
}
