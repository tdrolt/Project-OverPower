namespace Overpower.Match
{
    public enum OwnerPaintBase { Neutral, Team, OutOfPlay }

    /// <summary>What a zone's owner-coloured things show right now - the capture ring's edge on the ground and a
    /// tower's crown and column caps (arena step 1). Worked out from the CaptureRingState every client already
    /// builds each frame from replicated state, so the ring and the tower can never disagree and a late joiner is
    /// right at once. OwnerPaintColours (arena step 2) turns it into a colour.</summary>
    public readonly struct OwnerPaint
    {
        public readonly OwnerPaintBase Base;
        /// <summary>The owner when Base is Team; -1 otherwise.</summary>
        public readonly int Team;
        /// <summary>An owned zone with an enemy inside (or just gone): pulse to the warning colour.</summary>
        public readonly bool PulsesToWarning;
        /// <summary>The team draining this zone, which the paint pulses to; -1 when nobody drains it.</summary>
        public readonly int PulseTeam;

        public OwnerPaint(OwnerPaintBase paintBase, int team, bool pulsesToWarning, int pulseTeam)
        {
            Base = paintBase;
            Team = team;
            PulsesToWarning = pulsesToWarning;
            PulseTeam = pulseTeam;
        }

        public static OwnerPaint From(CaptureRingState state)
        {
            if (state.OutOfPlay)
                return new OwnerPaint(OwnerPaintBase.OutOfPlay, TerritoryMap.Neutral, false, TerritoryMap.Neutral);

            bool owned = state.OutlineTeam >= 0;
            var paintBase = owned ? OwnerPaintBase.Team : OwnerPaintBase.Neutral;
            int team = owned ? state.OutlineTeam : TerritoryMap.Neutral;

            // The order the ring has always drawn: a drain pulses in the drainer's colour; only otherwise does "under
            // attack" pulse to the warning (CaptureRingView before arena step 2).
            if (state.Phase == CaptureRingPhase.Draining && state.DrainerTeam >= 0)
                return new OwnerPaint(paintBase, team, false, state.DrainerTeam);
            if (state.UnderAttack)
                return new OwnerPaint(paintBase, team, true, TerritoryMap.Neutral);
            return new OwnerPaint(paintBase, team, false, TerritoryMap.Neutral);
        }
    }
}
