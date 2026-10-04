namespace Overpower.Dominion
{
    /// <summary>
    /// The stamp of a death in sudden death. A body that ARRIVES dead (joined or rejoined into a sudden death) carries the arrival stamp - a real earlier
    /// moment, never "now", which could make its team the one that "fell last" and win. A player who really dies there carries now. The "arrived dead"
    /// flag is taken (read and cleared in one step) wherever it could be left standing, so it can never stamp a later real death.
    /// </summary>
    public static class ArrivalDeathRules
    {
        /// <summary>The stamp a sudden-death death carries: the arrival stamp for a body that arrived dead, else the moment of the death.</summary>
        public static int StampFor(bool arrivedDead, int arrivalStampMs, int nowMs) => arrivedDead ? arrivalStampMs : nowMs;

        /// <summary>Returns the flag and clears it.</summary>
        public static bool Take(ref bool arrivedDeadFlag)
        {
            bool was = arrivedDeadFlag;
            arrivedDeadFlag = false;
            return was;
        }
    }
}
