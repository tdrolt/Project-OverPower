namespace Overpower.Combat
{
    /// <summary>
    /// Which AoE zone ticks are due by a given moment, counted from the zone's own placement - pulled
    /// out of AoeZone (Task 1.11b) so "no early ticks, no repeats, a late start never runs extra or
    /// skipped ticks" is provable without a scene or PhotonNetwork.
    ///
    /// TIME COMES FROM THE ZONE'S OWN AGE, NOT A LOCAL STOPWATCH - the Task 1.11 addendum: "every
    /// client applies tick k at spawnServerTime + k * tickSeconds ... using the timestamp from Age, so
    /// a late joiner's client never runs extra or early ticks." This class only answers "how many
    /// ticks are due" once told how many seconds have really passed since the zone was placed
    /// (NetworkedDeployable.Age plus real time elapsed on this client - the exact secondsSincePlaced
    /// pattern Mine.cs already uses for its own arm delay); it never reads a clock itself.
    ///
    /// A LATE CALL PAYS OUT EVERY TICK IT OWES, NOT JUST THE NEXT ONE - ConsumeDueTicks loops rather
    /// than checking a single boundary, so a client that only starts ticking after tick 3's moment has
    /// already passed (a late joiner, or one slow frame) is handed 1, 2 and 3 together the first time
    /// it asks, and never asked for again - "no repeats" falls out of nextTick only ever advancing.
    /// </summary>
    public sealed class ZoneTickSchedule
    {
        private readonly float tickSeconds;
        private readonly int totalTicks;

        // The next tick number still owed, 1-based to match the addendum's own "tick k, k = 1..6"
        // wording - tick k lands at k * tickSeconds after placement.
        private int nextTick = 1;

        public ZoneTickSchedule(float tickSeconds, int totalTicks)
        {
            this.tickSeconds = tickSeconds;
            this.totalTicks = totalTicks;
        }

        /// <summary>True once every tick has been consumed - the owner's cue to destroy the zone
        /// (Task 1.11 addendum: "tick on every client, destroy on the owner only").</summary>
        public bool IsComplete => nextTick > totalTicks;

        /// <summary>
        /// Call every frame with seconds elapsed since the zone was actually placed. Returns how many
        /// ticks are newly due this call (0 most frames; more than 1 only for a late-starting client
        /// catching up) and advances past every one of them, so the same tick is never reported twice.
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
