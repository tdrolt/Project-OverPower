using Newtonsoft.Json.Linq;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T7 / 2.7b step 9: turns a log's `phase` events into the windows the aggregator clips
    /// everything against. Pure - no IO, edit-mode tested directly (PhaseTimelineTests).
    ///
    /// From picks one of two passes, by whether the log carries a `phase` event with `num == 0` at all
    /// (2.7b's own warm-up anchor - MatchTelemetry.ClaimMatchIdentityWhenClockIsReady logs it once, the
    /// moment the master claims the match identity, whether or not the match ever actually goes live):
    ///
    /// - <b>LEGACY</b> (no `num == 0` event anywhere - every log recorded before 2.7b, and every fixture that
    ///   predates it): <see cref="FromLegacy"/>, unchanged from before this task. <see cref="LiveSeconds"/> is
    ///   0 and <see cref="WentLive"/> is true - there is no warm-up concept at all (<see cref="HasWarmup"/>
    ///   false, <see cref="Warmup"/> null), so Phase 1 starts at 0 exactly as it always has. The phase changes
    ///   ONLY on an elimination (the design doc, Part 3): Phase 1 is `[0, tPhase2)` from match start until the
    ///   first `phase` event whose own number is >= 2; Phase 2, when one exists, is `[tPhase2, matchLength]`.
    ///   A log with no such event is entirely Phase 1.
    ///
    /// - <b>NEW-STYLE</b> (a `num == 0` event exists): <see cref="FromNewStyle"/>. The countdown is part of
    ///   the warm-up (Decision 3) - "live" is not the countdown starting, it is the master's own `phase` >= 1
    ///   line, logged in MatchDirector.GoLive's live write at `mLiveAt`. <see cref="LiveSeconds"/> is that
    ///   event's own t (clamped to 0 if negative - the "clock not known yet" sentinel); <see cref="WentLive"/>
    ///   is whether one was found at all. <see cref="Warmup"/> is `[0, LiveSeconds)`, left out of every other
    ///   window; a match that never went live has no Phase 1/Phase 2 split left to report at all - Warmup
    ///   swallows the whole log instead, and Phase 1 collapses to the empty window at its very end (see the
    ///   private constructor's own "never went live" branch).
    ///
    /// MatchTelemetry's own `phase` 0 anchor is deliberately never itself a transition or a live moment - only
    /// phase numbers >= 1 (new-style, for the live moment) / >= 2 (both styles, for the transition) count.
    ///
    /// Review fix (item 9, kept from before this task): a `phase` event is 2.7's own job to raise, so a log
    /// with a real elimination but a MatchDirector that hasn't landed yet (or a bug in it) would otherwise stay
    /// entirely Phase 1 despite an elimination clearly having happened. When no qualifying `phase` >= 2 event
    /// exists but at least one qualifying `elimination` event does, the EARLIEST elimination's own t becomes
    /// the transition instead - see <see cref="UsedEliminationFallback"/>, which the aggregator surfaces as a
    /// header warning. 2.7b step 9: "qualifying" now also means "at or after LiveSeconds" on a new-style log -
    /// nothing before live counts (Decision 3), including a stray warm-up elimination line.</summary>
    public sealed class PhaseTimeline
    {
        public readonly double MatchLength;

        /// <summary>2.7b step 9: this log's own live moment - 0 for a legacy log (Phase 1 always started at
        /// 0), or the master's first `phase` >= 1 line's own t (clamped to 0 if negative) for a new-style one.
        /// Meaningless (reads 0) when <see cref="WentLive"/> is false.</summary>
        public readonly double LiveSeconds;

        /// <summary>2.7b step 9: true for a legacy log (there was never a warm-up to fail to leave) and for a
        /// new-style log that actually logged a `phase` >= 1 line. False only for a new-style log whose match
        /// never went live - see <see cref="Warmup"/>.</summary>
        public readonly bool WentLive;

        /// <summary>2.7b step 9: true only for a new-style log (one with its own `phase` 0 anchor) - a legacy
        /// log has no warm-up concept at all, so <see cref="Warmup"/> is null for it even though its own
        /// Phase 1 still starts at 0.</summary>
        public readonly bool HasWarmup;

        /// <summary>2.7b step 9: `[0, LiveSeconds)`, left out of every other window - null for a legacy log
        /// (<see cref="HasWarmup"/> false). A new-style match that never went live has no OTHER window worth
        /// anything, so this one becomes `[0, MatchLength]` instead - the whole log is warm-up.</summary>
        public readonly TimeWindow Warmup;

        /// <summary>Null when no qualifying `phase` >= 2 event, and no qualifying `elimination` event either,
        /// exists in the log - see the class comment.</summary>
        public readonly double? TransitionSeconds;

        /// <summary>True when <see cref="TransitionSeconds"/> came from an `elimination` event because no
        /// qualifying `phase` >= 2 event was found at all - see the class comment's review-fix paragraph.</summary>
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
                // 2.7b step 9: never went live - the whole log is warm-up, and there is nothing left for
                // Phase 1/2 to cover. Phase1 is the empty window right at the end (matches the "no phase
                // event at all" legacy shape's own Contains/Clip behaviour: nothing real falls inside it),
                // never null - callers that used to assume Phase1 is always a real TimeWindow keep working.
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

        /// <summary>Unchanged from before this task - see the class comment's LEGACY paragraph.</summary>
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

            return new PhaseTimeline(matchLength, 0, true, false, transition, usedFallback);
        }

        /// <summary>2.7b step 9: the countdown is warm-up - live is the master's own first `phase` >= 1 line
        /// (GoLive's live write), not the countdown starting. Two explicit passes over the log, as the plan
        /// names them: the first finds MatchLength and LiveSeconds alone; the second - now that LiveSeconds is
        /// known - finds the transition (a qualifying `phase` >= 2 event, or the elimination fallback), never
        /// considering anything before live. See the class comment's NEW-STYLE paragraph.</summary>
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
