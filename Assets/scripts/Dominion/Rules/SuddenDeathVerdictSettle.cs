namespace Overpower.Dominion
{
    /// <summary>
    /// Sudden death's verdict must hold still for a moment before the master writes it. Two players who fall "at the same instant" never land in the
    /// room in the same instant - a stalled frame or the network spreads them by a fraction of a second - and the master looks every quarter second, so
    /// without this a look in between would crown the one who fell last. The master asks Settled with the verdict it sees on each look (null = nothing
    /// to write); the answer is true only once the same verdict has been seen continuously for the settle time. A different verdict starts the wait
    /// again, and nothing to write forgets it. Deaths this close together therefore count as simultaneous (Tudor A8: the replay).
    /// </summary>
    public sealed class SuddenDeathVerdictSettle
    {
        private string key;
        private float since;

        /// <summary>True once <paramref name="verdictKey"/> has been the verdict on every look since at least settleSeconds ago.</summary>
        public bool Settled(string verdictKey, float nowSeconds, float settleSeconds)
        {
            if (verdictKey == null)
            {
                key = null;
                return false;
            }
            if (verdictKey != key)
            {
                key = verdictKey;
                since = nowSeconds;
            }
            return nowSeconds - since >= settleSeconds;
        }

        /// <summary>Forget what was seen (a new sudden death, a new master, leaving the room).</summary>
        public void Reset() => key = null;
    }
}
