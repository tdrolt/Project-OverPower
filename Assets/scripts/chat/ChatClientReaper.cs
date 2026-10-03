using System.Collections.Generic;
using UnityEngine;
using Photon.Chat;

/// <summary>Lobby Task 11: a chat connection that is being closed. A ChatClient only sends its disconnect (and the server only drops the session)
/// while its Service runs, and the PhotonChat that owned it is switched off (or destroyed) at that moment. This object, kept across scenes,
/// keeps calling Service until the client is really disconnected. Without it the old session lingers on the Photon Chat server while the same
/// user id connects again for the next lobby, and the lingering session's timeout later takes the new session's subscriptions with it
/// (seen in the first run of the Task 11 check: after leaving a lobby and joining another, nothing arrived either way).</summary>
public sealed class ChatClientReaper : MonoBehaviour
{
    private const float GiveUpSeconds = 8f;
    private static ChatClientReaper instance;
    private readonly List<ChatClient> closing = new List<ChatClient>();
    private readonly List<float> startedAt = new List<float>();

    /// <summary>True while some connection is still closing: the next connection waits for it.</summary>
    public static bool Busy => instance != null && instance.closing.Count > 0;

    public static void Close(ChatClient client)
    {
        if (client == null) return;
        client.Disconnect();
        if (instance == null)
        {
            var go = new GameObject("Chat client reaper");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ChatClientReaper>();
        }
        instance.closing.Add(client);
        instance.startedAt.Add(Time.unscaledTime);
    }

    private void Update()
    {
        for (int i = closing.Count - 1; i >= 0; i--)
        {
            ChatClient client = closing[i];
            client.Service();
            bool done = client.State == ChatState.Disconnected || client.State == ChatState.Uninitialized;
            bool gaveUp = !done && Time.unscaledTime - startedAt[i] > GiveUpSeconds;
            if (done || gaveUp)
            {
                if (gaveUp) client.StopThread(); // the client's own service thread must not outlive the object we drop
                closing.RemoveAt(i);
                startedAt.RemoveAt(i);
            }
        }
    }
}
