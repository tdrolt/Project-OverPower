namespace Overpower.Telemetry
{
    /// <summary>
    /// Classifies a capture-progress transition (Task T4, opus review fix) using ONLY the rate,
    /// team and progress the two <see cref="Overpower.Match.CaptureProgress"/> values themselves
    /// carry - no per-zone memory, no read of BuildingManager's live ownership. A first version of
    /// this tried to recover "completed"/"neutralised" by comparing against the live owner, which
    /// raced the ownership snapshot's own separate echo (a different SetCustomProperties call from
    /// the capture-progress one) and genuinely mislabelled transitions - a master-switch reset read
    /// as "paused", a restart-from-zero after the capturers left and came back read as "resumed" at
    /// progress 0, and a stopping drain could read as "drainPaused" when it had actually finished.
    ///
    /// This version does not try to tell a completed capture apart from an interrupted one, or a
    /// neutralised zone from a merely-paused drain: BOTH read as the same "paused"/"drainPaused" a
    /// capture/drain bar going idle always means, whatever caused it. `ownership` is a separate
    /// event, already logged with a real held-since stamp the moment BuildingManager applies the
    /// change - T5's aggregator (or a human reading the file) tells "paused because it completed"
    /// apart from "paused because it was interrupted" by looking for a same-timestamp `ownership`
    /// line, not by asking this classifier to guess from data it cannot see reliably.
    /// </summary>
    public static class CaptureTransitionClassifier
    {
        public const string Started = "started";
        public const string Resumed = "resumed";
        public const string Paused = "paused";
        public const string DrainStarted = "drainStarted";
        public const string DrainResumed = "drainResumed";
        public const string DrainPaused = "drainPaused";

        /// <summary>A capture/drain bar starting already above this much fill reads as resuming
        /// progress already banked, not a fresh start from zero. Set comfortably above one frame's
        /// own contribution at any realistic frame rate or capture speed: a solo Tier 2 capture (15s
        /// for one player, the fastest single-player rate TerritoryConfig ships by default) fills
        /// about 0.0011 in one frame at 60 Hz - two orders of magnitude below this.</summary>
        public const float ResumeThreshold01 = 0.01f;

        /// <summary>The `capture` event this transition should log - Started/Resumed/Paused or
        /// their Drain* equivalents - with the team it is about and the progress to report, or null
        /// if neither side is "active" (both old and new read rate 0; not expected from a real
        /// publish, since nothing would have changed to trigger one, but guarded rather than
        /// assumed). "Active" is judged purely by RatePerSecond01 being non-zero, not by comparing
        /// against <c>CaptureProgress.Idle</c> - a future task that publishes a genuinely paused,
        /// rate-0 hold with its team and progress still set (rather than collapsing to Idle) is
        /// handled the same way: it reads as "not active" here exactly as Idle does, so a stop into
        /// that hold still logs Paused/DrainPaused with the fill it stopped at, and a resume out of
        /// it is told apart from a fresh start by the same fill-above-threshold rule.</summary>
        public static string Classify(Overpower.Match.CaptureProgress oldProgress, Overpower.Match.CaptureProgress newProgress,
                                      int nowMs, out int team, out float progress)
        {
            bool isActive = newProgress.RatePerSecond01 != 0f;

            if (isActive)
            {
                team = newProgress.Team;
                progress = newProgress.Progress01;
                bool draining = newProgress.RatePerSecond01 < 0f;
                bool resuming = newProgress.Progress01 > ResumeThreshold01;
                if (draining) return resuming ? DrainResumed : DrainStarted;
                return resuming ? Resumed : Started;
            }

            bool wasActive = oldProgress.RatePerSecond01 != 0f;
            if (wasActive)
            {
                team = oldProgress.Team;
                // The fill AT THE MOMENT IT STOPPED, extrapolated from the OLD value's own rate and
                // stamp up to now - not oldProgress.Progress01, which is only the fill when that
                // segment BEGAN. A capture that ran from 0.1 to 0.9 before stopping must log 0.9,
                // not the 0.1 it started this segment at.
                progress = oldProgress.Evaluate(nowMs);
                return oldProgress.RatePerSecond01 < 0f ? DrainPaused : Paused;
            }

            team = -1;
            progress = 0f;
            return null;
        }
    }
}
