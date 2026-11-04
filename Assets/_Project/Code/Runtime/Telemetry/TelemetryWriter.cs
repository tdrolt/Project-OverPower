using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Overpower.Telemetry
{
    /// <summary>Buffered append-only writer for one match's `.jsonl` file. Not a MonoBehaviour, so MatchTelemetry decides when
    /// Flush/Close happen and this can be unit-tested without a scene. Nothing touches disk until Flush, which keeps `Log` calls from
    /// a hot path (a hit, a shot) cheap. Telemetry is exception-safe: an IO error disables it for the rest of the session rather
    /// than throwing into gameplay code.
    ///
    /// Outside a browser build the file stays open between flushes (TelemetryWriteRules.KeepsFileOpen); Release closes it so another
    /// reader (the zip, the upload) can open it, and the next Flush opens it again.</summary>
    public sealed class TelemetryWriter
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        private const bool WebGlPlayer = true;
#else
        private const bool WebGlPlayer = false;
#endif

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly List<string> buffered = new List<string>();
        private readonly StringBuilder chunk = new StringBuilder(4096);
        private byte[] bytes = new byte[8192];
        private string path;
        private FileStream stream;

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
                if (TelemetryWriteRules.KeepsFileOpen(WebGlPlayer))
                    AppendThroughOpenFile();
                else
                    File.AppendAllLines(path, buffered);
                LineCount += buffered.Count;
                buffered.Clear();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Telemetry] could not write to '{path}' - telemetry disabled for this session: {e.Message}");
                Disabled = true;
                buffered.Clear();
                CloseFile();
            }
        }

        private void AppendThroughOpenFile()
        {
            if (stream == null)
                stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);

            chunk.Clear();
            foreach (string line in buffered)
                chunk.Append(line).Append(Environment.NewLine);
            string text = chunk.ToString();
            int count = Utf8NoBom.GetByteCount(text);
            if (bytes.Length < count)
                bytes = new byte[Math.Max(count, bytes.Length * 2)];
            Utf8NoBom.GetBytes(text, 0, text.Length, bytes, 0);

            // The end is looked up for every chunk: a mode's map is a new scene whose writer appends to the same file, and the old scene's
            // writer may still have the file open when the new one first writes.
            stream.Seek(0, SeekOrigin.End);
            stream.Write(bytes, 0, count);
            stream.Flush();
        }

        /// <summary>Flushes and lets go of the file, so a reader that does not share it (the zip, the upload) can open it; the writer stays
        /// open and the next Flush opens the file again.</summary>
        public void Release()
        {
            Flush();
            CloseFile();
        }

        /// <summary>Flushes, then marks the writer closed. Open may be called again (a second match in the same session) unless Disabled.</summary>
        public void Close()
        {
            Flush();
            CloseFile();
            IsOpen = false;
        }

        private void CloseFile()
        {
            if (stream == null) return;
            try { stream.Dispose(); }
            catch (Exception e) { Debug.LogWarning($"[Telemetry] could not close '{path}': {e.Message}"); }
            stream = null;
        }
    }
}
