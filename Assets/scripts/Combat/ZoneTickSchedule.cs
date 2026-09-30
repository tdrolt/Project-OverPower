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
    /// A CALL CAN STILL PAY OUT SEVERAL TICKS TOGETHER - ConsumeDueTicks loops rather than checking a
    /// single boundary - but review fix (Task 1.11b): which ticks it is allowed to owe now depends on
    /// how old the zone already was the first time THIS client ever evaluated it (see initialAgeSeconds
    /// below). Only a HITCH - a frame spike that happens mid-life, on a client that has been evaluating
    /// the schedule right along - can still bundle up to N ticks into one call; that is still correct,
    /// because the victim really did stand in the zone for the whole span the hitch skipped over.
    /// </summary>
    public sealed class ZoneTickSchedule
    {
        private readonly float tickSeconds;
        private readonly int totalTicks;

        // The next tick number still owed, 1-based to match the addendum's own "tick k, k = 1..6"
        // wording - tick k lands at k * tickSeconds after placement.
        private int nextTick;

        /// <param name="tickSeconds">Seconds between ticks.</param>
        /// <param name="totalTicks">How many ticks the zone's whole life is worth.</param>
        /// <param name="initialAgeSeconds">
        /// How old the zone already was (NetworkedDeployable.Age, read once at OnPlaced) the moment
        /// THIS client's schedule was built. Review fix (Task 1.11b): a late joiner's zone copy can
        /// already be several ticks old before this client ever evaluates it - this client's players
        /// were never simulated for those already-passed moments, so they must never fire, unlike a
        /// HITCH (see the class comment), which happens mid-life on a client that has been evaluating
        /// right along and so still owes every tick the frame spike spans. Defaults to 0, which is
        /// exactly the old always-nextTick-starts-at-1 behaviour for an on-time client.
        /// </param>
        public ZoneTickSchedule(float tickSeconds, int totalTicks, float initialAgeSeconds = 0f)
        {
            this.tickSeconds = tickSeconds;
            this.totalTicks = totalTicks;

            // Skip past every tick whose moment was already behind this client before it ever looked.
            // At initialAgeSeconds 0 (the normal, on-time case) this is floor(0) + 1 = 1, identical to
            // the old hardcoded starting value.
            nextTick = (int)(initialAgeSeconds / tickSeconds) + 1;
        }

        /// <summary>True once every tick has been consumed - the owner's cue to destroy the zone
        /// (Task 1.11 addendum: "tick on every client, destroy on the owner only"). Also true from
        /// construction if initialAgeSeconds already put nextTick past totalTicks - a client whose
        /// first evaluation happens after the zone's entire life has already elapsed owes nothing.</summary>
        public bool IsComplete => nextTick > totalTicks;

        /// <summary>
        /// Call every frame with seconds elapsed since the zone was actually placed. Returns how many
        /// ticks are newly due this call (0 most frames; more than 1 only for a hitch mid-life catching
        /// up - see the class comment) and advances past every one of them, so the same tick is never
        /// reported twice.
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
