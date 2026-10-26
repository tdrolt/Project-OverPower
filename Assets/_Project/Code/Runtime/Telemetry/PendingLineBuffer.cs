using System.Collections.Generic;

namespace Overpower.Telemetry
{
    /// <summary>
    /// The lines logged before this client's file opens (the lobby's markers, joins, the match clock's first
    /// lines). It holds at most <c>cap</c> lines; when full it drops the OLDEST line to make room, so the newest (the start marker, the first
    /// team lines) always survive, and it counts what it dropped so the file can say how many.
    /// </summary>
    public sealed class PendingLineBuffer
    {
        private readonly int cap;
        private readonly List<string> lines = new List<string>();

        public PendingLineBuffer(int cap) => this.cap = cap < 1 ? 1 : cap;

        /// <summary>How many lines are waiting.</summary>
        public int Count => lines.Count;

        /// <summary>How many older lines were dropped to keep the newest ones.</summary>
        public int Dropped { get; private set; }

        /// <summary>The waiting lines, oldest first.</summary>
        public IReadOnlyList<string> Lines => lines;

        public void Add(string line)
        {
            if (lines.Count >= cap)
            {
                lines.RemoveAt(0);
                Dropped++;
            }
            lines.Add(line);
        }

        public void Clear()
        {
            lines.Clear();
            Dropped = 0;
        }
    }
}
