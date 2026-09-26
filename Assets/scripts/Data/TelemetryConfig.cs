using UnityEngine;
using UnityEngine.InputSystem;

namespace Overpower.Data
{
    /// <summary>
    /// The whole tuning surface for match telemetry (Task T2). One asset, one home, same reasoning
    /// as every other Data config: fields are [SerializeField] private with read-only properties, so
    /// nothing at runtime can quietly edit the shared asset instance instead of a per-player copy.
    ///
    /// Turning Enabled off (or deleting Assets/scripts/Telemetry entirely - see the design doc's
    /// Principle 1) is the whole kill switch: no other system reads or writes anything under this
    /// asset's control.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Telemetry Config", fileName = "TelemetryConfig")]
    public sealed class TelemetryConfig : ScriptableObject
    {
        [Header("On/off")]
        [Tooltip("Write a telemetry log for every match this client plays. Off = MatchTelemetry never " +
                 "opens a file and every Log call is dropped - the whole feature is inert, with no " +
                 "gameplay effect either way.")]
        [SerializeField] private bool enabled = true;
        public bool Enabled => enabled;

        [Header("Sampling")]
        [Tooltip("How often, in seconds, gold, position and loadout are sampled and flushed as one " +
                 "`sample` line (and per-weapon `shots`/`goldEarned`/`heal` totals). Shorter gives a " +
                 "smoother report timeline at the cost of a bigger log file.")]
        [SerializeField, Min(1f)] private float sampleIntervalSeconds = 5f;
        public float SampleIntervalSeconds => sampleIntervalSeconds;

        [Tooltip("On: each `sample` line includes this player's x/z position, which the report uses " +
                 "for death and position heatmaps. Off: positions are left out, for a playtest where " +
                 "that would be unwanted or unnecessary.")]
        [SerializeField] private bool recordPositions = true;
        public bool RecordPositions => recordPositions;

        [Header("Writing")]
        [Tooltip("How often, in seconds of unscaled time, buffered log lines are flushed to disk. " +
                 "Also flushed on leaving the room and on quitting, so a match's log is never more " +
                 "than this many seconds of play behind what actually happened.")]
        [SerializeField, Min(0.5f)] private float flushIntervalSeconds = 2f;
        public float FlushIntervalSeconds => flushIntervalSeconds;

        [Tooltip("Folder name under the platform's persistent data path that every match's own " +
                 "dated subfolder is created inside - e.g. .../Telemetry/2026-09-16_1730_ab12cd34/.")]
        [SerializeField] private string folderName = "Telemetry";
        public string FolderName => folderName;

        [Header("Console (playtest extras, 2026-09-26)")]
        [Tooltip("Write every player's own console output (errors, exceptions, warnings and asserts " +
                 "always; plain Debug.Log too once the match's file is open) into their own match log " +
                 "as `console` lines - see ConsoleTelemetry. Off = the listener never installs itself " +
                 "at all, the same kill-switch shape as Enabled above.")]
        [SerializeField] private bool recordConsole = true;
        public bool RecordConsole => recordConsole;

        [Tooltip("At most this many NEW console lines a second (a repeat within 1s folds into an " +
                 "existing line for free - see ConsoleLineRule - so this caps distinct messages, not " +
                 "the raw console rate). Past it, a message is only counted; the count is written as " +
                 "one summary `console` line once the next second begins.")]
        [SerializeField, Min(1)] private int consoleMaxLinesPerSecond = 50;
        public int ConsoleMaxLinesPerSecond => consoleMaxLinesPerSecond;

        [Tooltip("A console message's own text is cut to this many characters before it is written " +
                 "(the stack trace of an error/exception has its own, separate cut - see " +
                 "ConsoleLineRule). Keeps one runaway log line from bloating the whole match file.")]
        [SerializeField, Min(1)] private int consoleMessageMaxChars = 500;
        public int ConsoleMessageMaxChars => consoleMessageMaxChars;

        [Header("Bug mark key (playtest extras, 2026-09-26)")]
        [Tooltip("The key that marks 'a bug just happened' - a screenshot plus a note the reporter's " +
                 "next chat line supplies (see BugMarkerKey). Default B. In the EDITOR's own Play " +
                 "Mode, Ctrl+B is Unity's Build And Run shortcut (File menu) and may start a build - " +
                 "it never fires from a Player build, only from the Editor's own window - change this " +
                 "key here to test comfortably inside the Editor without touching code.")]
        [SerializeField] private Key bugMarkKey = Key.B;
        public Key BugMarkKey => bugMarkKey;

        [Tooltip("Require Ctrl held together with Bug Mark Key above. On by default, matching Unity's " +
                 "own Ctrl+B Build And Run binding in the Editor - turn this off only once Bug Mark " +
                 "Key above is changed to a key that needs no modifier.")]
        [SerializeField] private bool bugMarkNeedsCtrl = true;
        public bool BugMarkNeedsCtrl => bugMarkNeedsCtrl;
    }
}
