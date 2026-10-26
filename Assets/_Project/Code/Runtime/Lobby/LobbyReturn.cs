namespace Overpower.Lobby
{
    /// <summary>Survives the scene rebuild after a match (or a spectator's Leave). RoomManager.ReturnToLobbyList sets it before
    /// the rebuild; the rebuilt name screen consumes it once and goes straight to the lobby list instead of asking for the name again.</summary>
    public static class LobbyReturn
    {
        public static bool OpenListOnLoad;

        /// <summary>True once after ReturnToLobbyList asked for the list.</summary>
        public static bool Consume()
        {
            bool wanted = OpenListOnLoad;
            OpenListOnLoad = false;
            return wanted;
        }
    }
}
