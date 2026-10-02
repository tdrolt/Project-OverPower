namespace Overpower.Lobby
{
    /// <summary>
    /// Stops the same room write being sent twice in one instant. The master's seat-freeing sweep runs from two callbacks that can fire
    /// in the same frame (a player leaving, and the hand-over of mastership that follows); both would send the identical write. The
    /// second is dropped; the same write is allowed again after the window, so a write that really got lost is retried. Pure: the
    /// caller passes the clock.
    /// </summary>
    public sealed class RepeatWriteGate
    {
        private readonly float window;
        private string lastSignature;
        private float lastSentAt;

        public RepeatWriteGate(float windowSeconds) => window = windowSeconds;

        /// <summary>True the first time a signature is seen, and again once the window has passed since it was last sent.</summary>
        public bool ShouldSend(string signature, float now)
        {
            if (signature == lastSignature && now - lastSentAt < window) return false;
            lastSignature = signature;
            lastSentAt = now;
            return true;
        }
    }
}
