namespace Overpower.Data
{
    /// <summary>
    /// Tools for looking at the game while building it, which exist only in a development run (the Editor and the development builds,
    /// Debug.isDebugBuild): a player's build has none of them.
    /// </summary>
    public static class DevelopmentTools
    {
        /// <summary>The training dummies and their panel: GameplayConfig's Test Range switch, and only in a development run.</summary>
        public static bool TestRangeOn(bool switchOn, bool developmentRun) => switchOn && developmentRun;

        /// <summary>The scroll-wheel zoom: any development run; in a player's build only a spectator seat, whose key bar offers it.</summary>
        public static bool ScrollZoomOn(bool developmentRun, bool spectatorSeat) => developmentRun || spectatorSeat;

        /// <summary>The "F1: debug log" hint in the corner. F1 itself opens the log in every build.</summary>
        public static bool ShowsDebugLogHint(bool developmentRun) => developmentRun;
    }
}
