using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Lobby Task 13: the small rules behind the match log's lobby markers, its pending-line buffer, when a spectator host's file opens,
    /// and the zip's name.</summary>
    public class MatchLogRulesTests
    {
        // ---- the pending buffer: the newest lines survive

        [Test]
        public void TheBufferKeepsTheNewestLinesAndCountsWhatItDropped()
        {
            var buffer = new PendingLineBuffer(3);
            for (int i = 1; i <= 5; i++) buffer.Add("line" + i);
            CollectionAssert.AreEqual(new[] { "line3", "line4", "line5" }, buffer.Lines);
            Assert.AreEqual(2, buffer.Dropped);
            Assert.AreEqual(3, buffer.Count);
        }

        [Test]
        public void NothingIsDroppedUnderTheCap()
        {
            var buffer = new PendingLineBuffer(200);
            buffer.Add("a");
            buffer.Add("b");
            CollectionAssert.AreEqual(new[] { "a", "b" }, buffer.Lines);
            Assert.AreEqual(0, buffer.Dropped);
        }

        [Test]
        public void ClearingForgetsTheLinesAndTheCount()
        {
            var buffer = new PendingLineBuffer(1);
            buffer.Add("a");
            buffer.Add("b");
            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(0, buffer.Dropped);
        }

        [Test]
        public void TheDroppedNoteSaysHowMany() =>
            StringAssert.Contains("17", LobbyMarkerNotes.EarlyLinesDropped(17));

        // ---- a spectator host's file opens when its spec flag arrives, not only when a team does

        [Test]
        public void TheFileIsRetriedWhenTheTeamOrTheSpectatorFlagOfTheLocalPlayerChanges()
        {
            Assert.IsTrue(TelemetryRoleRule.RetriesOpenOnChange(true, teamChanged: true, spectatorChanged: false));
            Assert.IsTrue(TelemetryRoleRule.RetriesOpenOnChange(true, teamChanged: false, spectatorChanged: true), "the spec flag arrives after the stage edge");
            Assert.IsFalse(TelemetryRoleRule.RetriesOpenOnChange(true, teamChanged: false, spectatorChanged: false));
            Assert.IsFalse(TelemetryRoleRule.RetriesOpenOnChange(false, teamChanged: true, spectatorChanged: true), "somebody else's properties");
        }

        // ---- the lobby marker notes

        [Test]
        public void EachLobbyEventHasItsOwnReadableNote()
        {
            StringAssert.Contains("Tudor's lobby", LobbyMarkerNotes.LobbyCreated("Tudor's lobby", "Conquest 3v3v3"));
            StringAssert.Contains("Conquest 3v3v3", LobbyMarkerNotes.LobbyCreated("Tudor's lobby", "Conquest 3v3v3"));
            StringAssert.Contains("sT10", LobbyMarkerNotes.SeatTaken("sT10"));
            StringAssert.Contains("seat taken", LobbyMarkerNotes.SeatTaken("sT10"));
            StringAssert.Contains("sT10", LobbyMarkerNotes.SeatLeft("sT10"));
            StringAssert.Contains("seat left", LobbyMarkerNotes.SeatLeft("sT10"));
            StringAssert.Contains("start game", LobbyMarkerNotes.StartGame);
            StringAssert.Contains("end warm-up", LobbyMarkerNotes.EndWarmup);
            StringAssert.Contains("4", LobbyMarkerNotes.HostChanged(4, "Kim"));
            StringAssert.Contains("host changed", LobbyMarkerNotes.HostChanged(4, "Kim"));
        }

        [Test]
        public void AChangeOfSeatGivesTheRightNotes()
        {
            CollectionAssert.AreEqual(new[] { LobbyMarkerNotes.SeatTaken("sT10") }, LobbyMarkerNotes.SeatChange(null, "sT10"));
            CollectionAssert.AreEqual(new[] { LobbyMarkerNotes.SeatLeft("sT10") }, LobbyMarkerNotes.SeatChange("sT10", null));
            CollectionAssert.AreEqual(new[] { LobbyMarkerNotes.SeatLeft("sT10"), LobbyMarkerNotes.SeatTaken("sS0") }, LobbyMarkerNotes.SeatChange("sT10", "sS0"), "a move is a leave and a take");
            Assert.IsEmpty(LobbyMarkerNotes.SeatChange("sT10", "sT10"), "nothing changed, nothing noted");
            Assert.IsEmpty(LobbyMarkerNotes.SeatChange(null, null));
        }

        [Test]
        public void ASpectatorNoteCarriesTheActorAndReadsBack()
        {
            string note = LobbyMarkerNotes.SpectatorSeen(7);
            Assert.IsTrue(LobbyMarkerNotes.TryReadSpectator(note, out int actor));
            Assert.AreEqual(7, actor);
        }

        [Test]
        public void OnlyASpectatorNoteReadsAsOne()
        {
            Assert.IsFalse(LobbyMarkerNotes.TryReadSpectator("countdown start", out _));
            Assert.IsFalse(LobbyMarkerNotes.TryReadSpectator(LobbyMarkerNotes.SeatTaken("sT00"), out _));
            Assert.IsFalse(LobbyMarkerNotes.TryReadSpectator(null, out _));
            Assert.IsFalse(LobbyMarkerNotes.TryReadSpectator(LobbyMarkerNotes.SpectatorSeen(3) + "x", out _), "a typed note that merely starts the same way is not trusted");
        }

        // ---- the zip's name: one per actor

        [Test]
        public void TwoClientsOnOnePcNeverShareAZipName()
        {
            string a = MatchLogZipRule.ZipFileName("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", "Tudor", 1);
            string b = MatchLogZipRule.ZipFileName("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", "Tudor", 2);
            Assert.AreNotEqual(a, b);
            Assert.AreEqual("OverPower-log_2026-10-02_2130_Conquest-3v3v3_Tudors-lobby_1_Tudor.zip", a);
        }

        // ---- the saved box shows the path readably

        [Test]
        public void ALongPathMayWrapAfterEachSeparator()
        {
            string shown = MatchLogZipRule.WrappablePath(@"C:\Games\OverPower\Match logs\2026-10-02_2130_Conquest-3v3v3_Tudors-lobby");
            StringAssert.Contains("Match logs", shown);
            Assert.AreEqual(@"C:\Games\OverPower\Match logs\2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", shown.Replace("\u200B", ""));
            Assert.AreEqual(4, CountOf(shown, '\u200B'), "a break chance after every backslash");
        }

        [Test]
        public void TheOpenFolderButtonUsesAWellFormedFileAddress()
        {
            string url = MatchLogZipRule.FolderUrl("C:/Games/OverPower/Match logs/2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (2)");
            StringAssert.StartsWith("file:///C:/Games/OverPower/Match%20logs/", url, "a space in the path is percent-encoded");
            StringAssert.Contains("lobby%20(2)", url);
            Assert.IsNull(MatchLogZipRule.FolderUrl(""));
            Assert.IsNull(MatchLogZipRule.FolderUrl(null));
        }

        private static int CountOf(string text, char c)
        {
            int n = 0;
            foreach (char x in text) if (x == c) n++;
            return n;
        }
    }
}
