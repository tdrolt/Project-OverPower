using Photon.Pun;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Step 0 review fix (b), 2026-09-26: ONE scrub, shared by ConsoleLineRule's console lines and
    /// MatchTelemetry.LogChat's chat lines - before this, only the console listener scrubbed the
    /// Photon App IDs, and a chat message containing one (a player pasting a connection error, or a
    /// screenshot's OCR of one, however unlikely) would have gone into the log verbatim. Never write
    /// the Photon App IDs into any log (project rule) - this is the one place that promise is kept.
    /// </summary>
    public static class TelemetryScrub
    {
        /// <summary>Every Photon App ID this build could ever connect with - Realtime (PUN) and Chat,
        /// read fresh from PhotonNetwork.PhotonServerSettings.AppSettings. Null-guarded: a build with
        /// no PhotonServerSettings assigned (should never happen in this project) returns null rather
        /// than throwing, and Apply() below treats null targets as "scrub nothing". Cheap to call
        /// repeatedly (a couple of field reads off an already-loaded ScriptableObject, no disk or
        /// network I/O), so every caller reads it fresh rather than sharing one cached array.</summary>
        public static string[] AppIdTargets()
        {
            ServerSettings settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null || settings.AppSettings == null)
                return null;

            return new[] { settings.AppSettings.AppIdRealtime, settings.AppSettings.AppIdChat };
        }

        /// <summary>Replaces every occurrence of every non-empty target in text with "&lt;app id&gt;".
        /// Null/empty-safe: a null/empty text or a null targets array returns text unchanged (never
        /// null, so callers never need their own null-coalesce afterwards).</summary>
        public static string Apply(string text, string[] targets)
        {
            if (string.IsNullOrEmpty(text) || targets == null)
                return text ?? "";

            foreach (string target in targets)
                if (!string.IsNullOrEmpty(target))
                    text = text.Replace(target, "<app id>");
            return text;
        }
    }
}
