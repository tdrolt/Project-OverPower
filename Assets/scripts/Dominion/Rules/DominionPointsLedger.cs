namespace Overpower.Dominion
{
    /// <summary>The master's memory of what it last wrote to dPts and dCtr while the echo is still on its way (the lastWritten +
    /// writesAwaitingEcho pattern of BuildingManager and MatchDirector): the next write builds on it, not on the room's older copy. Its wait
    /// is its own - it never holds the stage writer back, and the stage writer's wait never holds it back.</summary>
    public sealed class DominionPointsLedger
    {
        private int[] written;
        private int writtenCentreMs;
        private int writtenSeq;
        private int pending;
        private float lastSentAt;

        public void Sent(int[] points, int centreMs, int seq, float nowSeconds)
        {
            written = points != null ? (int[])points.Clone() : null;
            writtenCentreMs = centreMs;
            writtenSeq = seq;
            pending++;
            lastSentAt = nowSeconds;
        }

        /// <summary>An echo of one of our writes came back.</summary>
        public void Echoed() { if (pending > 0) pending--; }

        /// <summary>Forget everything: a new stage, a new master, a new room.</summary>
        public void Reset()
        {
            written = null;
            pending = 0;
        }

        /// <summary>True while a write's echo is awaited (and not given up on after the timeout: a refused check-and-set never echoes).</summary>
        public bool Pending(float nowSeconds, float timeoutSeconds) => pending > 0 && written != null && nowSeconds - lastSentAt <= timeoutSeconds;

        /// <summary>The values to build the next write on: the last write while its echo is pending, else the room's.</summary>
        public void Basis(float nowSeconds, float timeoutSeconds, int[] roomPoints, int roomCentreMs, int roomSeq,
                          out int[] points, out int centreMs, out int seq)
        {
            if (Pending(nowSeconds, timeoutSeconds)) { points = (int[])written.Clone(); centreMs = writtenCentreMs; seq = writtenSeq; }
            else { points = roomPoints != null ? (int[])roomPoints.Clone() : new int[DominionKeys.TeamSlots]; centreMs = roomCentreMs; seq = roomSeq; }
        }
    }
}
