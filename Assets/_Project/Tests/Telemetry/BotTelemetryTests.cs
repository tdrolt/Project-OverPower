using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Net;
using Overpower.Telemetry;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>
    /// A bot is a seat the host plays: no client, so no file of its own and no join the report could read as a person. The logs mark it
    /// (bot:true on its session, join and leave lines, a marker the aggregator reads back), the host writes one file per bot
    /// ("{actor}_{nick}.jsonl") from PlayerTelemetry on the bot's body, and none of the bot's facts land in the host's own file.
    /// </summary>
    public class BotTelemetryTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static MethodInfo Private(System.Type type, string name) => type.GetMethod(name, Any);

        // ---------------------------------------------------------------- the marker the aggregator reads back

        [Test]
        public void ABotMarkerNamesTheBotsActorAndNick()
        {
            string note = LobbyMarkerNotes.BotSeen(1001, "Rex");
            Assert.IsTrue(LobbyMarkerNotes.TryReadBot(note, out int actor, out string nick));
            Assert.AreEqual(1001, actor);
            Assert.AreEqual("Rex", nick);
        }

        [Test]
        public void ANickWithSpacesComesBackWhole()
        {
            Assert.IsTrue(LobbyMarkerNotes.TryReadBot(LobbyMarkerNotes.BotSeen(1002, "Big Bot 2"), out int actor, out string nick));
            Assert.AreEqual(1002, actor);
            Assert.AreEqual("Big Bot 2", nick);
        }

        [Test]
        public void ABotWithNoNickIsStillRead()
        {
            Assert.IsTrue(LobbyMarkerNotes.TryReadBot(LobbyMarkerNotes.BotSeen(1003, ""), out int actor, out string nick));
            Assert.AreEqual(1003, actor);
            Assert.AreEqual("", nick);
            Assert.IsTrue(LobbyMarkerNotes.TryReadBot(LobbyMarkerNotes.BotSeen(1004, null), out actor));
            Assert.AreEqual(1004, actor);
        }

        [Test]
        public void OnlyABotNoteIsReadAsABot()
        {
            foreach (string note in new[] { null, "", "start game", LobbyMarkerNotes.SpectatorSeen(3), LobbyMarkerNotes.HostChanged(1, "Kim"),
                                            "bot joined: actor x", "bot joined: actor -5 Rex", "bot joined: actor " })
            {
                Assert.IsFalse(LobbyMarkerNotes.TryReadBot(note, out int actor), note);
                Assert.AreEqual(-1, actor, note);
            }
        }

        [Test]
        public void ASpectatorNoteAndABotNoteDoNotReadAsEachOther()
        {
            Assert.IsFalse(LobbyMarkerNotes.TryReadSpectator(LobbyMarkerNotes.BotSeen(1001, "Rex"), out _));
            Assert.IsFalse(LobbyMarkerNotes.TryReadBot(LobbyMarkerNotes.SpectatorSeen(3), out _));
        }

        [Test]
        public void TheBotKeysAreOwnedByNoOtherTelemetryKey()
        {
            List<string> values = typeof(TelemetryKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()).ToList();
            Assert.AreEqual("bot", TelemetryKeys.Bot);
            Assert.AreEqual(1, values.Count(v => v == TelemetryKeys.Bot), "a second key named like the bot flag would collide in a line");
            Assert.AreEqual(1, values.Count(v => v == TelemetryKeys.BotBuild));
        }

        // ---------------------------------------------------------------- the host's zip carries the bots' files

        [Test]
        public void TheBotFilesAreTheJsonlFilesOfActorsAboveTheBotBase()
        {
            var files = new List<string>
            {
                "1_Tudor.jsonl", "1001_Rex.jsonl", "1002_Big Bot.jsonl",
                "bug_1_5.png", "1001_Rex.png", "1000_Edge.jsonl", "abc_x.jsonl", "_x.jsonl", "match.id", "", null,
            };
            CollectionAssert.AreEquivalent(new[] { "1001_Rex.jsonl", "1002_Big Bot.jsonl" }, MatchLogZipRule.SelectBotFiles(files));
        }

        [Test]
        public void NoFileListSelectsNoBotFiles() => Assert.IsEmpty(MatchLogZipRule.SelectBotFiles(null));

        [Test]
        public void APlayersOwnFilesNeverIncludeABotsSoAnotherLocalClientsZipStaysItsOwn()
        {
            var files = new List<string> { "1_Tudor.jsonl", "1001_Rex.jsonl", "100_Other.jsonl" };
            CollectionAssert.AreEqual(new[] { "1_Tudor.jsonl" }, MatchLogZipRule.SelectOwnFiles(files, 1));
            Assert.IsEmpty(MatchLogZipRule.SelectOwnFiles(files, 10));
            CollectionAssert.AreEqual(new[] { "100_Other.jsonl" }, MatchLogZipRule.SelectOwnFiles(files, 100));
        }

        [Test]
        public void TheZipAddsTheBotFilesOnlyWhenThisClientWroteSome()
        {
            MethodInfo hasBotLogs = typeof(MatchTelemetry).GetProperty(nameof(MatchTelemetry.HasBotLogs)).GetGetMethod();
            Assert.IsTrue(IlWiring.Uses(typeof(MatchLogZip), "TryZip", typeof(MatchLogZipRule).GetMethod(nameof(MatchLogZipRule.SelectBotFiles))));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchLogZip), "TryZip", hasBotLogs));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchLogZip), "HandleBeforeClose", hasBotLogs), "remembered before leaving the room clears it");
        }

        // ---------------------------------------------------------------- PlayerTelemetry on a bot's body

        [Test]
        public void ABodyKnowsItIsABotFromItsOwnerAtAwake() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerTelemetry), "Awake", typeof(BodyRole).GetMethod(nameof(BodyRole.IsBot))));

        [Test]
        public void EveryLinePlayerTelemetryWritesGoesThroughTheOneRouter()
        {
            MethodInfo log = typeof(MatchTelemetry).GetMethod(nameof(MatchTelemetry.Log));
            MethodInfo logBot = typeof(MatchTelemetry).GetMethod(nameof(MatchTelemetry.LogBot));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerTelemetry), "Emit", log), "a person's lines go to the host's own file");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerTelemetry), "Emit", logBot), "a bot's lines go to the bot's file");
            IEnumerable<string> others = typeof(PlayerTelemetry).GetMethods(Any).Where(m => m.Name != "Emit" && !m.Name.Contains("<")).Select(m => m.Name).Distinct();
            foreach (string name in others)
            {
                Assert.IsFalse(IlWiring.Uses(typeof(PlayerTelemetry), name, log), name + " must hand its line to Emit, or a bot's fact lands in the host's file");
                Assert.IsFalse(IlWiring.Uses(typeof(PlayerTelemetry), name, logBot), name);
            }
        }

        [Test]
        public void ADummyHitIsLoggedByTheBodyWhosePlayerFiredIt()
        {
            MethodInfo ownerActor = typeof(PhotonView).GetProperty(nameof(PhotonView.OwnerActorNr)).GetGetMethod();
            MethodInfo localPlayer = typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.LocalPlayer)).GetGetMethod();
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerTelemetry), "HandleDummyDamaged", ownerActor), "a bot's shot on a dummy is the bot's to log");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(PlayerTelemetry), "HandleDummyDamaged", localPlayer), "the host's own file must not take a bot's shot");
        }

        [Test]
        public void ABotsShopCanReachTheRecorderThatLogsPurchases()
        {
            foreach (string name in new[] { "HandlePurchased", "HandleRefunded", "HandlePurchaseRefused" })
                Assert.IsTrue(typeof(PlayerTelemetry).GetMethod(name, BindingFlags.Instance | BindingFlags.Public) != null, name);
        }

        // ---------------------------------------------------------------- MatchTelemetry writes the bot's file and marks the bot

        [Test]
        public void ABotEnteringTheRoomIsLoggedAsAJoinAndNotedByTheMaster()
        {
            MethodInfo note = Private(typeof(MatchTelemetry), "NoteBot");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnPlayerEnteredRoom", note), "a bot added while the match runs");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnJoinedRoom", note), "a scene that loads inside the room finds its bots already there");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnMasterClientSwitched", note));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "NoteBot", typeof(MatchTelemetry).GetMethod(nameof(MatchTelemetry.DropMarker))));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "NoteBot", typeof(LobbyMarkerNotes).GetMethod(nameof(LobbyMarkerNotes.BotSeen))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(MatchTelemetry), "NoteBot", typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.IsMasterClient)).GetGetMethod()),
                "only the host drops the marker");
        }

        [Test]
        public void TheJoinAndTheSessionLinesCarryTheBotsBuild()
        {
            MethodInfo build = typeof(BotPlayer).GetProperty(nameof(BotPlayer.Build)).GetGetMethod();
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "LogJoinOrLeave", build));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "WriteSessionLine", build));
        }

        [Test]
        public void ABotsFileOpensByTheSameRoleRuleAndSessionMemoryAsAPlayers()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "TryOpenBotFile", typeof(TelemetryRoleRule).GetMethod(nameof(TelemetryRoleRule.MayOpenFile))));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "TryOpenBotFile", typeof(TelemetryWrittenMemory).GetMethod(nameof(TelemetryWrittenMemory.FirstTime))),
                "a scene reload appends to the bot's file without a second session line");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "TryOpenBotFile", Private(typeof(MatchTelemetry), "WriteSessionLine")));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "LogBot", Private(typeof(MatchTelemetry), "TryOpenBotFile")));
        }

        [Test]
        public void TheBotFilesAreFlushedOnTheTimerAndClosedWhereThePlayersFileCloses()
        {
            MethodInfo flush = Private(typeof(MatchTelemetry), "FlushBotLogs");
            MethodInfo close = Private(typeof(MatchTelemetry), "CloseBotLogs");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "Update", flush));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), nameof(MatchTelemetry.FlushNow), flush), "the zip reads the folder after FlushNow");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnApplicationQuit", close));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnDestroy", close));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchTelemetry), "OnLeftRoom", close));
        }
    }
}
