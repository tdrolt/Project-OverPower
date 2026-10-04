namespace Overpower.Dominion
{
    /// <summary>
    /// The Dominion keys in the room's properties, in one place. Never reuse a Conquest key (mPhase, mElim, mWin, mTeams, mLiveAt, mMode) or a
    /// lobby key (lN, lM, lS, lH, lF, lC): Dominion writes its own, so a Conquest room and a Dominion room can never be confused by what is in them.
    /// </summary>
    public static class DominionKeys
    {
        /// <summary>int: the round now in play, or (during a break) the round the break leads to, 1..3. Absent until round 1 starts.</summary>
        public const string Round = "dRnd";
        /// <summary>int: the stage (DominionStage).</summary>
        public const string Stage = "dStg";
        /// <summary>int: the server ms the current stage ends (a round's end or a break's end). 0 in sudden death, which has no clock here.</summary>
        public const string StageEnd = "dEnd";
        /// <summary>int[]: points per team id 0..2 in the round (kept at the round's final points through the break).</summary>
        public const string Points = "dPts";
        /// <summary>int[]: round wins per team id 0..2.</summary>
        public const string Wins = "dWins";
        /// <summary>int: the match winner's team id, -1 while there is none.</summary>
        public const string Winner = "dWin";

        // Reserved for later tasks - named here so nothing else claims them; nothing reads or writes them yet.
        /// <summary>Task 3: the centre's next payout time.</summary>
        public const string CentrePayout = "dCtr";
        /// <summary>Task 8: sudden death's start time.</summary>
        public const string SuddenDeathStart = "dSd";

        /// <summary>How many team ids the per-team arrays hold (team ids are always 0..2).</summary>
        public const int TeamSlots = 3;
    }
}
