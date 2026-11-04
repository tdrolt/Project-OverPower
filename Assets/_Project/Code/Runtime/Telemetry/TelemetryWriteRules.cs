namespace Overpower.Telemetry
{
    /// <summary>How a log file is written. Opening and closing a growing file every flush is what made a long match stutter (each close lets
    /// the virus scanner read the whole file again), so the file stays open for the match. A browser build keeps one short append per flush:
    /// its files live in the page's storage, which is only saved when a file is closed.</summary>
    public static class TelemetryWriteRules
    {
        public static bool KeepsFileOpen(bool webGlPlayer) => !webGlPlayer;

        /// <summary>The first sample of a body that is not this client's own player (a bot the host plays) comes this many seconds early, a
        /// share of the interval picked by its actor number, so the host's bots do not all sample and flush in the same frame. The interval
        /// between two samples stays the same.</summary>
        public static float FirstSampleHeadStart(int actor, float intervalSeconds)
        {
            if (intervalSeconds <= 0f) return 0f;
            double share = (System.Math.Abs((long)actor) * 0.6180339887498949) % 1.0;
            return (float)(share * intervalSeconds);
        }

        /// <summary>True when the points just read differ from the last ones logged (a stage write carries the same points again): only then
        /// is a `points` line worth writing. Nothing read, nothing to log.</summary>
        public static bool PointsDiffer(int[] lastLogged, int[] now)
        {
            if (now == null) return false;
            if (lastLogged == null || lastLogged.Length != now.Length) return true;
            for (int i = 0; i < now.Length; i++)
                if (lastLogged[i] != now[i]) return true;
            return false;
        }
    }
}
