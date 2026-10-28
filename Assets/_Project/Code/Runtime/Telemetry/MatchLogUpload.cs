using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Fire and forget: one POST of plain text, nothing waits for it, the reply is only written to the console. A static owner keeps
    /// each request alive until it completes, so it survives the scene rebuild that follows a leave (a request nobody holds can be
    /// cut off by the garbage collector). The redirect Google answers with is not followed (desktop): it would resend nothing useful.
    /// </summary>
    public static class MatchLogUpload
    {
        private static readonly HashSet<UnityWebRequest> inFlight = new HashSet<UnityWebRequest>();

        public static void Send(string url, string text, int timeoutSeconds)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(text)) { contentType = MatchLogUploadRules.ContentType },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds,
                redirectLimit = 0,
            };
            inFlight.Add(request);
            request.SendWebRequest().completed += _ =>
            {
                if (MatchLogUploadRules.IsDelivered(request.responseCode))
                    Debug.Log($"[MatchLogUpload] sent ({text.Length} characters, reply {request.responseCode})");
                else
                    Debug.LogWarning($"[MatchLogUpload] not sent: reply {request.responseCode}, {request.error}");
                inFlight.Remove(request);
                request.Dispose();
            };
        }
    }
}
