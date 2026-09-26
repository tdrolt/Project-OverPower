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
            Assert.AreEqual("OverPower-log_2026-09-26_1730_Tudor.zip",
                MatchLogZipRule.ZipFileName("2026-09-26_1730", "Tudor"));
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
