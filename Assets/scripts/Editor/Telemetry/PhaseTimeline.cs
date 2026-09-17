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
    /// looks like an elimination.
    ///
    /// Review fix (item 9): a `phase` event is 2.7's own job to raise (see the plan's "changes from
    /// the spec"), so a log with a real elimination but a MatchDirector that hasn't landed yet (or a
    /// bug in it) would otherwise stay entirely Phase 1 despite an elimination clearly having
    /// happened. When no `phase` >= 2 event exists but at least one `elimination` event does, the
    /// EARLIEST elimination's own t becomes the transition instead - see
    /// <see cref="UsedEliminationFallback"/>, which the aggregator surfaces as a header warning.</summary>
    public sealed class PhaseTimeline
    {
        public readonly double MatchLength;

        /// <summary>Null when no `phase` event with a number >= 2, and no `elimination` event
        /// either, exists in the log.</summary>
        public readonly double? TransitionSeconds;

        /// <summary>True when <see cref="TransitionSeconds"/> came from an `elimination` event
        /// because no `phase` >= 2 event was found at all - see the class comment's review-fix
        /// paragraph.</summary>
        public readonly bool UsedEliminationFallback;

        public readonly TimeWindow WholeMatch;
        public readonly TimeWindow Phase1;

        /// <summary>Null when <see cref="TransitionSeconds"/> is null - see <see cref="HasPhase2"/>.</summary>
        public readonly TimeWindow Phase2;

        public bool HasPhase2 => Phase2 != null;

        private PhaseTimeline(double matchLength, double? transitionSeconds, bool usedEliminationFallback)
        {
            MatchLength = matchLength;
            TransitionSeconds = transitionSeconds;
            UsedEliminationFallback = usedEliminationFallback;
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
            double? phaseTransition = null;
            double? earliestElimination = null;

            if (log != null)
            {
                foreach (TelemetryEvent e in log.Events)
                {
                    if (e.T > matchLength) matchLength = e.T;

                    // First (log.Events is t-sorted) `phase` event whose own number is >= 2. A
                    // negative t (the -1 "clock not known yet" sentinel) can never be a real
                    // transition instant - ignored rather than producing an inverted [0, -1) window.
                    if (!phaseTransition.HasValue && e.Name == TelemetryKeys.Phase && e.T >= 0)
                    {
                        int num = ReadPhaseNumber(e.Data);
                        if (num >= 2) phaseTransition = e.T;
                    }
                    else if (e.Name == TelemetryKeys.Elimination)
                    {
                        // Review fix (item 9): unlike a `phase` event's own number, an elimination's
                        // usefulness as a fallback transition doesn't depend on a clean positive t -
                        // a negative one still means "a team was eliminated essentially at match
                        // start", so it maps to 0 rather than being discarded outright.
                        double t = e.T < 0 ? 0 : e.T;
                        if (!earliestElimination.HasValue || t < earliestElimination.Value)
                            earliestElimination = t;
                    }
                }
            }

            bool usedFallback = !phaseTransition.HasValue && earliestElimination.HasValue;
            double? transition = phaseTransition ?? earliestElimination;

            return new PhaseTimeline(matchLength, transition, usedFallback);
        }

        private static int ReadPhaseNumber(JObject data)
        {
            JToken token = data?[TelemetryKeys.PhaseNumber];
            return (token != null && token.Type != JTokenType.Null) ? token.ToObject<int>() : 1;
        }
    }
}
