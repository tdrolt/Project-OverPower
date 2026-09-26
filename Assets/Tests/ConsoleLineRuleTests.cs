using NUnit.Framework;
using Overpower.Telemetry;
using static Overpower.Telemetry.ConsoleLineRule;

namespace Overpower.Tests
{
    /// <summary>Playtest extras P1 (2026-09-26): the pure rule behind ConsoleTelemetry. No Unity
    /// engine dependency - see ConsoleLineRule's own class comment.</summary>
    public class ConsoleLineRuleTests
    {
        private const string FakeAppId = "TEST-APP-ID-1234";

        [Test]
        public void ALongMessageIsCutToTheLimit()
        {
            var rule = new ConsoleLineRule(messageMaxChars: 10, stackMaxChars: 1000, maxLinesPerSecond: 50);
            rule.Submit(ConsoleLevel.Log, "0123456789ABCDEF", null, 1.0, out _, out _);
            ConsoleLineRule.Line line = rule.Flush().Value;
            Assert.AreEqual("0123456789", line.Message);
        }

        [Test]
        public void ARepeatWithinOneSecondFoldsInsteadOfStartingANewLine()
        {
            var rule = new ConsoleLineRule(500, 1000, 50);
            rule.Submit(ConsoleLevel.Warning, "low ammo", null, 1.0, out Line? finished1, out _);
            rule.Submit(ConsoleLevel.Warning, "low ammo", null, 1.5, out Line? finished2, out _);

            Assert.IsNull(finished1, "the first occurrence just opens the bucket - nothing to flush yet");
            Assert.IsNull(finished2, "a repeat within 1s folds - no new line");

            Line pending = rule.Flush().Value;
            Assert.AreEqual(2, pending.Count);
            Assert.AreEqual(1.0, pending.FirstT);
            Assert.AreEqual(1.5, pending.LastT);
        }

        [Test]
        public void ADifferentMessageStartsANewLineAndFlushesTheOldOne()
        {
            var rule = new ConsoleLineRule(500, 1000, 50);
            rule.Submit(ConsoleLevel.Log, "first", null, 1.0, out _, out _);
            rule.Submit(ConsoleLevel.Log, "second", null, 1.1, out Line? finished, out _);

            Assert.IsTrue(finished.HasValue, "a different message must flush whatever was pending");
            Assert.AreEqual("first", finished.Value.Message);
            Assert.AreEqual(1, finished.Value.Count);

            Line pending = rule.Flush().Value;
            Assert.AreEqual("second", pending.Message);
        }

        [Test]
        public void ARepeatPastTheFoldWindowStartsAFreshBucketInsteadOfFolding()
        {
            var rule = new ConsoleLineRule(500, 1000, 50);
            rule.Submit(ConsoleLevel.Log, "tick", null, 1.0, out _, out _);
            rule.Submit(ConsoleLevel.Log, "tick", null, 3.0, out Line? finished, out _);

            Assert.IsTrue(finished.HasValue, "more than 1s since the last occurrence - the old bucket flushes");
            Assert.AreEqual(1, finished.Value.Count);

            Line pending = rule.Flush().Value;
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual(3.0, pending.FirstT);
        }

        [Test]
        public void PastThePerSecondCapLinesAreCountedAsDroppedAndOneSummaryLineCarriesTheCount()
        {
            var rule = new ConsoleLineRule(500, 1000, maxLinesPerSecond: 2);

            // Three DISTINCT messages in second 1: two are admitted (each flushes the previous
            // bucket first), the third is over the cap of 2 and only counted.
            rule.Submit(ConsoleLevel.Log, "a", null, 1.0, out _, out _);
            rule.Submit(ConsoleLevel.Log, "b", null, 1.1, out Line? flushedA, out int? dropSoFar1);
            rule.Submit(ConsoleLevel.Log, "c", null, 1.2, out Line? flushedB, out int? dropSoFar2);

            Assert.AreEqual("a", flushedA.Value.Message);
            Assert.AreEqual("b", flushedB.Value.Message);
            Assert.IsNull(dropSoFar1, "still inside second 1 - nothing to summarise yet");
            Assert.IsNull(dropSoFar2, "still inside second 1 - nothing to summarise yet");

            // Second 2 begins: the rule must hand back exactly one dropped-count summary for second 1 (just "c").
            rule.Submit(ConsoleLevel.Log, "d", null, 2.0, out Line? flushedNone, out int? droppedSummary);

            Assert.IsNull(flushedNone, "c was dropped outright - it never became a pending bucket, so there is nothing to flush for it");
            Assert.AreEqual(1, droppedSummary, "exactly one message (c) was dropped by the per-second cap");
        }

        [Test]
        public void TakeDroppedSummaryForcesOutTheCurrentSecondsCountAtClose()
        {
            var rule = new ConsoleLineRule(500, 1000, maxLinesPerSecond: 1);
            rule.Submit(ConsoleLevel.Log, "a", null, 1.0, out _, out _);
            rule.Submit(ConsoleLevel.Log, "b", null, 1.1, out _, out _); // dropped - cap is 1

            Assert.AreEqual(1, rule.TakeDroppedSummary());
            Assert.AreEqual(0, rule.TakeDroppedSummary(), "reading it clears it");
        }

        [Test]
        public void TheScrubReplacesTheFakeAppIdEverywhereAndNeverEmitsIt()
        {
            var rule = new ConsoleLineRule(500, 1000, 50, scrubTargets: new[] { FakeAppId });
            string message = $"connecting with {FakeAppId} to region eu";
            string stack = $"at Foo.Bar({FakeAppId})";

            rule.Submit(ConsoleLevel.Error, message, stack, 1.0, out _, out _);
            Line line = rule.Flush().Value;

            Assert.IsFalse(line.Message.Contains(FakeAppId));
            Assert.IsFalse(line.Stack.Contains(FakeAppId));
            StringAssert.Contains("<app id>", line.Message);
            StringAssert.Contains("<app id>", line.Stack);
        }

        [Test]
        public void TheScrubAlsoAppliesToThePreOpenSingleLineFormatter()
        {
            var rule = new ConsoleLineRule(500, 1000, 50, scrubTargets: new[] { FakeAppId });
            Line line = rule.FormatSingle(ConsoleLevel.Warning, $"id {FakeAppId}", null, 0.0);
            Assert.IsFalse(line.Message.Contains(FakeAppId));
        }

        [Test]
        public void ErrorAndExceptionKeepACutStackLogAndWarningDoNot()
        {
            var rule = new ConsoleLineRule(500, stackMaxChars: 5, maxLinesPerSecond: 50);

            rule.Submit(ConsoleLevel.Log, "l", "abcdefgh", 1.0, out _, out _);
            Assert.IsNull(rule.Flush().Value.Stack, "log never keeps a stack");

            rule.Submit(ConsoleLevel.Warning, "w", "abcdefgh", 2.0, out _, out _);
            Assert.IsNull(rule.Flush().Value.Stack, "warning never keeps a stack");

            rule.Submit(ConsoleLevel.Error, "e", "abcdefgh", 3.0, out _, out _);
            Assert.AreEqual("abcde", rule.Flush().Value.Stack, "error keeps a stack, cut to the limit");

            rule.Submit(ConsoleLevel.Exception, "x", "abcdefgh", 4.0, out _, out _);
            Assert.AreEqual("abcde", rule.Flush().Value.Stack, "exception keeps a stack, cut to the limit");
        }

        [Test]
        public void FlushOnAnEmptyRuleReturnsNull()
        {
            Assert.IsNull(new ConsoleLineRule(500, 1000, 50).Flush());
        }

        [Test]
        public void HasPendingAndPendingLastTLetACallerNoticeAStaleBucketWithNoNewMessage()
        {
            // ConsoleTelemetry.Update polls exactly this pair every frame - a lone message with
            // nothing arriving afterward to fold into or differ from must still reach disk promptly
            // (see ConsoleTelemetry's own Update comment), not wait on the match's end.
            var rule = new ConsoleLineRule(500, 1000, 50);
            Assert.IsFalse(rule.HasPending);

            rule.Submit(ConsoleLevel.Warning, "lone", null, 5.0, out _, out _);
            Assert.IsTrue(rule.HasPending);
            Assert.AreEqual(5.0, rule.PendingLastT);

            rule.Submit(ConsoleLevel.Warning, "lone", null, 5.4, out _, out _); // a fold within the window
            Assert.AreEqual(5.4, rule.PendingLastT, "folding updates the last-touched time too");
        }

        // ---- pre-open queue (the controller's addendum, 2026-09-26): console must never crowd
        // gameplay lines out of MatchTelemetry's own pending-lines cap before the file opens. ----

        [Test]
        public void OnlyWarningsErrorsExceptionsAndAssertsAreEverQueuedBeforeOpen()
        {
            Assert.IsFalse(ConsoleLineRule.ShouldQueueBeforeOpen(ConsoleLevel.Log));
            Assert.IsTrue(ConsoleLineRule.ShouldQueueBeforeOpen(ConsoleLevel.Warning));
            Assert.IsTrue(ConsoleLineRule.ShouldQueueBeforeOpen(ConsoleLevel.Error));
            Assert.IsTrue(ConsoleLineRule.ShouldQueueBeforeOpen(ConsoleLevel.Exception));
            Assert.IsTrue(ConsoleLineRule.ShouldQueueBeforeOpen(ConsoleLevel.Assert));
        }

        [Test]
        public void APlainLogIsNeverAdmittedBeforeOpenAndIsCounted()
        {
            var rule = new ConsoleLineRule(500, 1000, 50);
            Assert.IsFalse(rule.AdmitBeforeOpen(ConsoleLevel.Log));
            Assert.AreEqual(1, rule.TakePreOpenDroppedSummary());
        }

        [Test]
        public void PastFiftyQueuedWarningsTheRestAreRefusedAndCounted()
        {
            var rule = new ConsoleLineRule(500, 1000, 50);
            int admitted = 0;
            for (int i = 0; i < 60; i++)
            {
                if (rule.AdmitBeforeOpen(ConsoleLevel.Warning))
                    admitted++;
            }

            Assert.AreEqual(50, admitted);
            Assert.AreEqual(10, rule.TakePreOpenDroppedSummary());
        }

        [Test]
        public void TakingThePreOpenDroppedSummaryClearsIt()
        {
            var rule = new ConsoleLineRule(500, 1000, 50);
            rule.AdmitBeforeOpen(ConsoleLevel.Log);
            Assert.AreEqual(1, rule.TakePreOpenDroppedSummary());
            Assert.AreEqual(0, rule.TakePreOpenDroppedSummary());
        }
    }
}
