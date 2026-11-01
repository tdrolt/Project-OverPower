using Photon.Realtime;

namespace Overpower.Net
{
    /// <summary>
    /// The only place the game asks Player.IsInactive (a test keeps it so). A bot is inactive to PUN, which is what makes the
    /// host its controller, but it never left: reading IsInactive directly would count every bot as dead, absent or gone.
    /// </summary>
    public static class Presence
    {
        /// <summary>A real player who dropped and may still rejoin; never a bot.</summary>
        public static bool IsDropped(Player player) => player != null && player.IsInactive && !(player is BotPlayer);
    }
}
