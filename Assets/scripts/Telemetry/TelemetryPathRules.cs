namespace Overpower.Telemetry
{
    /// <summary>Which root a client's match logs land under. GameFolder is the game/project folder
    /// itself (the parent of Application.dataPath - a build's own folder, or the Unity project folder
    /// in the Editor); Documents is Documents\OverPower; Legacy is the persistentDataPath location (AppData on Windows) - only
    /// used if neither of the first two could be created and written to.</summary>
    public enum MatchLogLocation
    {
        GameFolder,
        Documents,
        Legacy,
    }

    /// <summary>Pure decision for where match logs go (testers could not find the persistentDataPath/AppData location).
    /// <see cref="TelemetryPaths"/> is the impure half: it tries each candidate folder and feeds the pass/fail result in here, so
    /// this fallback order is unit tested with no IO.</summary>
    public static class TelemetryPathRules
    {
        /// <summary>Game/project folder first (where testers look), then Documents\OverPower (e.g. the game sits in Program Files),
        /// then the persistentDataPath location, so telemetry is never lost.</summary>
        public static MatchLogLocation Choose(bool gameFolderWritable, bool documentsWritable)
        {
            if (gameFolderWritable) return MatchLogLocation.GameFolder;
            if (documentsWritable) return MatchLogLocation.Documents;
            return MatchLogLocation.Legacy;
        }
    }
}
