namespace Overpower.Bots
{
    /// <summary>
    /// Capturing comes first: a free zone (one its team may take now that is no capital, no Dominion centre, with no enemy known on it) is
    /// taken before anything but its threatened capital, the urgent centre and a zone of its team being taken. A bot standing on a free zone
    /// stays until it is its team's or an enemy steps on, and while it goes for one it fights only an enemy that hit it or is in its weapon's
    /// range.
    /// </summary>
    public static class BotCaptureRules
    {
        public static bool FreeZone(bool mayCapture, bool capitalOrCentre, bool enemyOnIt) => mayCapture && !capitalOrCentre && !enemyOnIt;

        /// <summary>Whether a bot inside a zone's circle stays there: while the zone is still free.</summary>
        public static bool StayOnZone(bool standing, bool freeZone) => standing && freeZone;

        /// <summary>Whether a bot going for (or standing on) a free zone turns to fight its target.</summary>
        public static bool EngageOnFreeZone(bool hitMeLately, float targetMetres, float weaponRangeMetres) =>
            hitMeLately || targetMetres <= weaponRangeMetres;
    }
}
