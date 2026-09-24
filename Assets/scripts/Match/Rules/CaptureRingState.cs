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
        /// <summary>captureFadeSpeed (2026-09-24): unfinished progress sliding back (neutral, the fading team's
        /// colour, shrinking) or refilling (owned, the owner's colour, growing) with nobody actually capturing or
        /// draining it right now. Always moving, so unlike Paused it never blinks.</summary>
        Fading,
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

            // captureFadeSpeed (2026-09-24): checked before every guard below, on purpose. Those guards
            // (progress.Team < 0 || progress.Team == owner just below; the owner < 0 check inside the
            // RatePerSecond01 < 0 branch further down) exist for a real capture/drain's OWN two-update echo race,
            // and were never written to recognise a fade/refill's shape at all: a neutral fade (negative rate,
            // neutral owner) happens to land on the owner < 0 check and reads Idle, but an owned refill (positive
            // rate, Team = the last drainer, not the owner) matches NEITHER guard and falls through to Capturing,
            // in the drainer's own colour, not Idle. Checking Fading first sidesteps both mismatches so a genuine
            // fade/refill always animates correctly.
            if (progress.Fading)
            {
                // Review fix, 2026-09-24: the two shapes that can never be a genuine fade/refill
                // (CaptureProgressPublishRule.Decide only ever fades a NEUTRAL zone's claim - negative rate - or
                // refills an OWNED one - positive rate; never the other way round). Seeing one here means this
                // progress snapshot and a separate owner write haven't both landed yet - the exact one-round-trip
                // race the brief describes: a team knocked out mid-refill (MatchDirector.cs:427 resets the zone
                // neutral) shows a growing band in the old drainer's colour on every OTHER client until the
                // master's neutral reset echoes back. Idle for that one frame instead of the phantom band.
                if (progress.RatePerSecond01 > 0f && owner < 0)
                    return Idle(outlineTeam, attacked);
                if (progress.RatePerSecond01 < 0f && owner >= 0)
                    return Idle(outlineTeam, attacked);

                float fadeFill = nowMs == 0 || progress.StampMs == 0 ? Clamp01(progress.Progress01) : progress.Evaluate(nowMs);
                if (fadeFill <= 0f)
                    return Idle(outlineTeam, attacked);
                // Owned zone refilling: the owner's own colour (progress.Team is only the last drainer, now gone -
                // see CaptureProgressPublishRule's own comment). Neutral zone fading: the fading team's colour.
                int arcTeam = owner >= 0 ? owner : progress.Team;
                return new CaptureRingState(CaptureRingPhase.Fading, fadeFill, arcTeam, outlineTeam, TerritoryMap.Neutral, attacked);
            }

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
