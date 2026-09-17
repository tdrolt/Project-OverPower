namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T7: a half-open (or, for the last window in a timeline, closed) span of match
    /// seconds - the unit TelemetryAggregator.Build(log, window) filters discrete events against and
    /// clips every continuous integral (sample intervals, ownership stints, alive time...) to. Pure,
    /// no IO, edit-mode tested through PhaseTimeline.
    ///
    /// A single instance is reused for three different scopes: the whole match ([0, matchLength],
    /// EndInclusive), Phase 1 ([0, tPhase2), not inclusive - the phase-2 transition instant belongs to
    /// Phase 2, not Phase 1), and Phase 2 ([tPhase2, matchLength], EndInclusive - it is always the last
    /// window when it exists).</summary>
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

        /// <summary>Whether a discrete event's own timestamp falls inside this window. A t == -1
        /// ("match clock not known yet" - MatchClock's own sentinel, e.g. a `join` logged before the
        /// room's mStart arrives) is treated as the very first instant of the match: it belongs to
        /// whichever window starts at 0 (the whole match, and Phase 1 when there is one), never to a
        /// later phase window whose own Start is > 0.
        ///
        /// Review fix (item 9): also requires Start &lt; End - an elimination (or a `phase` >= 2
        /// event) logged at t == 0 makes Phase 1's own window `[0, 0)`, empty by construction, AND
        /// Phase 2's window start at 0 too - without this check BOTH windows would read Start &lt;= 0
        /// and double-count every t == -1 event into both phases. An empty window can never
        /// meaningfully "start" the match, so it now correctly claims nothing at all, including t ==
        /// -1; whichever window actually has positive length claims it instead.</summary>
        public bool Contains(double t)
        {
            if (t < 0) return Start <= 0 && Start < End;
            if (t < Start) return false;
            return EndInclusive ? t <= End : t < End;
        }

        /// <summary>Clips a continuous [from, to) span to this window's own bounds - the shared
        /// mechanism behind every integral this task clips (a sample interval, an ownership stint, a
        /// capture attempt, an alive-time tail). Returns false (and leaves the out params at 0) when
        /// the span has no overlap with this window at all.
        ///
        /// Review fix (item 8): a ZERO-LENGTH span (from == to) that lies inside the window is kept,
        /// not rejected - a capture completing and immediately being lost again at the exact same
        /// instant (or any other same-tick stint) produced a real, Duration == 0 row in every
        /// pre-T7, unwindowed table; rejecting on `>=` made that row silently vanish the moment ANY
        /// window (including the whole-match one) was applied.
        ///
        /// Round-2 review fix (regression in the above): using a bare `>` let a REAL, non-zero-length
        /// span that only TOUCHES this window's boundary from outside (e.g. a stint [10, 90) against
        /// a Phase 2 window starting at 90) produce a spurious zero-length row too - Max/Min clamping
        /// has no notion that the span's own `to` is exclusive, so clamping [10, 90) against Start=90
        /// gives clippedFrom == clippedTo == 90 even though the span never actually reaches 90. Now:
        /// a genuine positive-length overlap (clippedFrom &lt; clippedTo) is always kept; a touching
        /// result (clippedFrom == clippedTo) is kept ONLY when the ORIGINAL span was itself
        /// zero-length (from == to) AND this window's own Contains says it owns that exact instant
        /// (the half-open tie-break, so exactly one window ever claims it) - never for a real span
        /// that merely grazes the edge.</summary>
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

        /// <summary>Round-2 review fix (item B): like Clip, but for a continuous span whose own
        /// ends can legitimately fall OUTSIDE the timeline altogether - a player's life, which can
        /// start before the match clock was known (timeAlive &gt; deathT) or (via the t == -1
        /// sentinel) end there too. Clip's plain Start/End would truncate such a span at the
        /// literal 0/matchLength wall, which is wrong: everything before t=0 and after the log's own
        /// last instant still legitimately belongs to whichever window actually covers that edge of
        /// the timeline - the window starting at 0 (the whole match, and Phase 1 when there is one)
        /// therefore treats its own lower bound as -infinity, and the LAST window in the timeline
        /// (whichever one is EndInclusive) treats its own upper bound as +infinity. Returns 0 (never
        /// negative) when the span doesn't reach this window at all.</summary>
        public double OverlapWithUnboundedEdges(double spanStart, double spanEnd)
        {
            // Round-3 review fix: `Start <= 0` alone gave the SAME -infinity lower bound to both
            // Phase 1 and Phase 2 when the transition lands at exactly t == 0 - Phase 1 becomes
            // the empty [0, 0) window in that case, and an empty window must not claim a life
            // that only exists via the unbounded edge either. Mirrors Contains' own item-9 guard
            // (Start < End), plus EndInclusive for the degenerate case where the last window is
            // ALSO zero-length (Start == End) but is still the only window there is.
            double lo = (Start <= 0 && (Start < End || EndInclusive)) ? double.NegativeInfinity : Start;
            double hi = EndInclusive ? double.PositiveInfinity : End;
            return System.Math.Max(0.0, System.Math.Min(spanEnd, hi) - System.Math.Max(spanStart, lo));
        }
    }
}
