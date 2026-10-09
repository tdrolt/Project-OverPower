namespace Overpower.EditorTools.Telemetry
{
    /// <summary>A half-open (or, for the last window in a timeline, closed) span of match seconds - the unit
    /// TelemetryAggregator.Build(log, window) filters discrete events against and clips every continuous integral (sample
    /// intervals, ownership stints, alive time...) to. Pure, no IO, edit-mode tested through PhaseTimeline.
    ///
    /// One type serves three scopes: the whole match ([0, matchLength], EndInclusive), Phase 1 ([0, tPhase2), not inclusive: the
    /// phase-2 transition instant belongs to Phase 2) and Phase 2 ([tPhase2, matchLength], EndInclusive: always the last window
    /// when it exists).</summary>
    public sealed class TimeWindow
    {
        public readonly double Start;
        public readonly double End;

        /// <summary>True for the window that reaches all the way to the real end of what it's
        /// covering (the whole match, or the last phase) - an event landing exactly on End belongs to
        /// THIS window rather than spilling into a next one that doesn't exist. False for a window
        /// whose End is itself the start of the next window (Phase 1 ending exactly where Phase 2
        /// begins) - the instant AT that boundary belongs to the next window, not this one.</summary>
        public readonly bool EndInclusive;

        public TimeWindow(double start, double end, bool endInclusive)
        {
            Start = start;
            End = end;
            EndInclusive = endInclusive;
        }

        /// <summary>Whether a discrete event's timestamp falls inside this window. A t == -1 ("match clock not known yet", MatchClock's
        /// sentinel, e.g. a `join` logged before the room's mStart arrives) is treated as the very first instant of the match: it
        /// belongs to whichever window starts at 0 (the whole match, and Phase 1 when there is one), never to a later phase window.
        ///
        /// Also requires Start &lt; End: an elimination (or `phase` >= 2 event) at t == 0 makes Phase 1's window [0, 0), empty, AND
        /// Phase 2's start at 0 too; without the check BOTH would read Start &lt;= 0 and double-count every t == -1 event. An empty
        /// window claims nothing, including t == -1; whichever window has positive length claims it.</summary>
        public bool Contains(double t)
        {
            if (t < 0) return Start <= 0 && Start < End;
            if (t < Start) return false;
            return EndInclusive ? t <= End : t < End;
        }

        /// <summary>Clips a continuous [from, to) span to this window's bounds - the shared mechanism behind every integral (a sample
        /// interval, ownership stint, capture attempt, alive-time tail). Returns false (out params 0) when the span has no overlap.
        ///
        /// A ZERO-LENGTH span (from == to) inside the window is kept, not rejected: a capture completing and being lost again at the
        /// same instant is a real Duration == 0 row, and rejecting on `>=` would make it vanish once any window was applied. But a
        /// REAL span that only TOUCHES the boundary from outside (stint [10, 90) against a window starting at 90) must not produce
        /// a spurious zero-length row: Max/Min clamping gives clippedFrom == clippedTo == 90 though the span never reaches 90. So a
        /// positive-length overlap (clippedFrom &lt; clippedTo) is always kept; a touching result is kept ONLY when the ORIGINAL span
        /// was zero-length AND this window's Contains owns that instant (the half-open tie-break: exactly one window claims it).</summary>
        public bool Clip(double from, double to, out double clippedFrom, out double clippedTo)
        {
            double candidateFrom = System.Math.Max(from, Start);
            double candidateTo = System.Math.Min(to, End);

            if (candidateFrom < candidateTo)
            {
                clippedFrom = candidateFrom;
                clippedTo = candidateTo;
                return true;
            }

            if (from == to && Contains(from))
            {
                clippedFrom = from;
                clippedTo = to;
                return true;
            }

            clippedFrom = 0;
            clippedTo = 0;
            return false;
        }

        /// <summary>Like Clip, but for a continuous span whose ends can legitimately fall OUTSIDE the timeline - a player's life, which
        /// can start before the match clock was known (timeAlive &gt; deathT) or, via the t == -1 sentinel, end there. Clip would truncate
        /// it at the literal 0/matchLength wall; instead the window starting at 0 (the whole match, and Phase 1 when there is one)
        /// treats its lower bound as -infinity, and the LAST window (the EndInclusive one) its upper bound as +infinity. Returns 0
        /// (never negative) when the span doesn't reach this window.</summary>
        public double OverlapWithUnboundedEdges(double spanStart, double spanEnd)
        {
            // `Start <= 0` alone gave the SAME -infinity lower bound to both Phase 1 and Phase 2 when the transition lands at exactly
            // t == 0: Phase 1 is then the empty [0, 0) window, and an empty window must not claim a life that only exists via the
            // unbounded edge. Mirrors Contains' Start < End guard, plus EndInclusive for the degenerate case where the last window is
            // ALSO zero-length (Start == End) but is the only window there is.
            double lo = (Start <= 0 && (Start < End || EndInclusive)) ? double.NegativeInfinity : Start;
            double hi = EndInclusive ? double.PositiveInfinity : End;
            return System.Math.Max(0.0, System.Math.Min(spanEnd, hi) - System.Math.Max(spanStart, lo));
        }
    }
}
