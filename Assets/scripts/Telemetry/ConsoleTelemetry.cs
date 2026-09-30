using UnityEngine;
using Overpower.Data;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Playtest extras P1 (2026-09-26): every player's own console output goes into their own match
    /// log, through the pure <see cref="ConsoleLineRule"/> (cut, fold, per-second cap, scrub). Lives
    /// next to MatchTelemetry (same GameObject - the BuildingManager object) and follows its exact
    /// enable/disable shape: subscribes to Application.logMessageReceived in OnEnable, unsubscribes
    /// in OnDisable (which also covers OnApplicationQuit/OnDestroy - Unity calls OnDisable before
    /// either), and asks MatchTelemetry.BeforeClose for one last chance to flush a still-pending fold
    /// bucket before the writer closes, the same hook PlayerTelemetry's own accumulators use.
    ///
    /// Every player already has DebugOverlay's OWN Application.logMessageReceived subscription (an
    /// on-screen viewer, F1/F2) - the two are independent listeners on the same Unity event, which
    /// Unity supports with no ordering guarantee between them and no interference either way.
    /// </summary>
    [DisallowMultipleComponent]
    public class ConsoleTelemetry : MonoBehaviour
    {
        [Tooltip("The same asset MatchTelemetry reads - recordConsole is this component's own on/off " +
                 "switch (on top of Enabled), and the per-second cap/message length live here too.")]
        [SerializeField] private TelemetryConfig config;

        // The stack ConsoleLineRule needs a hard cap on too, even though it is not designer tuning
        // (the brief lists only the message length and the per-second cap as TelemetryConfig fields) -
        // matching MaxPendingLines' own "a constant, not an asset field" treatment on MatchTelemetry.
        private const int StackMaxChars = 1000;

        private ConsoleLineRule rule;
        private bool subscribed;

        // Reentrancy guard (the brief's own P1 wording: "a guard stops the listener reacting to
        // anything it logs itself") - this component never calls Debug.Log/LogWarning/LogError from
        // inside OnLog today, but a future edit easily could, and Application.logMessageReceived is
        // re-entrant if a handler logs anything at all: without this, that log would recurse back
        // into OnLog while it is still running.
        private bool inLog;

        // Tracks the file-open transition (Application.logMessageReceived is event-driven and may
        // not fire again for a long time after the file actually opens - see FlushPreOpenDroppedSummary's
        // own comment) so the pre-open dropped-count summary gets exactly one chance to be written.
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

            // A lone message with nothing after it to fold into, or to differ from, would otherwise
            // sit in the pending bucket until BeforeClose - reaching disk only when the match ends.
            // Application.logMessageReceived only fires on a NEW message, so nothing but a poll can
            // notice "the fold window has simply run out" - see ConsoleLineRule.FoldWindowSeconds.
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

        /// <summary>Step 0 review fix (b): now the shared TelemetryScrub.AppIdTargets() - MatchTelemetry.
        /// LogChat reads the exact same targets for chat text. Never stored anywhere else, never
        /// logged, never printed.</summary>
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
                // Step 0 review fix (c): swallowed on purpose, and nothing here may log anything -
                // this runs INSIDE Application.logMessageReceived, so a Debug.Log*/LogError call
                // would immediately re-enter this same event (the inLog guard would only stop it
                // from doing anything, not stop the recursive dispatch itself). Turns console
                // recording off for the rest of the session - a listener that throws once has no
                // reason to believe it won't throw on every following line, and disabling beats
                // repeating the same silent failure (and cost) on every message from here on.
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

        /// <summary>MatchTelemetry.BeforeClose: the fold bucket still pending (if any - the match's
        /// very last console line, which nothing ever arrived to flush) and this second's own dropped
        /// count both get one last chance to be written before the writer closes.</summary>
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

            // Step 0 review fix (a): this component (and its ConsoleLineRule) lives for the whole
            // session, not just one match - BeforeClose fires at every match's end (OnLeftRoom) as
            // well as at real shutdown, so resetting here always leaves the NEXT match (if there is
            // one) with its own fresh PreOpenQueueCap rather than whatever this match had already
            // spent by the time its file opened.
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
