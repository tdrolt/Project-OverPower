using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Lobby;
using Overpower.Telemetry;
using Overpower.UI;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Task 12, Part 0: the review fixes of Task 10 (the room is read before room updates arrive in a new scene, no second session line, a stale
    /// "arrived dead" flag, one countdown mapping), the rectangular minimap frame and the two spawn points per team. Each wiring point is read from the
    /// method that must make the call (IlWiring), because a rule nothing calls guards nothing.
    /// </summary>
    public class DominionTask12Part0Tests
    {
        private static MethodInfo Method(System.Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static MethodInfo Getter(System.Type type, string property) => type.GetProperty(property, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetGetMethod(true);

        // ---------------------------------------------------------------- the room is read before its updates arrive

        [Test] public void TheMessageQueueWaitsWhileANewSceneReadsTheRoom()
        {
            Assert.IsFalse(SceneLoadRules.QueueRunsWhileResuming());
        }

        [Test] public void StartHoldsTheQueueAndTheResumeRoutineLetsItGo()
        {
            // PhotonNetwork lives in another assembly, so the calls are read through RoomManager's one method that sets the flag.
            MethodInfo setter = Method(typeof(RoomManager), "SetMessageQueueRunning");
            Assert.IsNotNull(setter);
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "Start", setter));
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "Start", Method(typeof(SceneLoadRules), nameof(SceneLoadRules.QueueRunsWhileResuming))), "the hold is the tested rule's answer");
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "ResumeInRoomAfterSceneLoad", setter), "and the routine sets it running again, whatever happens");
        }

        [Test] public void EveryListenerOfTheAllowlistIsAReadOnlyRoomListenerByItsRealTypeName()
        {
            foreach (System.Type type in new[]
            {
                typeof(LobbySeats), typeof(LobbyStart), typeof(Overpower.Match.MatchDirector), typeof(MatchTelemetry), typeof(DominionDirector), typeof(DominionHud),
                typeof(Overpower.Match.HealthPackManager), typeof(Overpower.Net.RejoinController),
            })
            {
                Assert.IsTrue(SceneLoadRules.ResumesAfterSceneLoad(type.Name), type.Name);
                Assert.IsTrue(typeof(MonoBehaviourPunCallbacks).IsAssignableFrom(type), type.Name + " has a room-joined callback to repeat");
            }
            Assert.IsFalse(SceneLoadRules.ResumesAfterSceneLoad(typeof(RoomManager).Name));
        }

        [Test] public void TheListIsBuiltFromRealTypeNames()
        {
            FieldInfo list = typeof(SceneLoadRules).GetField("ResumedAfterSceneLoad", BindingFlags.NonPublic | BindingFlags.Static);
            var names = (string[])list.GetValue(null);
            Assert.AreEqual(8, names.Length);
            foreach (string name in names)
                Assert.IsNotNull(typeof(RoomManager).Assembly.GetTypes().FirstOrDefault(t => t.Name == name && typeof(MonoBehaviourPunCallbacks).IsAssignableFrom(t)), name + " is not a room listener type");
        }

        // ---------------------------------------------------------------- the rules RoomManager asks (review item 4)

        [Test] public void RoomManagerAsksTheTestedRulesFromTheMethodsThatMatter()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "Start", Method(typeof(SceneLoadRules), nameof(SceneLoadRules.MustSetAuthValues))), "the auth-values guard");
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "ReloadWhenBackOnMaster", Method(typeof(SceneLoadRules), nameof(SceneLoadRules.SceneForLobbyList))), "back to the list");
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "SpawnPlayerOnTeam", Getter(typeof(RoomManager), nameof(RoomManager.RoomSceneIsLoaded))), "the spawn gate");
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "get_RoomSceneIsLoaded", Method(typeof(SceneLoadRules), nameof(SceneLoadRules.RoomIsHere))));
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "ValidateTeamResources", Method(typeof(SceneMapRules), nameof(SceneMapRules.FirstTeamWithoutSpawn))));
        }

        // ---------------------------------------------------------------- one session line, one join line per process

        [Test] public void AMatchAndPlayerAreWrittenOncePerRoomUntilTheRoomIsLeft()
        {
            var memory = new TelemetryWrittenMemory();
            Assert.IsTrue(memory.FirstTime("session", "room a", 3));
            Assert.IsFalse(memory.FirstTime("session", "room a", 3), "the new scene's telemetry must not write the header again");
            Assert.IsTrue(memory.FirstTime("join", "room a", 3), "another kind of line is its own");
            Assert.IsTrue(memory.FirstTime("session", "room a", 4), "another player");
            Assert.IsTrue(memory.FirstTime("session", "room b", 3), "another room");
            memory.ForgetAll();
            Assert.IsTrue(memory.FirstTime("session", "room a", 3), "after leaving, the same room again is a new beginning");
        }

        [Test] public void ARoomWithNoNameIsAlwaysWritten()
        {
            var memory = new TelemetryWrittenMemory();
            Assert.IsTrue(memory.FirstTime("join", null, 1));
            Assert.IsTrue(memory.FirstTime("join", "", 1));
        }

        [Test] public void TelemetryAsksTheMemoryBeforeTheJoinLineAndTheSessionLineAndForgetsOnLeaving()
        {
            MethodInfo first = Method(typeof(TelemetryWrittenMemory), nameof(TelemetryWrittenMemory.FirstTime));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnJoinedRoom", first));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "TryOpenFile", first));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnLeftRoom", Method(typeof(TelemetryWrittenMemory), nameof(TelemetryWrittenMemory.ForgetAll))));
        }

        // ---------------------------------------------------------------- a stale "arrived dead" flag

        [Test] public void AnArrivalIsStampedWithTheArrivalAndARealDeathWithNow()
        {
            Assert.AreEqual(100, ArrivalDeathRules.StampFor(true, 100, 900));
            Assert.AreEqual(900, ArrivalDeathRules.StampFor(false, 100, 900));
        }

        [Test] public void TakingTheFlagReadsItAndClearsIt()
        {
            bool flag = true;
            Assert.IsTrue(ArrivalDeathRules.Take(ref flag));
            Assert.IsFalse(flag, "a flag left standing would stamp a later real death as an arrival");
            Assert.IsFalse(ArrivalDeathRules.Take(ref flag));
        }

        [Test] public void ThePlayerTakesTheFlagInEveryPlaceItCouldBeLeftStanding()
        {
            MethodInfo take = Method(typeof(ArrivalDeathRules), nameof(ArrivalDeathRules.Take));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), "DieForGoodInSuddenDeath", take), "before its early return");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), "DieForGoodInSuddenDeath", Method(typeof(ArrivalDeathRules), nameof(ArrivalDeathRules.StampFor))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), "ResetForRoundStart", take));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), "ResetForMatchStart", take));
        }

        // ---------------------------------------------------------------- one countdown mapping

        [Test] public void TheSuddenDeathCountdownIsTheBreakCountdown()
        {
            var config = ScriptableObject.CreateInstance<DominionConfig>();
            try
            {
                typeof(DominionConfig).GetField("breakCountdownSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(config, 7.5f);
                Assert.AreEqual(7.5f, config.SuddenDeathCountdownSeconds);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test] public void BothPlacesAskTheOneMapping()
        {
            MethodInfo mapping = Getter(typeof(DominionConfig), nameof(DominionConfig.SuddenDeathCountdownSeconds));
            MethodInfo raw = Getter(typeof(DominionConfig), nameof(DominionConfig.BreakCountdownSeconds));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "ArrivalStampMs", mapping));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "Update", mapping));
            Assert.IsFalse(IlWiring.Uses(typeof(DominionDirector), "ArrivalStampMs", raw));
            Assert.IsFalse(IlWiring.Uses(typeof(DominionDirector), "Update", raw));
        }

        // ---------------------------------------------------------------- two spawn points per team

        [Test] public void TeammatesTakeDifferentSlotsInTheOrderOfTheirActorNumbers()
        {
            var team = new[] { 7, 3 };
            Assert.AreEqual(0, SpawnSlotRules.SlotFor(team, 3, 2));
            Assert.AreEqual(1, SpawnSlotRules.SlotFor(team, 7, 2));
        }

        [Test] public void ABiggerTeamThanTheListWrapsAndAMissingPlayerOrNoSlotsTakeSlotZero()
        {
            var team = new[] { 1, 2, 3 };
            Assert.AreEqual(0, SpawnSlotRules.SlotFor(team, 3, 2), "the third player wraps to the first slot");
            Assert.AreEqual(0, SpawnSlotRules.SlotFor(team, 99, 2), "not in the list");
            Assert.AreEqual(0, SpawnSlotRules.SlotFor(team, 2, 0), "no slots");
            Assert.AreEqual(0, SpawnSlotRules.SlotFor(null, 2, 2));
        }

        [Test] public void EveryPlaceThatPutsAPlayerAtASpawnAsksRoomManagerForTheirOwnPoint()
        {
            MethodInfo point = Method(typeof(RoomManager), nameof(RoomManager.SpawnPointFor));
            foreach (string method in new[] { "ResetForMatchStart", "ResetForRoundStart", "MoveToSpawnPoint", "ChooseSpawnPoint" })
                Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), method, point), method);
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "SpawnPlayerOnTeam", point));
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "SpawnPointFor", Method(typeof(SpawnSlotRules), nameof(SpawnSlotRules.SlotFor))));
        }

        // ---------------------------------------------------------------- the rectangular minimap frame

        [Test] public void ATriangleArenasFrameIsASquareOfTheCornerSize()
        {
            Assert.AreEqual(new Vector2(340f, 340f), MinimapLayout.FrameSize(false, 154f, 154f, 340f));
            Assert.AreEqual(new Vector2(340f, 340f), MinimapLayout.FrameSize(false, 200f, 50f, 340f), "the flag, not the shape, decides");
        }

        [Test] public void ALongMapsFrameKeepsItsShapeWithTheLongSideAtTheCornerSize()
        {
            Vector2 wide = MinimapLayout.FrameSize(true, 100f, 40f, 300f);
            Assert.AreEqual(300f, wide.x, 0.001f);
            Assert.AreEqual(120f, wide.y, 0.001f);
            Vector2 tall = MinimapLayout.FrameSize(true, 40f, 100f, 300f);
            Assert.AreEqual(120f, tall.x, 0.001f);
            Assert.AreEqual(300f, tall.y, 0.001f);
        }

        [Test] public void ARectangularFrameWithNoSizeFallsBackToTheSquare()
        {
            Assert.AreEqual(new Vector2(300f, 300f), MinimapLayout.FrameSize(true, 0f, 40f, 300f));
        }

        [Test] public void TheMinimapUsesTheFrameRuleAndTheFlagToChooseItsShape()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "Awake", Method(typeof(MinimapLayout), nameof(MinimapLayout.FrameSize))));
            MethodInfo flag = Getter(typeof(MinimapConfig), nameof(MinimapConfig.RectangularFrame));
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "BuildFrame", flag));
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "TryBuild", flag));
        }

        [Test] public void TheSightPictureCoversTheSameRectangleAsTheMinimap()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Vision.TeamSight), "CreateSightTexture", Getter(typeof(MinimapConfig), nameof(MinimapConfig.WorldDepthMetres))));
        }

        // ---------------------------------------------------------------- the lane's pieces

        [Test] public void OnlyThePiecesOfTheHAreBlocks()
        {
            Assert.IsTrue(DominionLaneLayout.IsBlockWall("H Wall Left"));
            Assert.IsTrue(DominionLaneLayout.IsBlockWall("H Arm Right"));
            Assert.IsFalse(DominionLaneLayout.IsBlockWall("Hall Wall Top"), "the hall's walls are outer walls");
            Assert.IsFalse(DominionLaneLayout.IsBlockWall("Pocket Wall Left End"));
            Assert.IsFalse(DominionLaneLayout.IsBlockWall(null));
        }

        [Test] public void TheLayoutKeepsTheBoardsTowerSizesAndTheSpawnOffset()
        {
            var layout = AssetDatabase.LoadAssetAtPath<DominionLaneLayout>(Overpower.EditorTools.DominionLaneBuilder.LayoutPath);
            Assert.Greater(layout.ZoneTowerSizeMetres, 0f);
            Assert.Greater(layout.SpawnTowerSizeMetres, layout.ZoneTowerSizeMetres, "the spawn tower is the bigger one on the board");
            Assert.Greater(layout.SpawnSideOffsetMetres, 0f);
        }
    }
}
