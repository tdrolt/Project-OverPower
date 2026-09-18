namespace Overpower.Match
{
    /// <summary>What a zone's capture ring (and its minimap progress ring) shows.</summary>
    public enum CaptureRingPhase
    {
        /// <summary>Nothing in progress: no band.</summary>
        Idle,
        /// <summary>A team is taking a neutral zone: the band grows in that team's colour.</summary>
        Capturing,
        /// <summary>An enemy drains an owned zone: the band is the owner's remaining hold, shrinking, in the owner's colour.</summary>
        Draining,
        /// <summary>A capture or drain on hold (contested, its link under attack, a paused drain): the band stays and blinks.</summary>
        Paused,
    }

    /// <summary>
    /// The capture ring's state (spec 2026-09-16, "Capture ring"), worked out from what every client already has: the
    /// zone's replicated CaptureProgress, its owner and whether it is under attack. Pure, so the ground ring and the
    /// minimap can't disagree, and every case is tested.
    /// </summary>
    public readonly struct CaptureRingState
    {
        public readonly CaptureRingPhase Phase;
        /// <summary>How much of the band is drawn, 0..1.</summary>
        public readonly float Fill01;
        /// <summary>The band's team colour; -1 when Idle.</summary>
        public readonly int ArcTeam;
        /// <summary>The edge's team colour: the owner, -1 = neutral.</summary>
        public readonly int OutlineTeam;
        /// <summary>The team draining the zone while Draining (the edge pulses in its colour); -1 otherwise.</summary>
        public readonly int DrainerTeam;
        /// <summary>An owned zone with a living enemy inside or just gone (ZonePresenceTracker).</summary>
        public readonly bool UnderAttack;
        /// <summary>2.7b Decision 8: a capital nobody is playing for - the third capital when the host starts a
        /// two-team match. Checked before every other branch of From, so an out-of-play zone always shows Idle
        /// with a neutral edge and no arc, whatever capture progress or attack state the room still carries for it.</summary>
        public readonly bool OutOfPlay;

        public CaptureRingState(CaptureRingPhase phase, float fill01, int arcTeam, int outlineTeam, int drainerTeam, bool underAttack, bool outOfPlay = false)
        {
            Phase = phase;
            Fill01 = fill01;
            ArcTeam = arcTeam;
            OutlineTeam = outlineTeam;
            DrainerTeam = drainerTeam;
            UnderAttack = underAttack;
            OutOfPlay = outOfPlay;
        }

        public bool ShowsArc => Phase != CaptureRingPhase.Idle && Fill01 > 0f;

        /// <param name="owner">The zone's owner, or -1 for neutral.</param>
        /// <param name="underAttack">ZonePresenceTracker.IsUnderAttack(zone). Ignored for a neutral zone.</param>
        /// <param name="nowMs">PhotonNetwork.ServerTimestamp; 0 = not synced yet.</param>
        /// <param name="outOfPlay">MatchDirector.IsOutOfPlay(zone) (2.7b Decision 8). Wins over every other branch.</param>
        public static CaptureRingState From(CaptureProgress progress, int owner, bool underAttack, int nowMs, bool outOfPlay = false)
        {
            if (outOfPlay)
                return new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, TerritoryMap.Neutral,
                                            TerritoryMap.Neutral, false, outOfPlay: true);

            int outlineTeam = owner >= 0 ? owner : TerritoryMap.Neutral;
            bool attacked = owner >= 0 && underAttack;

            // A team never captures or drains its own zone. Seeing it means the room's owner update arrived before its
            // progress update (they are separate writes): the capture just completed.
            if (progress.Team < 0 || progress.Team == owner)
                return Idle(outlineTeam, attacked);

            // Without a synced clock, extrapolating from "now = 0" would flash the band full or empty; show the
            // published value instead (the old bar skipped the frame for the same reason).
            float fill = nowMs == 0 || progress.StampMs == 0 ? Clamp01(progress.Progress01) : progress.Evaluate(nowMs);

            if (progress.RatePerSecond01 > 0f)
                return new CaptureRingState(CaptureRingPhase.Capturing, fill, progress.Team, outlineTeam, TerritoryMap.Neutral, attacked);

            if (progress.RatePerSecond01 < 0f)
            {
                // Same two-update gap, for a drain: the zone already went neutral.
                if (owner < 0)
                    return Idle(outlineTeam, attacked);
                return new CaptureRingState(CaptureRingPhase.Draining, fill, owner, outlineTeam, progress.Team, attacked);
            }

            float held = Clamp01(progress.Progress01);
            if (held <= 0f)
                return Idle(outlineTeam, attacked);
            // A held drain is the owner's remaining hold (owner's colour); a held capture of a neutral zone is the
            // capturer's.
            return new CaptureRingState(CaptureRingPhase.Paused, held, owner >= 0 ? owner : progress.Team, outlineTeam,
                                        TerritoryMap.Neutral, attacked);
        }

        private static CaptureRingState Idle(int outlineTeam, bool attacked) =>
            new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, outlineTeam, TerritoryMap.Neutral, attacked);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
