namespace Overpower.Telemetry
{
    /// <summary>
    /// The rolling state behind ConsoleTelemetry: what a caller should write for the next raw `Application.logMessageReceived`
    /// message. Plain C#: an enum stands in for LogType (ConsoleTelemetry maps the real one), so it and its tests never touch UnityEngine.
    ///
    /// Every message the file is already open for goes through, in this order:
    ///  1. SCRUB - every configured Photon App ID is replaced by "&lt;app id&gt;" in message and stack BEFORE anything is cut, so a
    ///     truncated id can never leak a bare prefix.
    ///  2. FOLD - the same (level, scrubbed) message within one second of the bucket's last touch is folded into it (count goes up,
    ///     no new line); a different message flushes the pending one as a finished Line and starts a fresh bucket.
    ///  3. CAP - a FRESH bucket (a fold is free) spends one of this second's admission budget (consoleMaxLinesPerSecond); past it
    ///     the message is only counted, and the count comes back as one dropped-summary integer when the next second's message
    ///     arrives (or at close, via Flush/TakeDroppedSummary).
    ///
    /// Before the file has opened ConsoleTelemetry uses AdmitBeforeOpen/FormatSingle instead: console lines must never crowd real
    /// gameplay lines out of MatchTelemetry's shared pending-lines queue, so only warnings, errors, exceptions and asserts are
    /// offered a slot, capped at PreOpenQueueCap; everything else refused is counted into the same dropped-count mechanism
    /// (TakePreOpenDroppedSummary).
    /// </summary>
    public sealed class ConsoleLineRule
    {
        public enum ConsoleLevel { Log, Warning, Error, Exception, Assert }

        /// <summary>How long a fold bucket keeps accepting repeats, measured from the LAST time it was touched (a rolling window).
        /// Also how stale a pending bucket must be before ConsoleTelemetry.Update flushes it with no new message: a lone error in a
        /// quiet log must reach disk within about a second.</summary>
        public const double FoldWindowSeconds = 1.0;

        /// <summary>Pre-open queue budget - its own number, separate from consoleMaxLinesPerSecond: retuning the in-match cap must
        /// not change how many startup warnings/errors survive before the file opens.</summary>
        private const int PreOpenQueueCap = 50;

        /// <summary>One finished console line for MatchTelemetry.Log, already scrubbed and cut. Count/FirstT/LastT differ from
        /// 1/t/t only when Submit folded repeats.</summary>
        public readonly struct Line
        {
            public readonly ConsoleLevel Level;
            public readonly string Message;
            public readonly string Stack; // null for Log/Warning.
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

        public bool HasPending => hasPending;

        /// <summary>Only meaningful while HasPending: ConsoleTelemetry.Update flushes early once FoldWindowSeconds has passed with
        /// nothing new to fold in.</summary>
        public double PendingLastT => pendingLastT;

        /// <param name="scrubTargets">Every Photon App ID to scrub - read once by the caller, never logged or stored elsewhere.
        /// Null or empty scrubs nothing.</param>
        public ConsoleLineRule(int messageMaxChars, int stackMaxChars, int maxLinesPerSecond, string[] scrubTargets = null)
        {
            this.messageMaxChars = System.Math.Max(1, messageMaxChars);
            this.stackMaxChars = System.Math.Max(0, stackMaxChars);
            this.maxLinesPerSecond = System.Math.Max(1, maxLinesPerSecond);
            this.scrubTargets = scrubTargets;
        }

        // ---------------------------------------------------------------- after the file is open

        /// <summary>Sets the two out params to what this call made ready to write (0, 1 or both): a finished fold line (the PREVIOUS
        /// bucket) and a dropped-count summary (the PREVIOUS second). Writes nothing itself; never throws on a null message/stack.</summary>
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

        /// <summary>Finalises the pending fold bucket (the match's last log line, which nothing arrives to flush) - call from
        /// MatchTelemetry.BeforeClose.</summary>
        public Line? Flush()
        {
            if (!hasPending)
                return null;

            Line line = MakePendingLine();
            hasPending = false;
            return line;
        }

        /// <summary>Forces the current second's dropped count out - call alongside Flush() at close, so a drop in the final partial
        /// second is not lost.</summary>
        public int TakeDroppedSummary()
        {
            int n = droppedThisSecond;
            droppedThisSecond = 0;
            return n;
        }

        private Line MakePendingLine() => new Line(pendingLevel, pendingMessage, pendingStack, pendingCount, pendingFirstT, pendingLastT);

        // ---------------------------------------------------------------- before the file is open

        /// <summary>The levels the pre-open queue accepts; a plain Log never, so a busy console cannot crowd a real
        /// join/leave/marker line out of MatchTelemetry's pending-lines cap before the file opens.</summary>
        public static bool ShouldQueueBeforeOpen(ConsoleLevel level) => level != ConsoleLevel.Log;

        /// <summary>Whether the NEXT pre-open message is queued (and formatted with FormatSingle); a refusal is counted for
        /// TakePreOpenDroppedSummary.</summary>
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

        /// <summary>Read once right after the file opens, written as one summary console line (0 = nothing was dropped).</summary>
        public int TakePreOpenDroppedSummary()
        {
            int n = preOpenDropped;
            preOpenDropped = 0;
            return n;
        }

        /// <summary>Call once per match (ConsoleTelemetry.HandleBeforeClose): the rule instance lives for the whole session (no scene
        /// reload between matches), so without this the budget was gone by the 2nd/3rd match and early warnings/errors before that
        /// match's file opened were silently dropped.</summary>
        public void ResetPreOpenBudget()
        {
            preOpenAdmitted = 0;
            preOpenDropped = 0;
        }

        /// <summary>Scrub + cut with no fold/cap bookkeeping: every pre-open line is its own Line with Count 1.</summary>
        public Line FormatSingle(ConsoleLevel level, string rawMessage, string rawStack, double now)
        {
            string message = Cut(Scrub(rawMessage ?? ""), messageMaxChars);
            bool wantsStack = level == ConsoleLevel.Error || level == ConsoleLevel.Exception;
            string stack = wantsStack ? Cut(Scrub(rawStack ?? ""), stackMaxChars) : null;
            return new Line(level, message, stack, 1, now, now);
        }

        // ---------------------------------------------------------------- shared

        // The replace loop lives in TelemetryScrub (shared with MatchTelemetry.LogChat); this is the console-specific "which targets"
        // plumbing.
        private string Scrub(string text) => TelemetryScrub.Apply(text, scrubTargets);

        private static string Cut(string text, int maxChars) =>
            text == null || text.Length <= maxChars ? text : text.Substring(0, maxChars);
    }
}
