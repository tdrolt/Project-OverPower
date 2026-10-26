using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Task 10: which scene a lobby plays on, where Back to the lobby list goes, the capital zones a map carries and the spawn points a
    /// mode needs. Scene names are literals made up for the test, never the assets'. The components call these functions (LobbyStart, RoomManager,
    /// BuildingManager); the recorder rows in the report name the proof of each call site.
    /// </summary>
    public class SceneLoadRulesTests
    {
        // ---- load a scene on Start?

        [Test] public void AModeOnAnotherSceneLoadsItForEveryone()
        {
            Assert.AreEqual("Lane Map", SceneLoadRules.SceneToLoadOnStart("Lane Map", "Lobby Scene"));
        }

        [Test] public void AModeOnTheActiveSceneLoadsNothing()
        {
            Assert.IsNull(SceneLoadRules.SceneToLoadOnStart("Lobby Scene", "Lobby Scene"));
        }

        [Test] public void AModeWithNoSceneNameStaysWhereItIs()
        {
            Assert.IsNull(SceneLoadRules.SceneToLoadOnStart("", "Lobby Scene"));
            Assert.IsNull(SceneLoadRules.SceneToLoadOnStart(null, "Lobby Scene"));
        }

        [Test] public void SceneNamesAreComparedExactly()
        {
            Assert.AreEqual("lane map", SceneLoadRules.SceneToLoadOnStart("lane map", "Lane Map"), "a different spelling is a different scene");
        }

        [Test] public void TheRoomIsHereExactlyWhenThereIsNothingToLoad()
        {
            Assert.IsTrue(SceneLoadRules.RoomIsHere("Lane Map", "Lane Map"));
            Assert.IsTrue(SceneLoadRules.RoomIsHere("", "Lobby Scene"));
            Assert.IsFalse(SceneLoadRules.RoomIsHere("Lane Map", "Lobby Scene"), "the start must not be acted on, nor a body spawned, before the scene has loaded");
        }

        // ---- back to the lobby list

        [Test] public void BackToTheListAlwaysLoadsTheLobbyScene()
        {
            Assert.AreEqual("Game Scene", SceneLoadRules.SceneForLobbyList("Lane Map"));
            Assert.AreEqual("Game Scene", SceneLoadRules.SceneForLobbyList("Game Scene"));
            Assert.AreEqual("Game Scene", SceneLoadRules.SceneForLobbyList(""));
            Assert.AreEqual("Game Scene", SceneLoadRules.LobbyListScene);
        }

        // ---- the components that read the room again in an arriving scene

        [Test] public void OnlyTheReadOnlyRoomListenersAreRunAgainInANewScene()
        {
            Assert.IsTrue(SceneLoadRules.ResumesAfterSceneLoad("LobbyStart"));
            Assert.IsTrue(SceneLoadRules.ResumesAfterSceneLoad("LobbySeats"));
            Assert.IsTrue(SceneLoadRules.ResumesAfterSceneLoad("MatchDirector"));
            Assert.IsFalse(SceneLoadRules.ResumesAfterSceneLoad("RoomManager"), "its room-joined callback clears the team of a NEW player");
            Assert.IsFalse(SceneLoadRules.ResumesAfterSceneLoad("LobbyDirectory"));
            Assert.IsFalse(SceneLoadRules.ResumesAfterSceneLoad("NameScreen"), "it shows the lobby room screen itself when it starts inside a room");
            Assert.IsFalse(SceneLoadRules.ResumesAfterSceneLoad(null));
        }

        // ---- the connection's identity survives a scene loaded inside a room

        [Test] public void ARoomManagerStartingInsideALiveConnectionKeepsItsAuthenticationValues()
        {
            Assert.IsFalse(SceneLoadRules.MustSetAuthValues(alreadyConnected: true), "replacing them loses the token that brings the client back to the master server");
            Assert.IsTrue(SceneLoadRules.MustSetAuthValues(alreadyConnected: false), "a first connect needs the id");
        }

        // ---- capital zones per scene

        [Test] public void NoCapitalListMeansTheTriangleArenasThree()
        {
            var lookup = SceneMapRules.CapitalLookup(null);
            Assert.AreEqual(3, lookup.Count);
            Assert.AreEqual(0, lookup[6]);
            Assert.AreEqual(1, lookup[7]);
            Assert.AreEqual(2, lookup[8]);
        }

        [Test] public void ATwoTeamMapListsOnlyItsTwoCapitals()
        {
            var lookup = SceneMapRules.CapitalLookup(new[] { (0, 0), (9, 1) });
            Assert.AreEqual(2, lookup.Count);
            Assert.IsTrue(lookup.ContainsKey(9));
            Assert.IsFalse(lookup.ContainsKey(6));
        }

        [Test] public void AZoneListedTwiceKeepsTheLastTeamAndNegativesAreIgnored()
        {
            var lookup = SceneMapRules.CapitalLookup(new[] { (4, 0), (4, 1), (-1, 0), (5, -1) });
            Assert.AreEqual(1, lookup.Count);
            Assert.AreEqual(1, lookup[4]);
        }

        [Test] public void AnEmptyCapitalListIsAMapWithNoCapitals()
        {
            Assert.AreEqual(0, SceneMapRules.CapitalLookup(new (int, int)[0]).Count);
        }

        // ---- spawn points per mode

        [Test] public void AThreeTeamModeNeedsAllThreeSpawns()
        {
            Assert.AreEqual(-1, SceneMapRules.FirstTeamWithoutSpawn(new[] { true, true, true }, new[] { 0, 1, 2 }));
            Assert.AreEqual(2, SceneMapRules.FirstTeamWithoutSpawn(new[] { true, true, false }, new[] { 0, 1, 2 }));
            Assert.AreEqual(2, SceneMapRules.FirstTeamWithoutSpawn(new[] { true, true }, new[] { 0, 1, 2 }), "a list that is too short misses team 2");
        }

        [Test] public void ATwoTeamModeIsSatisfiedByTwoSpawns()
        {
            Assert.AreEqual(-1, SceneMapRules.FirstTeamWithoutSpawn(new[] { true, true }, new[] { 0, 1 }));
            Assert.AreEqual(-1, SceneMapRules.FirstTeamWithoutSpawn(new[] { true, true, false }, new[] { 0, 1 }), "a third, empty spawn is not needed");
        }

        [Test] public void ATeamIdOutsideTheListOrWithAnEmptyEntryIsReported()
        {
            Assert.AreEqual(1, SceneMapRules.FirstTeamWithoutSpawn(new[] { true, false }, new[] { 0, 1 }));
            Assert.AreEqual(5, SceneMapRules.FirstTeamWithoutSpawn(new[] { true }, new[] { 5 }));
            Assert.AreEqual(0, SceneMapRules.FirstTeamWithoutSpawn(null, new[] { 0 }));
            Assert.AreEqual(-1, SceneMapRules.FirstTeamWithoutSpawn(new[] { true }, null), "no teams to check");
        }
    }
}
