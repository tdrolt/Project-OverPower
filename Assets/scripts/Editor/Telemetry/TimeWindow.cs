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
        /// later phase window whose own Start is > 0.</summary>
        public bool Contains(double t)
        {
            if (t < 0) return Start <= 0;
            if (t < Start) return false;
            return EndInclusive ? t <= End : t < End;
        }

        /// <summary>Clips a continuous [from, to) span to this window's own bounds - the shared
        /// mechanism behind every integral this task clips (a sample interval, an ownership stint, a
        /// capture attempt, an alive-time tail). Returns false (and leaves the out params at 0) when
        /// the span has no overlap with this window at all.</summary>
        public bool Clip(double from, double to, out double clippedFrom, out double clippedTo)
        {
            clippedFrom = System.Math.Max(from, Start);
            clippedTo = System.Math.Min(to, End);
            if (clippedFrom >= clippedTo)
            {
                clippedFrom = 0;
                clippedTo = 0;
                return false;
            }
            return true;
        }
    }
}
