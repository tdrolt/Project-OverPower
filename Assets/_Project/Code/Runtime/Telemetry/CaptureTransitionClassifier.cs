using System;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Classifies a capture-progress transition using ONLY the rate, team and progress the two
    /// <see cref="Overpower.Match.CaptureProgress"/> values carry - no per-zone memory, no read of BuildingManager's live ownership:
    /// comparing against the live owner raced the ownership snapshot's separate echo (a different SetCustomProperties call) and
    /// mislabelled transitions. So it cannot tell a completed capture from an interrupted one, or a neutralised zone from a
    /// paused drain: both read as "paused"/"drainPaused". The separate `ownership` event, logged the moment BuildingManager applies
    /// the change, tells them apart: the aggregator looks for a same-timestamp `ownership` line.
    /// </summary>
    public static class CaptureTransitionClassifier
    {
        public const string Started = "started";
        public const string Resumed = "resumed";
        public const string Paused = "paused";
        public const string DrainStarted = "drainStarted";
        public const string DrainResumed = "drainResumed";
        public const string DrainPaused = "drainPaused";

        /// <summary>The floor of the margin Classify uses to tell a fresh start from a resume: a capture bar starting above this
        /// fill reads as resuming banked progress (a drain mirrors this around 1.0). The floor alone is too small for a fast
        /// transition (the fastest TerritoryConfig tier with several capturers, or a short DecaySeconds, on a slow master frame;
        /// rate = CaptureSpeedRule.For(eligibleCount, list) / captureSeconds, see BuildingCapture.ComputeCurrentProgress), so
        /// Classify scales it by the transition's own rate (see FirstFrameAllowanceSeconds).</summary>
        public const float ResumeThreshold01 = 0.01f;

        /// <summary>How long one master frame is assumed to ever run, for ResumeThreshold01's rate-aware margin: a first publish
        /// within this many seconds' worth of the transition's rate is never a resume. Deliberately generous, so a fresh start is
        /// never mistaken for a resume.</summary>
        public const float FirstFrameAllowanceSeconds = 0.25f;

        /// <summary>The `capture` event this transition should log (Started/Resumed/Paused or the Drain* equivalents) with its team
        /// and the progress to report, or null if neither side is active (guarded, though a real publish never does this). "Active"
        /// is RatePerSecond01 non-zero, not a comparison with CaptureProgress.Idle: a held capture or drain (CaptureProgress.Held)
        /// reads as not active exactly as Idle does, so a stop into the hold logs Paused/DrainPaused with the fill it stopped at.</summary>
        public static string Classify(Overpower.Match.CaptureProgress oldProgress, Overpower.Match.CaptureProgress newProgress,
                                      int nowMs, out int team, out float progress)
        {
            // A fade/refill (captureFadeSpeed) has a real nonzero rate (it extrapolates like a live capture/drain), but nobody is
            // capturing or draining during it, so it is never "active" for telemetry: it is treated like Held/Paused, and the
            // wasActive branch below logs the real stop fill.
            bool isActive = newProgress.RatePerSecond01 != 0f && !newProgress.Fading;

            if (isActive)
            {
                team = newProgress.Team;
                progress = newProgress.Progress01;
                bool draining = newProgress.RatePerSecond01 < 0f;
                // Rate-aware margin (see ResumeThreshold01).
                float margin = Math.Max(ResumeThreshold01, Math.Abs(newProgress.RatePerSecond01) * FirstFrameAllowanceSeconds);
                // A capture resumes when it starts already banked (progress counts UP from 0); a
                // drain resumes when it starts already partly drained (its progress is the owner's
                // remaining hold, counting DOWN from 1.0).
                bool resuming = draining ? newProgress.Progress01 < 1f - margin : newProgress.Progress01 > margin;
                // A fade/refill was never THIS team's own capture/drain to resume (CaptureFadeRule.CapturingTeamAbsent). A different
                // team taking the claim over inherits its progress fraction, which would read as "resuming" and credit the OLD team
                // (TelemetryAggregator only swaps the credited team on a fresh start). The SAME team resuming its own fade/refill is
                // untouched: only a team change forces a fresh start.
                if (oldProgress.Fading && newProgress.Team != oldProgress.Team)
                    resuming = false;
                // A rate change while the same team keeps going the same way (a second capturer or drainer walking in) is the same
                // attempt carrying on; the rate-aware margin only sees "a high rate, little progress" and would close the first
                // attempt as abandoned.
                if (oldProgress.RatePerSecond01 != 0f && !oldProgress.Fading && oldProgress.Team == newProgress.Team
                    && (oldProgress.RatePerSecond01 < 0f) == draining)
                    resuming = true;
                if (draining) return resuming ? DrainResumed : DrainStarted;
                return resuming ? Resumed : Started;
            }

            bool wasActive = oldProgress.RatePerSecond01 != 0f && !oldProgress.Fading;
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
