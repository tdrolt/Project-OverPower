using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Playtest extras P5 (2026-09-26): MatchLogZipRule's own file-selection and naming
    /// rules - see MatchLogZip for the real IO built on top of these.</summary>
    public class MatchLogZipRuleTests
    {
        [Test]
        public void SelectsOnlyThisActorsOwnJsonlAndBugScreenshots()
        {
            var files = new List<string>
            {
                "1_Tudor.jsonl", "2_Guest.jsonl",
                "bug_1_12.5.png", "bug_2_9.0.png",
                "OverPower-log_2026-09-26_1730_Tudor.zip", // an earlier zip - never re-zipped into itself
            };

            List<string> own = MatchLogZipRule.SelectOwnFiles(files, actor: 1);

            CollectionAssert.AreEquivalent(new[] { "1_Tudor.jsonl", "bug_1_12.5.png" }, own);
        }

        [Test]
        public void NeverMatchesAnotherActorWhoseNumberStartsWithTheSameDigits()
        {
            // Actor 1 must not pick up actor 10's files just because "1" is a string prefix of "10".
            var files = new List<string> { "10_Someone.jsonl", "bug_10_5.0.png" };
            List<string> own = MatchLogZipRule.SelectOwnFiles(files, actor: 1);
            Assert.IsEmpty(own);
        }

        [Test]
        public void IgnoresFilesThatAreNeitherJsonlNorBugScreenshots()
        {
            var files = new List<string> { "1_Tudor.jsonl.meta", "notes.txt", "1_Tudor.png" };
            List<string> own = MatchLogZipRule.SelectOwnFiles(files, actor: 1);
            Assert.IsEmpty(own);
        }

        [Test]
        public void NullFileListSelectsNothingRatherThanThrowing()
        {
            Assert.IsEmpty(MatchLogZipRule.SelectOwnFiles(null, actor: 1));
        }

        [Test]
        public void ZipFileNameFollowsTheBriefsPattern()
        {
            // The first argument is the match FOLDER's own name (MatchTelemetry.ResolveMatchFolder:
            // "<dateStamp>_<matchId8>"), not a wall-clock read taken at zip time (2026-09-26 fix) - it
            // never changes for the life of a match, which is exactly what makes every zip of the same
            // match come out under the same name.
            Assert.AreEqual("OverPower-log_2026-09-26_0745_1361e7bf_Tudor.zip",
                MatchLogZipRule.ZipFileName("2026-09-26_0745_1361e7bf", "Tudor"));
        }

        // Playtest extras P6 follow-up (2026-09-26, the zip-name-fix brief): the two-client check found
        // a real Player quit could mint a SECOND zip under a different name, because
        // MatchTelemetry.OnLeftRoom (fired by GameQuit.Quit's own PhotonNetwork.Disconnect) can clear
        // CurrentFolder before MatchLogZip's own quit-time re-zip attempt runs. ResolveZipFolder is the
        // pure decision behind the fix: prefer the live folder, and only fall back to the last one this
        // client actually zipped into (remembered from MatchLogZip.HandleBeforeClose, which runs while
        // CurrentFolder is still valid - see that method's own comment) when the live one has gone empty.

        [Test]
        public void ResolveZipFolderPrefersTheLiveFolder()
        {
            Assert.AreEqual("C:\\Telemetry\\2026-09-26_0745_1361e7bf",
                MatchLogZipRule.ResolveZipFolder("C:\\Telemetry\\2026-09-26_0745_1361e7bf", "C:\\Telemetry\\stale"));
        }

        [Test]
        public void ResolveZipFolderFallsBackWhenTheLiveFolderWentEmpty()
        {
            Assert.AreEqual("C:\\Telemetry\\2026-09-26_0745_1361e7bf",
                MatchLogZipRule.ResolveZipFolder(null, "C:\\Telemetry\\2026-09-26_0745_1361e7bf"));
            Assert.AreEqual("C:\\Telemetry\\2026-09-26_0745_1361e7bf",
                MatchLogZipRule.ResolveZipFolder("", "C:\\Telemetry\\2026-09-26_0745_1361e7bf"));
        }

        [Test]
        public void ResolveZipFolderIsEmptyWhenNeitherIsKnown()
        {
            Assert.IsTrue(string.IsNullOrEmpty(MatchLogZipRule.ResolveZipFolder(null, null)));
        }

        // Playtest extras P6 follow-up (item 3): "handle both orders" - a quit-time zip must still
        // attempt to zip once this client's file has ever opened, whether or not MatchTelemetry's
        // writer happens to have already closed by the time OnApplicationQuit reaches MatchLogZip.

        [Test]
        public void AttemptsToZipOnceTheFolderHasEverBeenSet()
        {
            // Stands in for MatchTelemetry.CurrentFolder AFTER the writer has closed on quit (Order A,
            // MatchTelemetry.OnApplicationQuit ran first) - CurrentFolder is never cleared by that path,
            // only by OnLeftRoom, so this must still say "go ahead and zip".
            Assert.IsTrue(MatchLogZipRule.ShouldAttemptZip("C:\\Telemetry\\2026-09-26_0643_c25d0fac"));
        }

        [Test]
        public void NeverAttemptsToZipBeforeAnyFileEverOpened()
        {
            Assert.IsFalse(MatchLogZipRule.ShouldAttemptZip(null));
            Assert.IsFalse(MatchLogZipRule.ShouldAttemptZip(""));
        }
    }
}
