using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Overpower.Telemetry
{
    /// <summary>Buffered append-only writer for one match's `.jsonl` file. Not a MonoBehaviour - it
    /// owns no Unity lifecycle of its own, so MatchTelemetry (which does) decides when Flush/Close
    /// happen and can unit-test this class without a scene.
    ///
    /// Every write goes through an in-memory buffer first; nothing touches disk until Flush. That
    /// keeps `Log` calls from a hot path (a hit, a shot) cheap, and matches the design's Principle 1:
    /// telemetry is exception-safe, so an IO error disables it for the rest of the session rather than
    /// throwing into gameplay code.</summary>
    public sealed class TelemetryWriter
    {
        private readonly List<string> buffered = new List<string>();
        private string path;

        /// <summary>True once Open or Flush has hit an exception. Every later call becomes a no-op -
        /// this is the "an IO error disables telemetry for the session" rule from the design doc,
        /// applied at the lowest level so nothing above has to remember to check it everywhere.</summary>
        public bool Disabled { get; private set; }

        /// <summary>True once Open has succeeded and Close has not yet been called.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>How many lines have actually been written to disk so far (i.e. flushed), not
        /// counting whatever still sits in the buffer. Diagnostic only - the F1 status label and the
        /// T2 verification step both read this.</summary>
        public int LineCount { get; private set; }

        /// <summary>Creates the file's directory (if needed) and remembers its path. Does not touch
        /// the file itself - the first Flush creates it via File.AppendAllLines.</summary>
        public void Open(string filePath)
        {
            if (Disabled) return;

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                path = filePath;
                IsOpen = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Telemetry] could not open '{filePath}' for writing - telemetry disabled for this session: {e.Message}");
                Disabled = true;
            }
        }

        /// <summary>Appends one line to the in-memory buffer. Does nothing if disabled or not open -
        /// callers never need to check either themselves.</summary>
        public void Write(string line)
        {
            if (Disabled || !IsOpen) return;
            buffered.Add(line);
        }

        /// <summary>Appends every buffered line to disk in one call and clears the buffer. Any
        /// exception here disables telemetry for the rest of the session (Principle 1) - the buffered
        /// lines are dropped rather than retried, since retrying the same failing disk on every future
        /// flush would just repeat the same error forever.</summary>
        public void Flush()
        {
            if (Disabled || !IsOpen || buffered.Count == 0) return;

            try
            {
                File.AppendAllLines(path, buffered);
                LineCount += buffered.Count;
                buffered.Clear();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Telemetry] could not write to '{path}' - telemetry disabled for this session: {e.Message}");
                Disabled = true;
                buffered.Clear();
            }
        }

        /// <summary>Flushes whatever remains, then marks the writer closed. Open may be called again
        /// afterwards (a second match in the same session) unless Disabled is set.</summary>
        public void Close()
        {
            Flush();
            IsOpen = false;
        }
    }
}
