using UnityEngine;

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
    }
}
