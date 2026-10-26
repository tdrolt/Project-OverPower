using Newtonsoft.Json.Linq;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Turns a log's `phase` events into the windows the aggregator clips everything against. Pure, no IO (PhaseTimelineTests).
    ///
    /// Two passes, chosen by whether the log carries a `phase` event with num == 0 (the warm-up anchor MatchTelemetry logs once when
    /// the master claims the match identity, whether or not the match ever goes live):
    /// - <b>LEGACY</b> (no num == 0 event): <see cref="FromLegacy"/>. No warm-up (<see cref="HasWarmup"/> false, <see cref="Warmup"/> null,
    ///   <see cref="LiveSeconds"/> 0, <see cref="WentLive"/> true). The phase changes ONLY on an elimination: Phase 1 is [0, tPhase2) up to
    ///   the first `phase` event numbered >= 2; Phase 2, when one exists, is [tPhase2, matchLength]. No such event: all Phase 1.
    /// - <b>NEW-STYLE</b> (a num == 0 event exists): <see cref="FromNewStyle"/>. The countdown is warm-up; "live" is the master's own
    ///   `phase` >= 1 line (MatchDirector.GoLive, at mLiveAt), not the countdown starting (Decision 3). <see cref="LiveSeconds"/> is that
    ///   event's t (clamped to 0 if negative: the "clock not known yet" sentinel). <see cref="Warmup"/> is [0, LiveSeconds), left out of
    ///   every other window. A match that never went live has no Phase 1/2 split: Warmup swallows the whole log and Phase 1
    ///   collapses to the empty window at its end.
    /// The num == 0 anchor is never itself a transition or a live moment: only phase numbers >= 1 (live) / >= 2 (transition) count.
    ///
    /// Fallback: a `phase` event is MatchDirector's job to raise, so a log with a real elimination but no `phase` >= 2 event would
    /// stay entirely Phase 1. Then the EARLIEST elimination's t becomes the transition (<see cref="UsedEliminationFallback"/>, which the
    /// aggregator surfaces as a header warning). On a new-style log nothing before LiveSeconds qualifies, including a warm-up
    /// elimination line.</summary>
    public sealed class PhaseTimeline
    {
        public readonly double MatchLength;

        /// <summary>This log's live moment: 0 for a legacy log, or the master's first `phase` >= 1 line's t (clamped to 0 if negative).
        /// Reads 0 when <see cref="WentLive"/> is false.</summary>
        public readonly double LiveSeconds;

        /// <summary>True for a legacy log and for a new-style log that logged a `phase` >= 1 line; false only for a new-style log whose
        /// match never went live (see <see cref="Warmup"/>).</summary>
        public readonly bool WentLive;

        /// <summary>True only for a new-style log (one with its own `phase` 0 anchor); a legacy log's <see cref="Warmup"/> is null even
        /// though its Phase 1 starts at 0.</summary>
        public readonly bool HasWarmup;

        /// <summary>[0, LiveSeconds), left out of every other window; null for a legacy log. A new-style match that never went live has
        /// no other window worth anything, so this becomes [0, MatchLength]: the whole log is warm-up.</summary>
        public readonly TimeWindow Warmup;

        /// <summary>Null when the log has no qualifying `phase` >= 2 event and no qualifying `elimination` event.</summary>
        public readonly double? TransitionSeconds;

        /// <summary>True when <see cref="TransitionSeconds"/> came from an `elimination` event because no qualifying `phase` >= 2 event
        /// was found (see the class comment's Fallback paragraph).</summary>
        public readonly bool UsedEliminationFallback;

        public readonly TimeWindow WholeMatch;
        public readonly TimeWindow Phase1;

        /// <summary>Null when <see cref="TransitionSeconds"/> is null - see <see cref="HasPhase2"/>.</summary>
        public readonly TimeWindow Phase2;

        public bool HasPhase2 => Phase2 != null;

        private PhaseTimeline(double matchLength, double liveSeconds, bool wentLive, bool hasWarmup,
            double? transitionSeconds, bool usedEliminationFallback)
        {
            MatchLength = matchLength;
            LiveSeconds = liveSeconds;
            WentLive = wentLive;
            HasWarmup = hasWarmup;
            TransitionSeconds = transitionSeconds;
            UsedEliminationFallback = usedEliminationFallback;

            if (hasWarmup && !wentLive)
            {
                // Never went live: the whole log is warm-up, nothing is left for Phase 1/2. Phase1 is the empty window right at the end
                // (nothing real falls inside it, like the legacy no-phase-event shape), never null, so callers that assume Phase1 is a
                // real TimeWindow keep working.
                Warmup = new TimeWindow(0, matchLength, true);
                WholeMatch = Warmup;
                Phase1 = new TimeWindow(matchLength, matchLength, false);
                Phase2 = null;
                return;
            }

            Warmup = hasWarmup ? new TimeWindow(0, liveSeconds, false) : null;
            WholeMatch = new TimeWindow(liveSeconds, matchLength, true);

            if (transitionSeconds.HasValue)
            {
                Phase1 = new TimeWindow(liveSeconds, transitionSeconds.Value, false);
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
            bool hasWarmup = false;
            if (log != null)
            {
                foreach (TelemetryEvent e in log.Events)
                {
                    if (e.Name == TelemetryKeys.Phase && ReadPhaseNumber(e.Data) == 0)
                    {
                        hasWarmup = true;
                        break;
                    }
                }
            }

            return hasWarmup ? FromNewStyle(log) : FromLegacy(log);
        }

        /// <summary>See the class comment's LEGACY paragraph.</summary>
        private static PhaseTimeline FromLegacy(TelemetryLog log)
        {
            double matchLength = 0;
            double? phaseTransition = null;
            double? earliestElimination = null;

            if (log != null)
            {
                foreach (TelemetryEvent e in log.Events)
                {
                    if (e.T > matchLength) matchLength = e.T;

                    // First (log.Events is t-sorted) `phase` event numbered >= 2. A negative t (the -1 "clock not known yet" sentinel)
                    // can never be a real transition instant: ignored rather than producing an inverted [0, -1) window.
                    if (!phaseTransition.HasValue && e.Name == TelemetryKeys.Phase && e.T >= 0)
                    {
                        int num = ReadPhaseNumber(e.Data);
                        if (num >= 2) phaseTransition = e.T;
                    }
                    else if (e.Name == TelemetryKeys.Elimination)
                    {
                        // An elimination's usefulness as a fallback transition doesn't depend on a clean positive t: a negative one still
                        // means "eliminated essentially at match start", so it maps to 0 rather than being discarded.
                        double t = e.T < 0 ? 0 : e.T;
                        if (!earliestElimination.HasValue || t < earliestElimination.Value)
                            earliestElimination = t;
                    }
                }
            }

            bool usedFallback = !phaseTransition.HasValue && earliestElimination.HasValue;
            double? transition = phaseTransition ?? earliestElimination;

            return new PhaseTimeline(matchLength, 0, true, false, transition, usedFallback);
        }

        /// <summary>The countdown is warm-up; live is the master's first `phase` >= 1 line (GoLive's live write). Two passes: the first
        /// finds MatchLength and LiveSeconds alone; the second, knowing LiveSeconds, finds the transition (a qualifying `phase` >= 2
        /// event, or the elimination fallback), never considering anything before live. See the class comment's NEW-STYLE paragraph.</summary>
        private static PhaseTimeline FromNewStyle(TelemetryLog log)
        {
            double matchLength = 0;
            double? liveSeconds = null;

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.T > matchLength) matchLength = e.T;

                if (!liveSeconds.HasValue && e.Name == TelemetryKeys.Phase && ReadPhaseNumber(e.Data) >= 1)
                    liveSeconds = e.T < 0 ? 0 : e.T;
            }

            bool wentLive = liveSeconds.HasValue;
            double live = liveSeconds ?? 0;

            double? phaseTransition = null;
            double? earliestElimination = null;
            if (wentLive)
            {
                foreach (TelemetryEvent e in log.Events)
                {
                    double t = e.T < 0 ? 0 : e.T;
                    if (t < live) continue; // Decision 3: nothing before live counts, including a warm-up elimination line.

                    if (!phaseTransition.HasValue && e.Name == TelemetryKeys.Phase && ReadPhaseNumber(e.Data) >= 2)
                        phaseTransition = t;
                    else if (e.Name == TelemetryKeys.Elimination && (!earliestElimination.HasValue || t < earliestElimination.Value))
                        earliestElimination = t;
                }
            }

            bool usedFallback = wentLive && !phaseTransition.HasValue && earliestElimination.HasValue;
            double? transition = phaseTransition ?? (wentLive ? earliestElimination : null);

            return new PhaseTimeline(matchLength, live, wentLive, true, transition, usedFallback);
        }

        private static int ReadPhaseNumber(JObject data)
        {
            JToken token = data?[TelemetryKeys.PhaseNumber];
            return (token != null && token.Type != JTokenType.Null) ? token.ToObject<int>() : 1;
        }
    }
}
