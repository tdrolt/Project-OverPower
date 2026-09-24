using System;

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

        /// <summary>The floor of the margin Classify uses to tell a fresh start from a resume: a
        /// capture bar starting already above this much fill reads as resuming progress already
        /// banked, not a fresh start from zero (a drain bar mirrors this around 1.0 instead of 0 -
        /// see Classify's own comment). This floor alone is comfortably above one frame's own
        /// contribution for a SLOW transition - a solo Tier 2 capture (15s for one player) fills
        /// about 0.0011 in one frame at 60 Hz, two orders of magnitude below this - but it is NOT
        /// enough headroom for a fast one: Tier 3 (10s, the fastest tier TerritoryConfig ships by
        /// default) with several capturers multiplies the rate (rate = eligibleCount / captureSeconds
        /// - see BuildingCapture.ComputeCurrentProgress), and a drain's DecaySeconds can be short
        /// too. A slow or lagged master frame (well under 60 Hz) multiplies whichever rate further.
        /// Classify scales this floor by the transition's own rate (review fix, 2026-09-17: the old
        /// static floor alone misread a fast fresh drain or capture as a resume whenever a master
        /// frame ran long enough - see FirstFrameAllowanceSeconds and this file's own tests for the
        /// real-world values that hid the bug).</summary>
        public const float ResumeThreshold01 = 0.01f;

        /// <summary>How long a single master frame is assumed to ever realistically run, in seconds,
        /// for ResumeThreshold01's rate-aware margin: Classify never treats a first publish within
        /// this many seconds' worth of the transition's own rate as a resume. Deliberately generous -
        /// far longer than any single frame at any master tick rate this project targets - so a
        /// genuinely fresh start is never mistaken for a resume, while a real resume (which starts
        /// well past what one frame could have contributed) is still told apart correctly.</summary>
        public const float FirstFrameAllowanceSeconds = 0.25f;

        /// <summary>The `capture` event this transition should log - Started/Resumed/Paused or
        /// their Drain* equivalents - with the team it is about and the progress to report, or null
        /// if neither side is "active" (both old and new read rate 0; not expected from a real
        /// publish, since nothing would have changed to trigger one, but guarded rather than
        /// assumed). "Active" is judged purely by RatePerSecond01 being non-zero, not by comparing
        /// against <c>CaptureProgress.Idle</c> - a held capture or drain (CaptureProgress.Held,
        /// published since the capture ring change, 2026-09-17) is handled the same way: it reads as
        /// "not active" here exactly as Idle does, so a stop into that hold still logs
        /// Paused/DrainPaused with the fill it stopped at, and a resume out of it is told apart from
        /// a fresh start by the same rate-aware margin (see ResumeThreshold01), mirrored below 1.0 for
        /// a drain since its fill counts down from 1.0, not up from 0.</summary>
        public static string Classify(Overpower.Match.CaptureProgress oldProgress, Overpower.Match.CaptureProgress newProgress,
                                      int nowMs, out int team, out float progress)
        {
            // captureFadeSpeed (2026-09-24): a fade/refill has a real nonzero rate too (that is the whole point -
            // it extrapolates smoothly like a live capture/drain), so without excluding it here it would fall
            // through to the very same branch below and misreport as Started/Resumed/DrainStarted/DrainResumed.
            // Nobody is actually capturing or draining during a fade/refill, so it is never "active" for telemetry
            // purposes - it is treated the same as a Held/Paused state instead (see the wasActive branch below,
            // which already logs the real stop fill whichever ends a segment: an Idle, a Held, or now a fade).
            bool isActive = newProgress.RatePerSecond01 != 0f && !newProgress.Fading;

            if (isActive)
            {
                team = newProgress.Team;
                progress = newProgress.Progress01;
                bool draining = newProgress.RatePerSecond01 < 0f;
                // Rate-aware margin (see ResumeThreshold01) - the plain floor alone isn't enough
                // headroom for a fast capture (several capturers, a short tier) or a short-DecaySeconds
                // drain on a slow master frame.
                float margin = Math.Max(ResumeThreshold01, Math.Abs(newProgress.RatePerSecond01) * FirstFrameAllowanceSeconds);
                // A capture resumes when it starts already banked (progress counts UP from 0); a
                // drain resumes when it starts already partly drained (its progress is the owner's
                // remaining hold, counting DOWN from 1.0).
                bool resuming = draining ? newProgress.Progress01 < 1f - margin : newProgress.Progress01 > margin;
                // Review fix, 2026-09-24: a fade/refill was never THIS team's own capture/drain to resume (see
                // CaptureFadeRule.CapturingTeamAbsent - it only ever fades/refills a claim nobody of its own team
                // is standing in). A different team taking the claim over from a fade/refill inherits its
                // progress fraction (captureFadeSpeed's whole point), which used to read as "resuming" purely by
                // that fraction and credit the OLD team (TelemetryAggregator ~640-666 only swaps the credited team
                // on a fresh start, never on a resume - "resumed/drainResumed: continues the SAME open attempt").
                // The SAME team resuming its own interrupted fade/refill is untouched: only a team change forces
                // a fresh start here.
                if (oldProgress.Fading && newProgress.Team != oldProgress.Team)
                    resuming = false;
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
