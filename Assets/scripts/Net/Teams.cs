using Photon.Pun;

namespace Overpower.Net
{
    /// <summary>
    /// The one place that knows how team membership is stored and compared. Team lives in
    /// exactly one Photon Custom Property key, "teamID" - moved here (out of Multiplayer.cs)
    /// because later abilities need the same same-team check that damage does, and a key this
    /// important should have exactly one home rather than one per script that needs it.
    /// </summary>
    public static class Teams
    {
        public const string TeamKey = "teamID";

        /// <summary>Set to true on a player who watches instead of playing (a spectator seat at Start, lobby Task 4); cleared on
        /// joining a room and on a deliberate leave (MatchPropertyReset).</summary>
        public const string SpectatorKey = "spec";

        /// <summary>True for a player on a spectator seat (spec = true): they have no body and no team.</summary>
        public static bool IsSpectator(Photon.Realtime.Player player) =>
            player != null && player.CustomProperties.TryGetValue(SpectatorKey, out object raw) && raw is bool spectating && spectating;

        /// <summary>True when this player is a player of a team: has a team and is not a seat spectator. Every loop that counts the room's
        /// members for a team (counts, scoreboard, minimap, zones, spectate targets) asks this, so a spectator (no body, no team) and a
        /// seatless arrival are never counted.</summary>
        public static bool PlaysForTeam(bool spectator, bool teamKnown) => teamKnown && !spectator;

        /// <summary>The team of a player who plays for one: false for a seat spectator and for a player with no team yet.</summary>
        public static bool TryGetPlayingTeam(Photon.Realtime.Player player, out int teamId)
        {
            if (TryGetTeam(player, out teamId) && PlaysForTeam(IsSpectator(player), true))
                return true;
            teamId = -1;
            return false;
        }

        public static bool TryGetTeam(Photon.Realtime.Player player, out int teamId)
        {
            teamId = -1;
            if (player != null && player.CustomProperties.TryGetValue(TeamKey, out object raw) && raw is int value)
            {
                teamId = value;
                return true;
            }
            return false;
        }

        /// Resolves through the current room, because a DamageInfo carries an actor number
        /// rather than a Player reference - the more robust choice of the two, since a Player
        /// reference cannot cross the wire.
        public static bool TryGetTeam(int actorNumber, out int teamId)
        {
            return TryGetTeam(PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber), out teamId);
        }

        /// True only when both players are known and share a teamID. Deliberately fails OPEN:
        /// if either team is unknown, damage still applies, because an unknown state silently
        /// making someone invulnerable is far worse to debug than one stray friendly-fire hit.
        public static bool AreSameTeam(Photon.Realtime.Player a, Photon.Realtime.Player b)
        {
            if (a == null || b == null)
                return false;
            if (a == b)
                return true;

            return TryGetTeam(a, out int teamA) && TryGetTeam(b, out int teamB) && teamA == teamB;
        }
    }
}
