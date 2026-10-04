using System.Collections.Generic;

namespace Overpower.Lobby
{
    /// <summary>
    /// Which scene a lobby plays on (Dominion Task 10). Every lobby is made and filled in Game Scene (the name screen, the list, the seats and
    /// the chat live there); a game mode that plays on another map names that scene (GameModeDefinition.SceneName). When the host presses Start
    /// game the master loads that scene for everyone, and from then on nobody spawns a body or reacts to the start until the active scene is the
    /// room's. These are the pure decisions; LobbyStart and RoomManager ask them.
    /// </summary>
    public static class SceneLoadRules
    {
        /// <summary>The scene the lobby list, the name screen and every lobby live in. Back to the lobby list always loads this one.</summary>
        public const string LobbyListScene = "Game Scene";

        /// <summary>The scene to load for everyone when the game starts: the mode's scene when it names one and it is not the active scene,
        /// else null (stay where we are). A mode with no scene name plays on whatever scene the lobby is in.</summary>
        public static string SceneToLoadOnStart(string modeScene, string activeScene)
        {
            if (string.IsNullOrEmpty(modeScene)) return null;
            return modeScene == activeScene ? null : modeScene;
        }

        /// <summary>True when the active scene is the one the room's game is played on (nothing to load): bodies may spawn and the start may be
        /// acted on. False while the room's scene is still to be loaded.</summary>
        public static bool RoomIsHere(string modeScene, string activeScene) => SceneToLoadOnStart(modeScene, activeScene) == null;

        /// <summary>The scene Back to the lobby list loads: always the lobby scene, whatever scene the match was played on (the active scene
        /// is no longer used for this).</summary>
        public static string SceneForLobbyList(string activeScene) => LobbyListScene;

        /// <summary>Whether a starting RoomManager sets Photon's AuthenticationValues: only when the client is not connected yet. A live connection's values
        /// hold the token it needs to return to the master server after leaving a room, so a scene loaded inside a room must not replace them.</summary>
        public static bool MustSetAuthValues(bool alreadyConnected) => !alreadyConnected;

        /// <summary>Which of the already-created components get the room-joined notice again when a scene arrives in a room that is already
        /// running (a scene load for everyone, or a joiner whose scene was synced). The room-joined callbacks of these only READ the room, so
        /// repeating them in the new scene is safe. BuildingManager and ZonePresenceTracker are left out: their Start already reads a room that is there. The others (RoomManager clears the team of a new player, the lobby list clears itself) must
        /// not be repeated.</summary>
        private static readonly string[] ResumedAfterSceneLoad =
        {
            nameof(LobbySeats), nameof(LobbyStart), nameof(Overpower.Match.MatchDirector), nameof(Overpower.Telemetry.MatchTelemetry),
            nameof(Overpower.Dominion.DominionDirector), nameof(Overpower.UI.DominionHud), nameof(Overpower.Match.HealthPackManager),
            nameof(Overpower.Net.RejoinController),
        };

        /// <summary>Whether Photon's message queue runs while a scene that arrived inside a room is still reading the room. It does not: room updates wait
        /// until every component has read the room once, so none meets its blank "last seen" values.</summary>
        public static bool QueueRunsWhileResuming() => false;

        public static bool ResumesAfterSceneLoad(string componentTypeName) => componentTypeName != null && System.Array.IndexOf(ResumedAfterSceneLoad, componentTypeName) >= 0;
    }

    /// <summary>The map data a scene carries about capitals and spawns, as pure rules.</summary>
    public static class SceneMapRules
    {
        /// <summary>The default capital zones and the team each belongs to, the triangle arena's: zone 6 is team 0's, 7 team 1's, 8 team 2's.</summary>
        public static (int zone, int team)[] DefaultCapitals => new[] { (6, 0), (7, 1), (8, 2) };

        /// <summary>The lookup (zone id to team id) from a scene's list of capital zones. A zone listed twice keeps the last team; a negative zone
        /// or team is ignored. A null list is the default three.</summary>
        public static Dictionary<int, int> CapitalLookup(IEnumerable<(int zone, int team)> capitals)
        {
            var lookup = new Dictionary<int, int>();
            foreach ((int zone, int team) in capitals ?? DefaultCapitals)
            {
                if (zone < 0 || team < 0) continue;
                lookup[zone] = team;
            }
            return lookup;
        }

        /// <summary>The first team of the room's mode that has no spawn point (the list is shorter than its id, or its entry is empty), or -1 when
        /// every team has one. A two-team map needs two spawn points, not three: it checks the teams of the mode, not a fixed three.</summary>
        public static int FirstTeamWithoutSpawn(IReadOnlyList<bool> spawnPresentByTeam, IReadOnlyList<int> modeTeams)
        {
            if (modeTeams == null) return -1;
            foreach (int team in modeTeams)
            {
                if (spawnPresentByTeam == null || team < 0 || team >= spawnPresentByTeam.Count || !spawnPresentByTeam[team]) return team;
            }
            return -1;
        }
    }

    /// <summary>Which of a team's spawn points a player stands on, so teammates do not appear on the same spot.</summary>
    public static class SpawnSlotRules
    {
        /// <summary>The slot (0 .. slotCount-1) for a player: their place in the ascending order of the team's actor numbers, wrapped when the team is bigger than
        /// the list of points. A player not in the list (or an empty list) takes slot 0; no slots gives 0.</summary>
        public static int SlotFor(IReadOnlyList<int> teamActors, int myActor, int slotCount)
        {
            if (slotCount <= 0 || teamActors == null) return 0;
            int below = 0;
            bool found = false;
            foreach (int actor in teamActors)
            {
                if (actor == myActor) found = true;
                else if (actor < myActor) below++;
            }
            return found ? below % slotCount : 0;
        }
    }
}
