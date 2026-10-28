using System.Collections.Generic;
using Photon.Realtime;

namespace Overpower.Match
{
    /// <summary>What an exit button does: close the game, lead back to the lobby list after a match, or leave a running match.</summary>
    public enum ResultButtonAction { CloseGame, BackToLobbyList, LeaveMatch }

    /// <summary>What the return-to-the-lobby-list sequence does next while it waits for the connection.</summary>
    public enum ReturnStep { Wait, ReloadScene, Reconnect }

    /// <summary>Every exit during a match leads back to the lobby list (RoomManager.ReturnToLobbyList, a real leave: the seat is
    /// freed and the match carries on for the others); closing the game is offered only outside a match, and never in the browser.
    /// Also pure: when the leave has finished far enough to rebuild the scene, and which of two own bodies a rejoining player keeps.</summary>
    public static class BackToNameScreenRules
    {
        /// <summary>The result, waiting and death-card button: back to the lobby list once the match is over (the room's phase),
        /// Leave match while it still runs (a knocked-out player's lose panel, the waiting panel).</summary>
        public static ResultButtonAction ButtonAction(MatchPhase phase) =>
            phase == MatchPhase.Over ? ResultButtonAction.BackToLobbyList : ResultButtonAction.LeaveMatch;

        public static bool LeadsToLobbyList(ResultButtonAction action) => action != ResultButtonAction.CloseGame;

        /// <summary>A match runs from the warm-up on (lobby stage 1 and later); a lobby before its start is not a match.</summary>
        public static bool IsMatchRunning(bool inRoom, int lobbyStage) => inRoom && lobbyStage >= 1;

        /// <summary>Escape's Yes during a match leaves it (back to the lobby list) instead of closing the game.</summary>
        public static bool EscapeLeavesTheMatch(bool matchRunning) => matchRunning;

        /// <summary>"Close the game?" only outside a match, and not in the browser: a web page cannot close itself.</summary>
        public static bool OffersQuitGame(bool matchRunning, bool webPlayer) => !matchRunning && !webPlayer;

        /// <summary>The button's label follows what it will do (UiTheme texts).</summary>
        public static string ButtonLabel(ResultButtonAction action, string lobbyListLabel, string leaveLabel) =>
            action == ResultButtonAction.BackToLobbyList ? lobbyListLabel : leaveLabel;

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
