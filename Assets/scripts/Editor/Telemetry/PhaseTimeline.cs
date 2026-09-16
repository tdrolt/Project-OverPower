using Newtonsoft.Json.Linq;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T7: turns a log's `phase` events into the two (or one) windows the aggregator
    /// clips everything against. Pure - no IO, edit-mode tested directly (PhaseTimelineTests).
    ///
    /// The phase changes ONLY on an elimination (Tudor, corrected the same day the spec was written -
    /// see the design doc, Part 3): Phase 1 is `[0, tPhase2)` from match start until the first `phase`
    /// event whose own number is >= 2; Phase 2, when one exists, is `[tPhase2, matchLength]`. A log
    /// with no such event (every log before Task 2.7 ships, and any two-team test that never eliminates
    /// a team) is entirely Phase 1 - Phase2 stays null and Phase1 IS the whole-match window.
    ///
    /// MatchTelemetry's own `phase` 1 anchor (logged once, the moment the master claims the match
    /// identity) is deliberately NOT a transition - only phase numbers >= 2 count, so that anchor never
    /// looks like an elimination.</summary>
    public sealed class PhaseTimeline
    {
        public readonly double MatchLength;

        /// <summary>Null when no `phase` event with a number >= 2 exists in the log.</summary>
        public readonly double? TransitionSeconds;

        public readonly TimeWindow WholeMatch;
        public readonly TimeWindow Phase1;

        /// <summary>Null when <see cref="TransitionSeconds"/> is null - see <see cref="HasPhase2"/>.</summary>
        public readonly TimeWindow Phase2;

        public bool HasPhase2 => Phase2 != null;

        private PhaseTimeline(double matchLength, double? transitionSeconds)
        {
            MatchLength = matchLength;
            TransitionSeconds = transitionSeconds;
            WholeMatch = new TimeWindow(0, matchLength, true);

            if (transitionSeconds.HasValue)
            {
                Phase1 = new TimeWindow(0, transitionSeconds.Value, false);
                Phase2 = new TimeWindow(transitionSeconds.Value, matchLength, true);
            }
            else
            {
                Phase1 = WholeMatch;
                Phase2 = null;
            }
        }

        public static PhaseTimeline From(TelemetryLog log)
        {
            double matchLength = 0;
            double? transition = null;

            if (log != null)
            {
                foreach (TelemetryEvent e in log.Events)
                {
                    if (e.T > matchLength) matchLength = e.T;

                    // First (log.Events is t-sorted) `phase` event whose own number is >= 2. A
                    // negative t (the -1 "clock not known yet" sentinel) can never be a real
                    // transition instant - ignored rather than producing an inverted [0, -1) window.
                    if (!transition.HasValue && e.Name == TelemetryKeys.Phase && e.T >= 0)
                    {
                        int num = ReadPhaseNumber(e.Data);
                        if (num >= 2) transition = e.T;
                    }
                }
            }

            return new PhaseTimeline(matchLength, transition);
        }

        private static int ReadPhaseNumber(JObject data)
        {
            JToken token = data?[TelemetryKeys.PhaseNumber];
            return (token != null && token.Type != JTokenType.Null) ? token.ToObject<int>() : 1;
        }
    }
}
