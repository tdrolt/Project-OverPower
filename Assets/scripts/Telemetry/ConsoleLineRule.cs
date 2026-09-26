namespace Overpower.Telemetry
{
    /// <summary>
    /// Playtest extras P1 (2026-09-26): the pure rolling state behind ConsoleTelemetry - what a
    /// caller should write for the next raw `Application.logMessageReceived` message. Plain C#, no
    /// Unity engine dependency (an enum stands in for LogType, so this class - and its tests - never
    /// touch UnityEngine; ConsoleTelemetry maps the real LogType at the boundary), same reasoning as
    /// DotAccumulator/MarkLedger.
    ///
    /// Three things happen to every message the file is already open for, in this order:
    ///  1. SCRUB - every configured Photon App ID is replaced by "&lt;app id&gt;" in both the message
    ///     and the stack, BEFORE anything is cut - so a truncated id can never leak a bare prefix.
    ///  2. FOLD - the same (level, scrubbed) message landing within one second of the last time this
    ///     bucket was touched is folded into it (its count goes up, no new line); a different message
    ///     flushes whatever was pending as one finished Line and starts a fresh bucket.
    ///  3. CAP - starting a FRESH bucket (a fold is free - see above) spends one of this second's
    ///     admission budget (consoleMaxLinesPerSecond); past it, the message is only counted, and
    ///     that count comes back as one dropped-summary integer the moment the next second's message
    ///     arrives (or Flush/TakeDroppedSummary is called at close - see ConsoleTelemetry).
    ///
    /// Before the match's own file has opened, ConsoleTelemetry uses AdmitBeforeOpen/FormatSingle
    /// instead (the controller's own addendum, 2026-09-26): console lines must never crowd real
    /// gameplay lines out of MatchTelemetry's shared pending-lines queue, so only warnings, errors,
    /// exceptions and asserts are even offered a slot pre-open, capped at PreOpenQueueCap of them -
    /// everything else refused (a plain Log, or the 51st warning) is counted into the SAME
    /// dropped-count mechanism the per-second cap already uses (TakePreOpenDroppedSummary), per the
    /// controller's "fold it into the existing dropped-count mechanism if one exists".
    /// </summary>
    public sealed class ConsoleLineRule
    {
        public enum ConsoleLevel { Log, Warning, Error, Exception, Assert }

        /// <summary>How long a fold bucket keeps accepting repeats of the same message, measured from
        /// the LAST time it was touched (a rolling window, not a fixed one from the first occurrence).
        /// Also how stale a pending bucket must be before ConsoleTelemetry.Update proactively flushes
        /// it with no new message to trigger that - see HasPending/PendingLastT below: a lone error in
        /// an otherwise quiet log must reach disk within about a second, not sit in memory until the
        /// next unrelated log line (or the match's end) happens to flush it.</summary>
        public const double FoldWindowSeconds = 1.0;

        /// <summary>Pre-open queue budget - its own number, separate from consoleMaxLinesPerSecond: a
        /// designer retuning the in-match per-second cap for a noisy build must not also change how
        /// many startup warnings/errors survive the few frames before the file opens.</summary>
        private const int PreOpenQueueCap = 50;

        /// <summary>One finished console line, ready for a caller to write through MatchTelemetry.Log -
        /// already scrubbed and cut. Count/FirstT/LastT only differ from 1/t/t when Submit folded two
        /// or more repeats of the same message together.</summary>
        public readonly struct Line
        {
            public readonly ConsoleLevel Level;
            public readonly string Message;
            public readonly string Stack; // null for Log/Warning - see the class comment.
            public readonly int Count;
            public readonly double FirstT;
            public readonly double LastT;

            internal Line(ConsoleLevel level, string message, string stack, int count, double firstT, double lastT)
            {
                Level = level;
                Message = message;
                Stack = stack;
                Count = count;
                FirstT = firstT;
                LastT = lastT;
            }
        }

        private readonly int messageMaxChars;
        private readonly int stackMaxChars;
        private readonly int maxLinesPerSecond;
        private readonly string[] scrubTargets;

        // ---- fold bucket (the one line not yet flushed) ----
        private bool hasPending;
        private ConsoleLevel pendingLevel;
        private string pendingRawMessage; // scrubbed, NOT cut - the fold key, so truncation can never cause a false fold.
        private string pendingMessage;    // scrubbed AND cut - what actually gets written.
        private string pendingStack;
        private int pendingCount;
        private double pendingFirstT;
        private double pendingLastT;

        // ---- per-second cap (after the file is open) ----
        private long currentSecondBucket = long.MinValue;
        private int admittedThisSecond;
        private int droppedThisSecond;

        // ---- pre-open queue cap (before the file is open) ----
        private int preOpenAdmitted;
        private int preOpenDropped;

        /// <summary>True while a fold bucket is waiting for either a different message or a call to
        /// Flush()/Submit() to finish it - see PendingLastT.</summary>
        public bool HasPending => hasPending;

        /// <summary>The pending bucket's own last-touched time (only meaningful while HasPending is
        /// true) - ConsoleTelemetry.Update compares this against `now` and flushes early once
        /// FoldWindowSeconds has passed with nothing new to fold in, so a lone message reaches disk
        /// promptly instead of waiting on an unrelated later message or the match's end.</summary>
        public double PendingLastT => pendingLastT;

        /// <param name="scrubTargets">Every Photon App ID to scrub - read once by the caller (never
        /// logged, never stored anywhere else). Null or empty scrubs nothing.</param>
        public ConsoleLineRule(int messageMaxChars, int stackMaxChars, int maxLinesPerSecond, string[] scrubTargets = null)
        {
            this.messageMaxChars = System.Math.Max(1, messageMaxChars);
            this.stackMaxChars = System.Math.Max(0, stackMaxChars);
            this.maxLinesPerSecond = System.Math.Max(1, maxLinesPerSecond);
            this.scrubTargets = scrubTargets;
        }

        // ---------------------------------------------------------------- after the file is open

        /// <summary>Feeds one raw console message in. Sets the two out params to whatever this call
        /// made ready to write - 0, 1 or both of: a finished fold line (the PREVIOUS bucket, flushed
        /// because this message differs or the fold window passed) and a dropped-count summary (the
        /// PREVIOUS second, flushed because this message's `now` moved into a new one). Never writes
        /// anything itself, and never throws on a null message/stack.</summary>
        public void Submit(ConsoleLevel level, string rawMessage, string rawStack, double now,
            out Line? finishedLine, out int? droppedSummaryForPreviousSecond)
        {
            finishedLine = null;
            droppedSummaryForPreviousSecond = null;

            string scrubbedMessage = Scrub(rawMessage ?? "");
            bool wantsStack = level == ConsoleLevel.Error || level == ConsoleLevel.Exception;
            string scrubbedStack = wantsStack ? Scrub(rawStack ?? "") : null;

            long secondBucket = (long)System.Math.Floor(now);
            if (secondBucket != currentSecondBucket)
            {
                if (droppedThisSecond > 0)
                    droppedSummaryForPreviousSecond = droppedThisSecond;
                currentSecondBucket = secondBucket;
                admittedThisSecond = 0;
                droppedThisSecond = 0;
            }

            if (hasPending && pendingLevel == level && pendingRawMessage == scrubbedMessage &&
                now - pendingLastT <= FoldWindowSeconds)
            {
                pendingCount++;
                pendingLastT = now;
                return;
            }

            if (hasPending)
                finishedLine = MakePendingLine();

            if (admittedThisSecond >= maxLinesPerSecond)
            {
                hasPending = false;
                droppedThisSecond++;
                return;
            }

            admittedThisSecond++;
            hasPending = true;
            pendingLevel = level;
            pendingRawMessage = scrubbedMessage;
            pendingMessage = Cut(scrubbedMessage, messageMaxChars);
            pendingStack = wantsStack ? Cut(scrubbedStack, stackMaxChars) : null;
            pendingCount = 1;
            pendingFirstT = now;
            pendingLastT = now;
        }

        /// <summary>Finalises whatever fold bucket is still pending (e.g. the match's very last log
        /// line, which nothing ever arrived to flush) - call from MatchTelemetry.BeforeClose.</summary>
        public Line? Flush()
        {
            if (!hasPending)
                return null;

            Line line = MakePendingLine();
            hasPending = false;
            return line;
        }

        /// <summary>Forces the current second's dropped count out (0 without writing anything if
        /// there were none) - call alongside Flush() at close, so a drop in the final partial second
        /// is never silently lost.</summary>
        public int TakeDroppedSummary()
        {
            int n = droppedThisSecond;
            droppedThisSecond = 0;
            return n;
        }

        private Line MakePendingLine() => new Line(pendingLevel, pendingMessage, pendingStack, pendingCount, pendingFirstT, pendingLastT);

        // ---------------------------------------------------------------- before the file is open

        /// <summary>True for the four levels the pre-open queue accepts at all - a plain Log is
        /// never one of them, so a busy console can never crowd a real join/leave/marker line out of
        /// MatchTelemetry's own pending-lines cap before the file opens.</summary>
        public static bool ShouldQueueBeforeOpen(ConsoleLevel level) => level != ConsoleLevel.Log;

        /// <summary>Whether the NEXT pre-open message should be queued (and formatted with
        /// FormatSingle) - false either because its level is never queued pre-open, or because the
        /// PreOpenQueueCap is already spent; either way it is counted, ready for
        /// TakePreOpenDroppedSummary once the file opens.</summary>
        public bool AdmitBeforeOpen(ConsoleLevel level)
        {
            if (!ShouldQueueBeforeOpen(level) || preOpenAdmitted >= PreOpenQueueCap)
            {
                preOpenDropped++;
                return false;
            }

            preOpenAdmitted++;
            return true;
        }

        /// <summary>The dropped total from before the file opened - read once, right after the file
        /// opens, and written as one summary console line (0 means nothing was dropped).</summary>
        public int TakePreOpenDroppedSummary()
        {
            int n = preOpenDropped;
            preOpenDropped = 0;
            return n;
        }

        /// <summary>Scrub + cut, with no fold/cap bookkeeping at all - every pre-open line (there is
        /// never more than a handful) is written as its own Line with Count 1.</summary>
        public Line FormatSingle(ConsoleLevel level, string rawMessage, string rawStack, double now)
        {
            string message = Cut(Scrub(rawMessage ?? ""), messageMaxChars);
            bool wantsStack = level == ConsoleLevel.Error || level == ConsoleLevel.Exception;
            string stack = wantsStack ? Cut(Scrub(rawStack ?? ""), stackMaxChars) : null;
            return new Line(level, message, stack, 1, now, now);
        }

        // ---------------------------------------------------------------- shared

        private string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text) || scrubTargets == null)
                return text ?? "";

            foreach (string target in scrubTargets)
                if (!string.IsNullOrEmpty(target))
                    text = text.Replace(target, "<app id>");
            return text;
        }

        private static string Cut(string text, int maxChars) =>
            text == null || text.Length <= maxChars ? text : text.Substring(0, maxChars);
    }
}
