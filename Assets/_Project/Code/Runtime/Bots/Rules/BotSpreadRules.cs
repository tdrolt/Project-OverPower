namespace Overpower.Bots
{
    /// <summary>
    /// Spreading out: at the start of a match or round, and when its team is pushed back to its Tier II zones with nobody to defend
    /// against, a zone another bot of its team already heads for counts further, and the zone picked is kept a moment so two bots do not
    /// keep swapping.
    /// </summary>
    public static class BotSpreadRules
    {
        /// <summary>Pushed back: its team holds no zone above Tier II.</summary>
        public static bool PushedBack(int highestTierHeld) => highestTierHeld <= 2;

        public static bool Spreading(bool atStart, bool pushedBack, bool zoneOfOursUnderAttack) => atStart || (pushedBack && !zoneOfOursUnderAttack);

        /// <summary>Whether to keep the zone picked secondsHeld ago instead of a new pick: for holdSeconds, while that zone is still open to
        /// it and nothing urgent came up.</summary>
        public static bool HoldZone(float secondsHeld, float holdSeconds, bool stillOpen, bool urgent) =>
            secondsHeld < holdSeconds && stillOpen && !urgent;
    }
}
