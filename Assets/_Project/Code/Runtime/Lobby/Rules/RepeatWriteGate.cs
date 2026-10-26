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
        private readonly System.Collections.Generic.Dictionary<string, float> sentAt = new System.Collections.Generic.Dictionary<string, float>();
        private readonly System.Collections.Generic.List<string> expired = new System.Collections.Generic.List<string>();

        public RepeatWriteGate(float windowSeconds) => window = windowSeconds;

        /// <summary>True the first time a signature is seen, and again once the window has passed since it was last sent. Every write sent inside
        /// the window is remembered (A, then B, then A again is still a repeat of A), and a write older than the window is forgotten.</summary>
        public bool ShouldSend(string signature, float now)
        {
            expired.Clear();
            foreach (var pair in sentAt)
                if (now - pair.Value >= window) expired.Add(pair.Key);
            foreach (string key in expired) sentAt.Remove(key);

            if (sentAt.ContainsKey(signature)) return false;
            sentAt[signature] = now;
            return true;
        }
    }
}
