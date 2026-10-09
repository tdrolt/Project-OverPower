namespace Overpower.Dominion
{
    /// <summary>
    /// The Dominion keys in the room's properties, in one place. Never reuse a Conquest key (mPhase, mElim, mWin, mTeams, mLiveAt, mMode) or a
    /// lobby key (lN, lM, lS, lH, lF, lC): Dominion writes its own, so a Conquest room and a Dominion room can never be confused by what is in them.
    /// </summary>
    public static class DominionKeys
    {
        /// <summary>int: the round now in play, or (during a break) the round the break leads to, 1..3. Written at go-live together with the break before round 1, so it is absent only before the match is live.</summary>
        public const string Round = "dRnd";
        /// <summary>int: the stage (DominionStage).</summary>
        public const string Stage = "dStg";
        /// <summary>int: the server ms the current stage ends (a round's end or a break's end). 0 in sudden death, which has no clock: the players' alive flags end it.</summary>
        public const string StageEnd = "dEnd";
        /// <summary>int[]: points per team id 0..2 in the round (kept at the round's final points through the break).</summary>
        public const string Points = "dPts";
        /// <summary>int[]: round wins per team id 0..2.</summary>
        public const string Wins = "dWins";
        /// <summary>int: the match winner's team id, -1 while there is none.</summary>
        public const string Winner = "dWin";

        /// <summary>int: the dEnd of the stage (a round or a break) whose start the master has reset the zones for. A master that sees the
        /// stage's dEnd differ from dRz resets the zones and writes it, so a new master finishes a reset the old one never did.</summary>
        public const string ZonesResetFor = "dRz";

        /// <summary>int: the server ms of the centre's next payout (3v3v3 on a map with a Tier 4 zone only). Written with each round start.</summary>
        public const string CentrePayout = "dCtr";

        /// <summary>int: counts up with every points write (dPts and/or dCtr). Every points write expects the value it built on, so two clients
        /// that both think they are master cannot both add to the same points (the second write is refused). Absent until the first points write.</summary>
        public const string PointsSeq = "dPseq";

        /// <summary>int: the server ms the sudden-death circle starts to shrink. Written with the sudden-death stage (the stage's start plus the get-ready
        /// countdown), and written again with a new value when everyone fell at once and sudden death starts over. Absent before sudden death.</summary>
        public const string SuddenDeathStart = "dSd";

        /// <summary>int[]: the team ids playing the current sudden death (A33). Written with sudden death's start (the teams level on round wins) and
        /// again with every replay (only the teams whose last players fell together); everyone else waits dead. Absent before sudden death.</summary>
        public const string SuddenDeathTeams = "dSdT";

        /// <summary>int[]: each finished round's final points, rounds x TeamSlots flattened (round 1's three teams, then round 2's, ...). The master appends one
        /// round in the same write that scores it, because dPts is cleared at the next round's start and the result table needs every round.
        /// Absent until round 1 is over.</summary>
        public const string History = "dHist";

        /// <summary>int[]: the team that won each finished round, one entry per round in the same order as dHist (-1 for a tied round and for a round cut short
        /// by "last team standing", which no one won). Written by the master in the same write that appends dHist, so the result table bolds the winner the
        /// wins counted, not the points leader. Absent in a room from before it existed: the table then falls back to the points leader.</summary>
        public const string HistoryWinners = "dHistW";

        /// <summary>int[]: the teams playing the current overtime (A50): the ones within the lead of the top at the buzzer. Written with the Overtime stage,
        /// removed again by the write that ends the round. Absent outside overtime.</summary>
        public const string OvertimeTeams = "dOtT";

        /// <summary>How many team ids the per-team arrays hold (team ids are always 0..2).</summary>
        public const int TeamSlots = 3;
    }
}
