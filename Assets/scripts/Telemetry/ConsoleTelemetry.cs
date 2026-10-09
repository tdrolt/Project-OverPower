using UnityEngine;
using Overpower.Data;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Every player's own console output goes into their own match log, through the pure <see cref="ConsoleLineRule"/> (cut, fold,
    /// per-second cap, scrub). Lives on MatchTelemetry's GameObject (the BuildingManager object): subscribes to
    /// Application.logMessageReceived in OnEnable, unsubscribes in OnDisable (Unity calls it before OnApplicationQuit/OnDestroy), and
    /// uses MatchTelemetry.BeforeClose for one last flush of a pending fold bucket before the writer closes.
    ///
    /// DebugOverlay has its own independent logMessageReceived subscription (the on-screen viewer); Unity gives no ordering
    /// between the two and neither interferes.
    /// </summary>
    [DisallowMultipleComponent]
    public class ConsoleTelemetry : MonoBehaviour
    {
        [Tooltip("The same asset MatchTelemetry reads - recordConsole is this component's own on/off " +
                 "switch (on top of Enabled), and the per-second cap/message length live here too.")]
        [SerializeField] private TelemetryConfig config;

        // Not a TelemetryConfig field (the designer tunes only the message length and the per-second cap); a constant like
        // MatchTelemetry.MaxPendingLines.
        private const int StackMaxChars = 1000;

        private ConsoleLineRule rule;
        private bool subscribed;

        // Reentrancy guard: Application.logMessageReceived is re-entrant if a handler logs anything. This component logs nothing in
        // OnLog today, but without the guard a future edit's log would recurse into OnLog while it is still running.
        private bool inLog;

        // Tracks the file-open transition: logMessageReceived is event-driven and may not fire for a long time after the file opens,
        // so the pre-open dropped-count summary needs its own one chance to be written.
        private bool wasOpen;

        private void OnEnable()
        {
            if (config == null || !config.Enabled || !config.RecordConsole)
                return;

            rule = new ConsoleLineRule(config.ConsoleMessageMaxChars, StackMaxChars, config.ConsoleMaxLinesPerSecond, ScrubTargets());

            if (MatchTelemetry.Instance != null)
                MatchTelemetry.Instance.BeforeClose += HandleBeforeClose;

            Application.logMessageReceived += OnLog;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed)
                return;

            Application.logMessageReceived -= OnLog;
            if (MatchTelemetry.Instance != null)
                MatchTelemetry.Instance.BeforeClose -= HandleBeforeClose;
            subscribed = false;
        }

        private void Update()
        {
            if (rule == null)
                return;

            bool isOpen = MatchTelemetry.Instance != null && MatchTelemetry.Instance.IsRecording;
            if (isOpen && !wasOpen)
                FlushPreOpenDroppedSummary();
            wasOpen = isOpen;

            // A lone message would otherwise sit in the pending bucket until BeforeClose. logMessageReceived only fires on a NEW
            // message, so only a poll notices the fold window has run out (ConsoleLineRule.FoldWindowSeconds).
            if (isOpen && rule.HasPending)
            {
                double now = MatchTelemetry.Instance.Now;
                if (now - rule.PendingLastT > ConsoleLineRule.FoldWindowSeconds)
                {
                    ConsoleLineRule.Line? stale = rule.Flush();
                    if (stale.HasValue)
                        WriteLine(stale.Value);
                }
            }
        }

        /// <summary>The shared TelemetryScrub.AppIdTargets(), the same targets MatchTelemetry.LogChat reads. Never stored elsewhere,
        /// logged or printed.</summary>
        private static string[] ScrubTargets() => TelemetryScrub.AppIdTargets();

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (rule == null || inLog)
                return;

            inLog = true;
            try
            {
                ConsoleLineRule.ConsoleLevel level = LevelFor(type);
                bool fileOpen = MatchTelemetry.Instance != null && MatchTelemetry.Instance.IsRecording;
                double now = MatchTelemetry.Instance != null ? MatchTelemetry.Instance.Now : -1.0;

                if (!fileOpen)
                {
                    if (rule.AdmitBeforeOpen(level))
                        WriteLine(rule.FormatSingle(level, message, stackTrace, now));
                    return;
                }

                rule.Submit(level, message, stackTrace, now, out ConsoleLineRule.Line? finished, out int? droppedSummary);
                if (droppedSummary.HasValue && droppedSummary.Value > 0)
                    MatchTelemetry.Instance.LogConsoleDropped(droppedSummary.Value);
                if (finished.HasValue)
                    WriteLine(finished.Value);
            }
            catch
            {
                // Swallowed on purpose, and nothing here may log: this runs INSIDE Application.logMessageReceived, so a Debug.Log*
                // call would re-enter the event (the inLog guard stops the handling, not the recursive dispatch). Turns console
                // recording off for the rest of the session: a listener that threw once is not trusted to stop throwing.
                rule = null;
                Application.logMessageReceived -= OnLog;
                if (MatchTelemetry.Instance != null)
                    MatchTelemetry.Instance.BeforeClose -= HandleBeforeClose;
                subscribed = false;
            }
            finally
            {
                inLog = false;
            }
        }

        private void FlushPreOpenDroppedSummary()
        {
            int dropped = rule.TakePreOpenDroppedSummary();
            if (dropped > 0 && MatchTelemetry.Instance != null)
                MatchTelemetry.Instance.LogConsoleDropped(dropped);
        }

        /// <summary>MatchTelemetry.BeforeClose: the pending fold bucket (the match's last console line) and this second's dropped
        /// count get one last write before the writer closes.</summary>
        private void HandleBeforeClose()
        {
            if (rule == null || MatchTelemetry.Instance == null)
                return;

            ConsoleLineRule.Line? pending = rule.Flush();
            if (pending.HasValue)
                WriteLine(pending.Value);

            int dropped = rule.TakeDroppedSummary();
            if (dropped > 0)
                MatchTelemetry.Instance.LogConsoleDropped(dropped);

            // This component and its rule live for the whole session, and BeforeClose fires at every match's end (OnLeftRoom) as
            // well as at shutdown, so resetting here gives the NEXT match its own fresh PreOpenQueueCap.
            rule.ResetPreOpenBudget();
        }

        private static void WriteLine(ConsoleLineRule.Line line) =>
            MatchTelemetry.Instance.LogConsole(NameFor(line.Level), line.Message, line.Stack, line.Count, line.FirstT, line.LastT);

        private static ConsoleLineRule.ConsoleLevel LevelFor(LogType type) => type switch
        {
            LogType.Warning => ConsoleLineRule.ConsoleLevel.Warning,
            LogType.Error => ConsoleLineRule.ConsoleLevel.Error,
            LogType.Exception => ConsoleLineRule.ConsoleLevel.Exception,
            LogType.Assert => ConsoleLineRule.ConsoleLevel.Assert,
            _ => ConsoleLineRule.ConsoleLevel.Log,
        };

        private static string NameFor(ConsoleLineRule.ConsoleLevel level) => level switch
        {
            ConsoleLineRule.ConsoleLevel.Warning => "warning",
            ConsoleLineRule.ConsoleLevel.Error => "error",
            ConsoleLineRule.ConsoleLevel.Exception => "exception",
            ConsoleLineRule.ConsoleLevel.Assert => "assert",
            _ => "log",
        };
    }
}
