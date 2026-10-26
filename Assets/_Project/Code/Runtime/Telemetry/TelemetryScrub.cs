using Photon.Pun;

namespace Overpower.Telemetry
{
    /// <summary>
    /// ONE scrub, shared by ConsoleLineRule's console lines and MatchTelemetry.LogChat's chat lines: a chat message can contain a
    /// Photon App ID (a pasted connection error, say) and must not reach the log verbatim. Never write the Photon App IDs into any
    /// log (project rule) - this is the one place that promise is kept.
    /// </summary>
    public static class TelemetryScrub
    {
        /// <summary>Every Photon App ID this build could connect with (Realtime and Chat), read fresh from
        /// PhotonNetwork.PhotonServerSettings.AppSettings. Null when no PhotonServerSettings is assigned; Apply() treats null targets
        /// as "scrub nothing". Cheap, so every caller reads it fresh rather than sharing a cached array.</summary>
        public static string[] AppIdTargets()
        {
            ServerSettings settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null || settings.AppSettings == null)
                return null;

            return new[] { settings.AppSettings.AppIdRealtime, settings.AppSettings.AppIdChat };
        }

        /// <summary>Replaces every occurrence of every non-empty target in text with "&lt;app id&gt;". Never returns null, so callers
        /// need no null-coalesce afterwards.</summary>
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
