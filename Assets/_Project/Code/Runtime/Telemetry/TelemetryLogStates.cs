namespace Overpower.Telemetry
{
    /// <summary>
    /// The warm-up anchor (`phase` 0) and when it counts as written. The master logs it when it claims the match, before any file is open; a mode
    /// whose map is another scene drops that queued line with the lobby scene, so the scene that arrives inside the started room logs it again,
    /// unless one already reached this match's file in this process. Kept per room, under actor 0 (one anchor per match).
    /// </summary>
    public sealed class WarmupAnchorLine
    {
        public const string Kind = "warmup";
        private bool queued;

        /// <summary>A scene that arrives inside a room whose lobby has started (stage 1 or later): true when this client, as the master, must log
        /// the anchor here.</summary>
        public bool LogsOnArrival(TelemetryWrittenMemory memory, string roomName, bool isMaster, int lobbyStage) =>
            isMaster && lobbyStage >= 1 && !memory.WasWritten(Kind, roomName, 0);

        /// <summary>The anchor was just logged: into an open file it is written at once; otherwise it waits in the queue for the file.</summary>
        public void OnLogged(TelemetryWrittenMemory memory, string roomName, bool fileIsOpen)
        {
            if (fileIsOpen) memory.MarkWritten(Kind, roomName, 0);
            else queued = true;
        }

        /// <summary>The file has just opened and written its queue: the anchor counts as written only when it was in that queue.</summary>
        public void OnFileOpened(TelemetryWrittenMemory memory, string roomName)
        {
            if (!queued) return;
            memory.MarkWritten(Kind, roomName, 0);
            queued = false;
        }

        public void Reset() => queued = false;
    }

    /// <summary>
    /// One body's respawn shield as the log tells it: up, the hits and effects it stopped, and the one reason it went down. Only a shield that was
    /// logged as up is ever logged as down, and only once.
    /// </summary>
    public sealed class ShieldLogState
    {
        public const string RanOutReason = "ran out";
        public const string AttackedReason = "attacked";
        public const string LeftSpawnReason = "left spawn";
        public const string DiedReason = "died";
        public const string FreshStartReason = "fresh start";

        private bool up;
        private int blocked;

        public bool IsUp => up;

        public void Started()
        {
            up = true;
            blocked = 0;
        }

        public void Blocked()
        {
            if (up) blocked++;
        }

        /// <summary>True (once) when a logged shield goes down now; <paramref name="blockedWhileUp"/> is what it stopped.</summary>
        public bool Ended(out int blockedWhileUp)
        {
            blockedWhileUp = blocked;
            if (!up) return false;
            up = false;
            blocked = 0;
            return true;
        }

        /// <summary>The shield ran out by itself: it was logged up, nothing cleared it, and it reads down now.</summary>
        public bool RanOut(bool upNow, bool clearSent) => up && !upNow && !clearSent;
    }

    /// <summary>
    /// One centre-scan wave for the log, stepped every frame with what the scan sees. A wave is logged once, when its front leaves the arena, when
    /// the next wave cuts it off, or when the scan stops (the map shrank, the match ended). Caught counts the enemies this client's team spotted;
    /// it reads -1 for a wave that was never this client's team's.
    /// </summary>
    public sealed class ScanWaveLog
    {
        public readonly struct Wave
        {
            public readonly int StartMs;
            public readonly int Holder;
            public readonly int Caught;

            public Wave(int startMs, int holder, int caught)
            {
                StartMs = startMs;
                Holder = holder;
                Caught = caught;
            }
        }

        private bool open;
        private bool loggedAny;
        private int lastLoggedStartMs;
        private int startMs;
        private int holder = -1;
        private int caught;
        private bool seen;

        /// <summary>One frame. active: a live wave is on the clock. Returns true when a wave is finished and must be logged now.</summary>
        public bool Step(bool active, int scanStartMs, int holderTeam, bool travelling, bool seenByMyTeam, out Wave wave)
        {
            wave = default;
            bool finished = false;
            if (open && (!active || scanStartMs != startMs))
            {
                wave = Close();
                finished = true;
            }
            if (!active) return finished;

            if (!open && (!loggedAny || scanStartMs != lastLoggedStartMs))
            {
                open = true;
                startMs = scanStartMs;
                caught = 0;
                seen = false;
            }
            if (!open) return finished;

            holder = holderTeam;
            seen |= seenByMyTeam;
            if (!travelling && !finished)
            {
                wave = Close();
                finished = true;
            }
            return finished;
        }

        /// <summary>This client's team spotted one enemy in the wave now running.</summary>
        public void Caught()
        {
            if (open) caught++;
        }

        private Wave Close()
        {
            open = false;
            loggedAny = true;
            lastLoggedStartMs = startMs;
            return new Wave(startMs, holder, seen ? caught : -1);
        }
    }
}
