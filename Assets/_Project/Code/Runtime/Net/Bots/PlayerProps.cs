using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Net
{
    /// <summary>
    /// The one way to write a player's Custom Properties (a test keeps it so). A real player's go to the server as before. A
    /// bot's go to the room as b{actor}.{key}, written by the host, because the server has no such actor: a plain
    /// SetCustomProperties on a BotPlayer is sent for a non-existent actor and lost without a word. Readers do not change:
    /// BotRoster mirrors the room keys back into the BotPlayer and raises OnPlayerPropertiesUpdate, as the server would.
    /// </summary>
    public static class PlayerProps
    {
        public enum Route { None, Server, BotRoom }

        private static bool warnedNotMaster;

        public static Route RouteFor(Player player) =>
            player == null ? Route.None : player is BotPlayer ? Route.BotRoom : Route.Server;

        /// <summary>Writes props for player; expected, when given, is the compare-and-swap condition on the same keys.</summary>
        public static bool Set(Player player, Hashtable props, Hashtable expected = null)
        {
            switch (RouteFor(player))
            {
                case Route.Server:
                    return player.SetCustomProperties(props, expected);
                case Route.BotRoom:
                    if (!PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
                    {
                        // Only the host plays bots; another client writing one would race the host's own writes.
                        if (!warnedNotMaster)
                        {
                            warnedNotMaster = true;
                            Debug.LogWarning($"[BOTS] a non-host client tried to write {props?.Count ?? 0} propert(ies) of bot {player.ActorNumber}; ignored");
                        }
                        return false;
                    }
                    return PhotonNetwork.CurrentRoom.SetCustomProperties(
                        BotRosterRules.ToRoom(player.ActorNumber, props),
                        expected == null ? null : BotRosterRules.ToRoom(player.ActorNumber, expected));
                default:
                    return false;
            }
        }
    }
}
