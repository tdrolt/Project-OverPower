namespace Overpower.Combat
{
    /// <summary>
    /// Which AoE zone ticks are due by a given moment, counted from the zone's own placement, so
    /// "no early ticks, no repeats, a late start never runs extra or skipped ticks" is provable
    /// without a scene or PhotonNetwork.
    ///
    /// Time comes from the zone's own age (NetworkedDeployable.Age plus real time elapsed on this
    /// client, the secondsSincePlaced pattern Mine.cs uses), never a local stopwatch: every client
    /// applies tick k at spawn time + k * tickSeconds, so a late joiner never runs extra or early
    /// ticks. This class never reads a clock itself.
    ///
    /// ConsumeDueTicks may pay out several ticks in one call, but only for a mid-life HITCH on a client
    /// that has been evaluating right along (the victim really stood in the zone for the whole span).
    /// Ticks already behind a late joiner are skipped through initialAgeSeconds.
    /// </summary>
    public sealed class ZoneTickSchedule
    {
        private readonly float tickSeconds;
        private readonly int totalTicks;

        // 1-based: tick k lands at k * tickSeconds after placement.
        private int nextTick;

        /// <param name="tickSeconds">Seconds between ticks.</param>
        /// <param name="totalTicks">How many ticks the zone's whole life is worth.</param>
        /// <param name="initialAgeSeconds">
        /// How old the zone already was (NetworkedDeployable.Age, read once at OnPlaced) when THIS
        /// client's schedule was built. A late joiner's zone copy can already be several ticks old;
        /// this client's players were never simulated for those moments, so they must never fire,
        /// unlike a HITCH (see the class comment). 0 for an on-time client.
        /// </param>
        public ZoneTickSchedule(float tickSeconds, int totalTicks, float initialAgeSeconds = 0f)
        {
            this.tickSeconds = tickSeconds;
            this.totalTicks = totalTicks;

            // Skip every tick already behind this client before it ever looked.
            nextTick = (int)(initialAgeSeconds / tickSeconds) + 1;
        }

        /// <summary>True once every tick has been consumed - the owner's cue to destroy the zone (tick
        /// on every client, destroy on the owner only). Also true from construction when
        /// initialAgeSeconds already put nextTick past totalTicks.</summary>
        public bool IsComplete => nextTick > totalTicks;

        /// <summary>
        /// Call every frame with seconds elapsed since the zone was actually placed. Returns how many
        /// ticks are newly due (0 most frames; more than 1 only for a hitch mid-life) and advances past
        /// them, so a tick is never reported twice.
        /// </summary>
        public int ConsumeDueTicks(float secondsSincePlaced)
        {
            int due = 0;

            while (!IsComplete && secondsSincePlaced >= nextTick * tickSeconds)
            {
                due++;
                nextTick++;
            }

            return due;
        }
    }
}
