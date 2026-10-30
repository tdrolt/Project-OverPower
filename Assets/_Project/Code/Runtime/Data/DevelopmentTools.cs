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

        /// <summary>The closest the scroll wheel may bring the camera (a multiple of its normal distance). A player's build zooms out
        /// only: never closer than normal (1). A development run and a spectator seat (whose key bar offers the wheel) get the camera's
        /// own closest.</summary>
        public static float ClosestScrollZoom(bool developmentRun, bool spectatorSeat, float cameraClosest) =>
            developmentRun || spectatorSeat ? cameraClosest : System.Math.Max(1f, cameraClosest);

        /// <summary>The "F1: debug log" hint in the corner. F1 itself opens the log in every build.</summary>
        public static bool ShowsDebugLogHint(bool developmentRun) => developmentRun;
    }
}
