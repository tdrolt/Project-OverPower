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

        [Test] public void StartHoldsTheQueueAndTheResumeRoutineLetsItGo()
        {
            // PhotonNetwork lives in another assembly, so the calls are read through RoomManager's one method that sets the flag.
            MethodInfo setter = Method(typeof(RoomManager), "SetMessageQueueRunning");
            Assert.IsNotNull(setter);
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "Start", setter));
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "Start", Method(typeof(SceneLoadRules), nameof(SceneLoadRules.QueueRunsWhileResuming))), "the hold is the tested rule's answer");
            Assert.IsFalse(SceneLoadRules.QueueRunsWhileResuming(), "the queue is held while a new scene reads the room");

            // The release must sit in the routine's finally block, after the loop that repeats OnJoinedRoom on the listeners: a release anywhere else
            // would skip on an error (the client would never see another room update) or let a room update through before the listeners have read the room.
            System.Type machine = typeof(RoomManager).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).First(t => t.Name.StartsWith("<ResumeInRoomAfterSceneLoad>"));
            MethodBase moveNext = machine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var releases = IlWiring.CallOffsets(moveNext, (MethodBase)setter);
            var loopCalls = IlWiring.CallOffsets(moveNext, Method(typeof(MonoBehaviourPunCallbacks), "OnJoinedRoom"));
            Assert.AreEqual(1, releases.Count, "one release, nowhere else in the routine");
            Assert.IsNotEmpty(loopCalls, "the loop that repeats OnJoinedRoom is in the routine");
            ExceptionHandlingClause guard = moveNext.GetMethodBody().ExceptionHandlingClauses.FirstOrDefault(c => c.Flags == ExceptionHandlingClauseOptions.Finally
                && releases[0] >= c.HandlerOffset && releases[0] < c.HandlerOffset + c.HandlerLength);
            Assert.IsNotNull(guard, "the release is inside a finally block");
            foreach (int call in loopCalls)
                Assert.IsTrue(call >= guard.TryOffset && call < guard.TryOffset + guard.TryLength, "the loop runs inside the try that this finally guards, so the release comes after it");
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

        [Test] public void AQueuedLineIsNotWrittenUntilItIsMarkedSoTheNextSceneStillWritesIt()
        {
            var memory = new TelemetryWrittenMemory();
            Assert.IsFalse(memory.WasWritten("join", "room a", 3), "nothing has reached a file yet: a join that was only queued in a scene that is gone must be written by the next one");
            memory.MarkWritten("join", "room a", 3);
            Assert.IsTrue(memory.WasWritten("join", "room a", 3));
            Assert.IsFalse(memory.WasWritten("join", "room a", 4));
            Assert.IsFalse(memory.WasWritten("join", "", 3));
            memory.ForgetAll();
            Assert.IsFalse(memory.WasWritten("join", "room a", 3));
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
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "TryOpenFile", first));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnLeftRoom", Method(typeof(TelemetryWrittenMemory), nameof(TelemetryWrittenMemory.ForgetAll))));
        }

        // ---------------------------------------------------------------- Task 14 Part 0: the player's own join line (review item 1)

        [Test] public void AFileThatOpensBeforeTheJoinCheckStillGetsTheJoinLine()
        {
            // The rejoiner's and the lane-scene joiner's order: the room already tells who they are, so the file opens first and the join check comes after.
            var memory = new TelemetryWrittenMemory();
            var join = new LocalJoinLine();
            join.OnFileOpened(memory, "room a", 3);
            Assert.IsFalse(memory.WasWritten("join", "room a", 3), "opening a file writes no join line, so it marks none");
            Assert.IsTrue(join.OnJoinedRoom(memory, "room a", 3, fileIsOpen: true), "the join line is logged");
            Assert.IsTrue(memory.WasWritten("join", "room a", 3), "and marked, being in the open file");
        }

        [Test] public void AJoinQueuedBehindAClosedFileIsMarkedWhenTheFileOpensAndOnlyThen()
        {
            var memory = new TelemetryWrittenMemory();
            var join = new LocalJoinLine();
            Assert.IsTrue(join.OnJoinedRoom(memory, "room a", 3, fileIsOpen: false));
            Assert.IsFalse(memory.WasWritten("join", "room a", 3), "only queued: a scene that dies before the file opens must leave it to the next one");
            join.OnFileOpened(memory, "room a", 3);
            Assert.IsTrue(memory.WasWritten("join", "room a", 3));
            Assert.IsFalse(join.OnJoinedRoom(memory, "room a", 3, fileIsOpen: true), "the new scene does not write it again");
        }

        [Test] public void TheQueuedFlagIsSpentByTheOpenThatWritesIt()
        {
            var memory = new TelemetryWrittenMemory();
            var join = new LocalJoinLine();
            join.OnJoinedRoom(memory, "room a", 3, fileIsOpen: false);
            join.OnFileOpened(memory, "room a", 3);
            memory.ForgetAll();
            join.OnFileOpened(memory, "room a", 3);
            Assert.IsFalse(memory.WasWritten("join", "room a", 3), "a later open (another room) must not mark a join nobody queued");
            join.OnJoinedRoom(memory, "room a", 3, fileIsOpen: false);
            join.Reset();
            join.OnFileOpened(memory, "room a", 3);
            Assert.IsFalse(memory.WasWritten("join", "room a", 3), "leaving the room drops a queued join");
        }

        [Test] public void TelemetryUsesTheJoinLineRuleAtBothPlaces()
        {
            MethodInfo onJoined = Method(typeof(LocalJoinLine), nameof(LocalJoinLine.OnJoinedRoom));
            MethodInfo onOpened = Method(typeof(LocalJoinLine), nameof(LocalJoinLine.OnFileOpened));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnJoinedRoom", onJoined));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "TryOpenFile", onOpened));
            Assert.IsFalse(IlWiring.Uses(typeof(MatchTelemetry), "TryOpenFile", Method(typeof(TelemetryWrittenMemory), nameof(TelemetryWrittenMemory.MarkWritten))), "no unconditional mark when the file opens");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnLeftRoom", Method(typeof(LocalJoinLine), nameof(LocalJoinLine.Reset))));
        }

        // ---------------------------------------------------------------- Task 14 Part 0: the shield's own clear (review item 2)

        [Test] public void TheClearIsWrittenForAShieldStartedHereOrAnyValueStillSet()
        {
            Assert.IsTrue(RespawnShieldRules.MustWriteClear(true, 0), "started on this client: the echo may not be back, the room may hold the end time");
            Assert.IsTrue(RespawnShieldRules.MustWriteClear(false, 12345), "the property still holds a shield");
            Assert.IsFalse(RespawnShieldRules.MustWriteClear(false, 0), "nothing set and nothing started: no write");
        }

        [Test] public void ClearShieldAsksTheRuleAndStartShieldRemembersItsOwnEnd()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "ClearShield", Method(typeof(RespawnShieldRules), nameof(RespawnShieldRules.MustWriteClear))));
            FieldInfo own = typeof(RespawnShield).GetField("ownEndMs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(own);
            Assert.IsTrue(IlWiring.Stores(typeof(RespawnShield), "StartShield", own), "the owner is shielded at once, not a round trip later");
            Assert.IsTrue(IlWiring.Stores(typeof(RespawnShield), "ClearShield", own), "and the clear forgets it");
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
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "FlowNumbersOf", mapping), "the master's flow numbers (Update asks FlowNumbersOf)");
            Assert.IsFalse(IlWiring.Uses(typeof(DominionDirector), "ArrivalStampMs", raw));
            Assert.IsFalse(IlWiring.Uses(typeof(DominionDirector), "FlowNumbersOf", raw));
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
            // The numbers are the board drawing's as it stands today (Tudor may redraw it): change them here when he does.
            // The drawing's numbers (a 24 px square at 12 px per metre, a 46 px circle, the two spawn points 2 m either side of the tower).
            Assert.AreEqual(2f, layout.ZoneTowerSizeMetres, 0.001f);
            Assert.AreEqual(3.8f, layout.SpawnTowerSizeMetres, 0.001f);
            Assert.AreEqual(2f, layout.SpawnSideOffsetMetres, 0.001f);
        }

        // ---------------------------------------------------------------- A41/A42: the straight lane camera

        [Test] public void WhiteOnTheEastAndPurpleOnTheWestEachHaveTheirOwnSpawnOnTheLeft()
        {
            Assert.AreEqual(180f, CameraYawRules.OwnSpawnLeftYaw(28f, 0f), 0.001f, "the spawn is east of the middle: turn the view round so east is the left of the screen");
            Assert.AreEqual(0f, CameraYawRules.OwnSpawnLeftYaw(-28f, 0f), 0.001f, "the spawn is west: the ordinary view already has west on the left");
        }

        [Test] public void ScreenRightPointsAwayFromTheOwnSpawnWhateverTheSpawnDirection()
        {
            foreach (Vector2 toSpawn in new[] { new Vector2(30f, 0f), new Vector2(-30f, 0f), new Vector2(0f, 20f), new Vector2(0f, -20f), new Vector2(17f, -9f), new Vector2(-4f, 12f) })
            {
                float yaw = CameraYawRules.OwnSpawnLeftYaw(toSpawn.x, toSpawn.y) * Mathf.Deg2Rad;
                var screenRight = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw)); // camera forward is (sin yaw, cos yaw); right is forward turned clockwise
                Assert.AreEqual(-1f, Vector2.Dot(screenRight, toSpawn.normalized), 0.0001f, toSpawn.ToString());
            }
        }

        private static Transform[] Spawns(params Vector3?[] places)
        {
            var list = new Transform[places.Length];
            for (int i = 0; i < places.Length; i++)
                if (places[i].HasValue) { list[i] = new GameObject("spawn " + i).transform; list[i].position = places[i].Value; }
            return list;
        }

        private static void Destroy(Transform[] spawns) { foreach (Transform t in spawns) if (t != null) Object.DestroyImmediate(t.gameObject); }

        [Test] public void TheVectorToASpawnStartsAtTheMiddleOfAllThePlacedSpawns()
        {
            Transform[] spawns = Spawns(new Vector3(30f, 5f, 0f), new Vector3(-30f, 5f, 0f));
            try
            {
                Assert.IsTrue(CameraYawRules.ToSpawnFromCentre(spawns, 0, out Vector2 toFirst));
                Assert.AreEqual(30f, toFirst.x, 0.001f);
                Assert.AreEqual(0f, toFirst.y, 0.001f, "height is dropped");
                Assert.IsTrue(CameraYawRules.ToSpawnFromCentre(spawns, 1, out Vector2 toSecond));
                Assert.AreEqual(-30f, toSecond.x, 0.001f);
            }
            finally { Destroy(spawns); }
        }

        [Test] public void AMissingSpawnIsLeftOutOfTheMiddleAndAMissingTeamHasNoVector()
        {
            Transform[] spawns = Spawns(new Vector3(0f, 0f, 10f), null, new Vector3(0f, 0f, -10f));
            try
            {
                Assert.IsTrue(CameraYawRules.ToSpawnFromCentre(spawns, 2, out Vector2 v));
                Assert.AreEqual(-10f, v.y, 0.001f, "the middle is of the two that exist");
                Assert.IsFalse(CameraYawRules.ToSpawnFromCentre(spawns, 1, out _), "that team has no spawn");
                Assert.IsFalse(CameraYawRules.ToSpawnFromCentre(spawns, 3, out _));
                Assert.IsFalse(CameraYawRules.ToSpawnFromCentre(spawns, -1, out _));
                Assert.IsFalse(CameraYawRules.ToSpawnFromCentre(null, 0, out _));
                Assert.AreEqual(0, CameraYawRules.FirstTeamWithSpawn(spawns));
                Transform[] second = Spawns(null, Vector3.one); // kept so the finally can destroy it: an undestroyed object dirties the open scene
                try
                {
                    Assert.AreEqual(1, CameraYawRules.FirstTeamWithSpawn(second), "no assumption that the first slot is the one that is filled");
                    Assert.AreEqual(-1, CameraYawRules.FirstTeamWithSpawn(Spawns(null, null)));
                }
                finally { Destroy(second); }
            }
            finally { Destroy(spawns); }
        }

        [Test] public void TheCameraAndTheSpectatorAskTheOneSharedVectorRule()
        {
            MethodInfo shared = Method(typeof(CameraYawRules), nameof(CameraYawRules.ToSpawnFromCentre));
            Assert.IsTrue(IlWiring.Uses(typeof(CameraTracking), "ResolveTeamYaw", shared));
            Assert.IsTrue(IlWiring.Uses(typeof(SpectatorSeatView), "SpectatorYaw", shared));
            Assert.IsTrue(IlWiring.Uses(typeof(SpectatorSeatView), "SpectatorYaw", Method(typeof(CameraYawRules), nameof(CameraYawRules.FirstTeamWithSpawn))), "the spectator's team is the first one with a spawn, not index 0");
        }

        [Test] public void ASceneWithoutTheStraightViewKeepsTheAngledYawExactly()
        {
            float old = Mathf.Atan2(3f, 4f) * Mathf.Rad2Deg + 120f; // the formula CameraTracking had before
            Assert.AreEqual(old, CameraYawRules.TeamYaw(false, 3f, 4f, 120f), 0.0001f);
            Assert.AreEqual(CameraYawRules.OwnSpawnLeftYaw(3f, 4f), CameraYawRules.TeamYaw(true, 3f, 4f, 120f), 0.0001f, "the offset belongs to the angled view only");
        }

        [Test] public void SpectatorsWatchFromWhitesSideOnAStraightMapAndFromTheThemesAngleElsewhere()
        {
            Assert.AreEqual(180f, CameraYawRules.SpectatorYaw(true, 33f, 28f, 0f), 0.001f);
            Assert.AreEqual(33f, CameraYawRules.SpectatorYaw(false, 33f, 28f, 0f), 0.001f);
        }

        [Test] public void TheCameraAndTheSpectatorViewAskTheYawRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(CameraTracking), "ResolveTeamYaw", Method(typeof(CameraYawRules), nameof(CameraYawRules.TeamYaw))));
            Assert.IsTrue(IlWiring.Uses(typeof(CameraTracking), "ResolveTeamYaw", Method(typeof(SceneCameraConfig), nameof(SceneCameraConfig.SceneWantsOwnSpawnOnLeft))));
            Assert.IsTrue(IlWiring.Uses(typeof(SpectatorSeatView), "SpectatorYaw", Method(typeof(CameraYawRules), nameof(CameraYawRules.SpectatorYaw))));
            Assert.IsTrue(IlWiring.Uses(typeof(SpectatorSeatView), "Begin", Method(typeof(SpectatorSeatView), "SpectatorYaw")));
        }

        // ---------------------------------------------------------------- Task 12 findings: teammates from the seat table, the shield up at once

        [Test] public void TheSeatTableNamesEveryTeammateEvenBeforeTheirTeamPropertiesHaveArrived()
        {
            var seats = new System.Collections.Generic.Dictionary<string, int> { { "sT00", 5 }, { "sT01", 9 }, { "sT10", 7 }, { "sS0", 11 }, { "sT02", 0 } };
            CollectionAssert.AreEquivalent(new[] { 5, 9 }, SpawnSlotRules.TeammatesFromSeats(seats, 0));
            CollectionAssert.AreEquivalent(new[] { 7 }, SpawnSlotRules.TeammatesFromSeats(seats, 1), "a spectator seat is nobody's teammate");
            CollectionAssert.IsEmpty(SpawnSlotRules.TeammatesFromSeats(seats, 2));
            CollectionAssert.IsEmpty(SpawnSlotRules.TeammatesFromSeats(null, 0));
        }

        [Test] public void TwoPlayersOnOneSeatTableTakeDifferentSpawnSlots()
        {
            var seats = new System.Collections.Generic.Dictionary<string, int> { { "sT00", 5 }, { "sT01", 9 } };
            var team = SpawnSlotRules.TeammatesFromSeats(seats, 0);
            Assert.AreNotEqual(SpawnSlotRules.SlotFor(team, 5, 2), SpawnSlotRules.SlotFor(team, 9, 2));
        }

        [Test] public void TheFirstBodyAsksTheSeatTableForItsTeammates()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "SpawnPointFor", Method(typeof(SpawnSlotRules), nameof(SpawnSlotRules.TeammatesFromSeats))));
        }

        [Test] public void TheOwnersShieldIsUpAsSoonAsTheOwnerStartedItBeforeTheServerEchoesIt()
        {
            Assert.IsTrue(RespawnShieldRules.IsUpForOwner(0, 5000, 4000), "nothing written back yet, but this client started it");
            Assert.IsTrue(RespawnShieldRules.IsUpForOwner(5000, 0, 4000), "the property alone, as for everyone else");
            Assert.IsFalse(RespawnShieldRules.IsUpForOwner(0, 0, 4000));
            Assert.IsFalse(RespawnShieldRules.IsUpForOwner(3000, 3500, 4000), "both ended");
        }

        [Test] public void TheOwnersHitBlockAsksTheOwnerRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "OwnerIsUp", Method(typeof(RespawnShieldRules), nameof(RespawnShieldRules.OwnerShieldUp))));
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShieldRules), nameof(RespawnShieldRules.OwnerShieldUp), Method(typeof(RespawnShieldRules), nameof(RespawnShieldRules.IsUpForOwner))));
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "get_IsUp", Method(typeof(RespawnShield), "OwnerIsUp")));
        }
    }
}