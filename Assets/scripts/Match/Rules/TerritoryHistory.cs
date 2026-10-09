namespace Overpower.Match
{
    /// <summary>Which snapshot an ownership event must compare against. Several zones can change in one snapshot, and every one of their events needs the
    /// snapshot from BEFORE it, not the one the first event already moved on to.</summary>
    public sealed class TerritoryHistory
    {
        private TerritorySnapshot last;
        private TerritorySnapshot handled;
        private TerritorySnapshot before;

        /// <summary>The newest snapshot the game has shown; call it every frame.</summary>
        public void Seen(TerritorySnapshot snapshot) => last = snapshot;

        /// <summary>The snapshot before <paramref name="snapshot"/>, whichever of its zones is asking. Null when nothing was seen before it.</summary>
        public TerritorySnapshot BeforeEventIn(TerritorySnapshot snapshot)
        {
            if (snapshot != handled)
            {
                before = last;
                handled = snapshot;
            }
            last = snapshot;
            return before;
        }
    }
}
