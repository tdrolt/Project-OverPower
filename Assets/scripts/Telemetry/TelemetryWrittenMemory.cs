using System.Collections.Generic;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Which one-per-match lines this process has already written. A mode's map is a new scene, so a new MatchTelemetry opens the same match file again
    /// (appending): without this memory the file would get a second session header and a second "join" line for the same player. Keyed by room and
    /// actor; forgotten when the room is left, so a player who really joins another match (or the same one again) is written again.
    /// </summary>
    public sealed class TelemetryWrittenMemory
    {
        /// <summary>The process-wide memory the game uses (the tests make their own instances).</summary>
        public static readonly TelemetryWrittenMemory Process = new TelemetryWrittenMemory();

        private readonly HashSet<string> written = new HashSet<string>();

        /// <summary>True the first time a line of this kind is asked for for this room and actor, false afterwards. The room's name is the key
        /// (the match id is not known yet when a player first joins); a missing name is always "first".</summary>
        public bool FirstTime(string kind, string roomName, int actor)
        {
            if (string.IsNullOrEmpty(roomName)) return true;
            return written.Add(kind + "|" + roomName + "|" + actor);
        }

        /// <summary>True when a line of this kind for this room and actor has already been WRITTEN to the match file in this process. A line that was only queued
        /// (the file was not open yet) is not written: the scene that queued it may be gone before the file ever opens.</summary>
        public bool WasWritten(string kind, string roomName, int actor) => !string.IsNullOrEmpty(roomName) && written.Contains(kind + "|" + roomName + "|" + actor);

        /// <summary>Notes that a line of this kind for this room and actor is now in the match file.</summary>
        public void MarkWritten(string kind, string roomName, int actor)
        {
            if (!string.IsNullOrEmpty(roomName)) written.Add(kind + "|" + roomName + "|" + actor);
        }

        public void ForgetAll() => written.Clear();
    }
}
