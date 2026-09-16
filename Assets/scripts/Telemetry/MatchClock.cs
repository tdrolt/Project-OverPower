namespace Overpower.Telemetry
{
    /// <summary>Seconds since the match started, from Photon's server clock: the same on every client, so
    /// the report can line up every player's log. The server clock is an int that wraps, so the subtraction is
    /// unchecked. 0 means "not known yet" (Photon reads 0 right after connecting), which returns -1.</summary>
    public static class MatchClock
    {
        public static double Seconds(int nowMs, int startMs)
        {
            if (nowMs == 0 || startMs == 0) return -1.0;
            return unchecked(nowMs - startMs) / 1000.0;
        }
    }
}
