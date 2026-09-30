namespace Overpower.Telemetry
{
    /// <summary>Which root a client's match logs land under. GameFolder is the game/project folder
    /// itself (the parent of Application.dataPath - a build's own folder, or the Unity project folder
    /// in the Editor); Documents is Documents\OverPower; Legacy is the old persistentDataPath location
    /// (AppData on Windows) - only used if neither of the first two could be created and written to.</summary>
    public enum MatchLogLocation
    {
        GameFolder,
        Documents,
        Legacy,
    }

    /// <summary>Pure decision for where match logs go (2026-09-27 designer change: testers complained
    /// the old location, persistentDataPath/AppData, was too hard to find). <see cref="TelemetryPaths"/>
    /// is the impure half: it actually tries to create and write into each candidate folder and feeds
    /// the pass/fail result in here. This class does no IO at all, so the fallback order can be unit
    /// tested without touching a filesystem.</summary>
    public static class TelemetryPathRules
    {
        /// <summary>Game/project folder first (where testers will actually look); Documents\OverPower
        /// next (e.g. the game sits in Program Files and can't be written to); the old persistentDataPath
        /// location last, so telemetry is never simply lost.</summary>
        public static MatchLogLocation Choose(bool gameFolderWritable, bool documentsWritable)
        {
            if (gameFolderWritable) return MatchLogLocation.GameFolder;
            if (documentsWritable) return MatchLogLocation.Documents;
            return MatchLogLocation.Legacy;
        }
    }
}
