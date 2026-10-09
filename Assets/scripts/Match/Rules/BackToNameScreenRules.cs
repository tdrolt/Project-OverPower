using System.Collections.Generic;
using Photon.Realtime;

namespace Overpower.Match
{
    /// <summary>What the result panel's button does: close the game, or lead back to the lobby list.</summary>
    public enum ResultButtonAction { CloseGame, BackToLobbyList }

    /// <summary>What the return-to-the-lobby-list sequence does next while it waits for the connection.</summary>
    public enum ReturnStep { Wait, ReloadScene, Reconnect }

    /// <summary>After a match (D22) the result screen's button starts a fresh game on the name screen instead of
    /// closing the game. The pieces that are pure: which action a panel button takes, when the leave has finished far enough to
    /// rebuild the scene, and which of two own bodies a rejoining player keeps.</summary>
    public static class BackToNameScreenRules
    {
        /// <summary>The result panel's button leads back to the name screen only when the MATCH is really over (the room's phase). A
        /// knocked-out player sees their lose panel mid-match: their button stays a plain Quit, so they cannot re-join the running
        /// room as a fresh player on another team. The waiting panel's button is a Quit too.</summary>
        public static ResultButtonAction ButtonAction(MatchPhase phase) =>
            phase == MatchPhase.Over ? ResultButtonAction.BackToLobbyList : ResultButtonAction.CloseGame;

        /// <summary>The button's label follows what it will do (UiTheme texts).</summary>
        public static string ButtonLabel(ResultButtonAction action, string lobbyListLabel, string quitLabel) =>
            action == ResultButtonAction.BackToLobbyList ? lobbyListLabel : quitLabel;

        /// <summary>The wait for the leave timed out: unless the client is on the master server, disconnect before the scene is rebuilt so
        /// the new scene's Start connects from a clean state (it only connects when not connected).</summary>
        public static bool MustDisconnectBeforeReload(ClientState state) => state != ClientState.ConnectedToMasterServer;

        /// <summary>After LeaveRoom(false) the client walks back to the master server. Only there is it safe to rebuild the scene
        /// (a scene loaded mid-leave would run its Start against a half-left connection). A client that lost its connection
        /// meanwhile has to reconnect first; anything else is still on its way.</summary>
        public static ReturnStep NextStep(ClientState state)
        {
            if (state == ClientState.ConnectedToMasterServer)
                return ReturnStep.ReloadScene;
            if (state == ClientState.Disconnected || state == ClientState.PeerCreated)
                return ReturnStep.Reconnect;
            return ReturnStep.Wait;
        }

        /// <summary>A rejoining player can briefly own two bodies (an old cached one arriving after the fallback spawn). Which one is kept
        /// must be the same answer on every client and not depend on arrival order: the one THIS client spawned itself if it is still
        /// there, else the one the lookup points at, else the oldest (lowest view id). -1 = no bodies.</summary>
        public static int BodyToKeep(IReadOnlyList<int> ownViewIds, int spawnedByThisClient, int lookupPointsAt)
        {
            if (ownViewIds == null || ownViewIds.Count == 0)
                return -1;
            int oldest = ownViewIds[0];
            bool hasSpawned = false, hasLookup = false;
            for (int i = 0; i < ownViewIds.Count; i++)
            {
                int id = ownViewIds[i];
                if (id < oldest) oldest = id;
                if (id == spawnedByThisClient) hasSpawned = true;
                if (id == lookupPointsAt) hasLookup = true;
            }
            return hasSpawned ? spawnedByThisClient : hasLookup ? lookupPointsAt : oldest;
        }
    }
}
