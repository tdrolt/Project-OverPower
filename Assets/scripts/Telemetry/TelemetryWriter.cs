using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Overpower.Telemetry
{
    /// <summary>Buffered append-only writer for one match's `.jsonl` file. Not a MonoBehaviour, so MatchTelemetry decides when
    /// Flush/Close happen and this can be unit-tested without a scene. Nothing touches disk until Flush, which keeps `Log` calls from
    /// a hot path (a hit, a shot) cheap. Telemetry is exception-safe: an IO error disables it for the rest of the session rather
    /// than throwing into gameplay code.</summary>
    public sealed class TelemetryWriter
    {
        private readonly List<string> buffered = new List<string>();
        private string path;

        /// <summary>True once Open or Flush has hit an exception; every later call is a no-op, so nothing above has to check it.</summary>
        public bool Disabled { get; private set; }

        public bool IsOpen { get; private set; }

        /// <summary>Lines flushed to disk so far (not the buffer). Diagnostic: the F1 status label reads it.</summary>
        public int LineCount { get; private set; }

        /// <summary>Creates the file's directory and remembers the path; the first Flush creates the file itself.</summary>
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

        public void Write(string line)
        {
            if (Disabled || !IsOpen) return;
            buffered.Add(line);
        }

        /// <summary>Any exception here disables telemetry for the session; the buffered lines are dropped, not retried, since
        /// retrying the same failing disk on every flush would repeat the error forever.</summary>
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

        /// <summary>Flushes, then marks the writer closed. Open may be called again (a second match in the same session) unless Disabled.</summary>
        public void Close()
        {
            Flush();
            IsOpen = false;
        }
    }
}
