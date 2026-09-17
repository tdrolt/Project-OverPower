using System;

namespace Overpower.Match
{
    /// <summary>
    /// A capture in progress as "where it was, how fast it moves, since when". The master publishes
    /// one only when the speed changes; every client works out the current fill from the server
    /// clock, so a capture bar moves smoothly on every screen without a network message per frame.
    /// Progress is 0..1 of a full capture; a negative rate is an enemy draining an owned zone.
    /// </summary>
    public readonly struct CaptureProgress
    {
        public const string TeamKey = "cTeam";
        public const string ProgressKey = "cProg";
        public const string RateKey = "cRate";
        public const string StampKey = "cStamp";

        // Ints on the wire: Photon handles int[] natively, and 1/10000 of a capture is finer than a pixel.
        private const float Scale = 10000f;

        public static readonly CaptureProgress Idle = new CaptureProgress(-1, 0f, 0f, 0);

        public readonly int Team;
        public readonly float Progress01;
        public readonly float RatePerSecond01;
        public readonly int StampMs;

        public CaptureProgress(int team, float progress01, float ratePerSecond01, int stampMs)
        {
            Team = team;
            Progress01 = progress01;
            RatePerSecond01 = ratePerSecond01;
            StampMs = stampMs;
        }

        public float Evaluate(int nowMs)
        {
            if (Team < 0) return 0f;
            float seconds = unchecked(nowMs - StampMs) / 1000f;
            return Math.Max(0f, Math.Min(1f, Progress01 + RatePerSecond01 * seconds));
        }

        /// <summary>True when the other snapshot moves differently (a different team or speed), so
        /// the master must publish; the same team at the same speed extrapolates identically.</summary>
        public bool NeedsRepublishComparedTo(CaptureProgress other) =>
            Team != other.Team || Math.Abs(RatePerSecond01 - other.RatePerSecond01) > 1e-4f;

        public int EncodeTeam() => Team;
        public int EncodeProgress() => (int)Math.Round(Progress01 * Scale);
        public int EncodeRate() => (int)Math.Round(RatePerSecond01 * Scale);

        public static CaptureProgress Decode(int team, int progress, int rate, int stampMs) =>
            new CaptureProgress(team, progress / Scale, rate / Scale, stampMs);

        /// <summary>A capture or drain on hold (Tudor, 2026-09-16 capture ring): a contested capture, one whose link is
        /// under attack, or a paused drain. It keeps its team and how far it got, at rate 0, so every client can draw
        /// the paused band. The same team at the same rate never republishes, and a hold never moves, so its
        /// progress can't go stale on the wire. Idle when there is no team or nothing banked.</summary>
        public static CaptureProgress Held(int team, float progress01, int stampMs) =>
            team < 0 || progress01 <= 0f ? Idle : new CaptureProgress(team, Math.Min(1f, progress01), 0f, stampMs);

        /// <summary>A team with progress that isn't moving - see Held. Requires real progress banked (review fix,
        /// 2026-09-17): a hold under 1/10000 of a capture rounds to 0 on the wire, and CaptureRingState already
        /// reads that as Idle, not Paused - IsHeld must agree.</summary>
        public bool IsHeld => Team >= 0 && RatePerSecond01 == 0f && Progress01 > 0f;
    }
}
