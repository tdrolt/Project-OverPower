namespace Overpower.Dominion
{
    /// <summary>
    /// Sudden death's verdict must hold still for a moment before the master writes it. Two death reports never reach the master in the same instant -
    /// a stalled frame or the network spreads them by a fraction of a second - and the master looks every quarter second, so without this a look in
    /// between would judge before the second report is in. The master asks ShouldWrite with the write it computed on each look; a sudden-death verdict
    /// is sent only once the same verdict has been seen continuously for the settle time (a network wait; which falls were the same MOMENT is decided
    /// afterwards by the death stamps, A31). A different verdict starts the wait again, and nothing to write forgets it.
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

        /// <summary>The master's one decision to send a write now, from the write it computed. A sudden-death verdict (a win or a replay) is held until
        /// it has been the same on every look for the settle time. Anything else - a round's end, and the "last team in the room" write (A3) even
        /// while sudden death is being judged - is never held back; and nothing to write sends nothing. (Holding everything while
        /// judging would swallow the A3 write, and an emptied tied pair would replay for ever.)</summary>
        public bool ShouldWrite(DominionWrite write, bool judgingSuddenDeath, float nowSeconds, float settleSeconds)
        {
            string verdictKey = judgingSuddenDeath ? DominionRoomWrites.SuddenDeathVerdictKey(write) : null;
            if (verdictKey == null)
            {
                Reset();
                return write != null;
            }
            return Settled(verdictKey, nowSeconds, settleSeconds);
        }

        /// <summary>Forget what was seen (a new sudden death, a new master, leaving the room).</summary>
        public void Reset() => key = null;
    }
}
