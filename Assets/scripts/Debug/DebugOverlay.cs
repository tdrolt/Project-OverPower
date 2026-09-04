using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-screen log overlay, so a playtester running a BUILD can see diagnostics and send them back.
/// Debug.Log is invisible in a build unless someone digs out Player.log, which testers will not do.
///
/// F1 toggles the overlay. F2 copies everything it has captured to the clipboard, ready to paste
/// straight into Discord.
///
/// Self-installing: no scene setup, no prefab, nothing to remember before making a build.
/// Captures anything containing <see cref="Filter"/>, plus every error and exception.
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    /// Substring a log line must contain to be captured. Set to "" to capture everything.
    const string Filter = "[VIS]";

    const int MaxLines = 60;
    const KeyCode ToggleKey = KeyCode.F1;
    const KeyCode CopyKey = KeyCode.F2;

    static DebugOverlay instance;

    readonly List<string> lines = new List<string>();
    bool visible;
    Vector2 scroll;
    float copiedAt = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (instance != null)
            return;

        GameObject host = new GameObject("~DebugOverlay");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<DebugOverlay>();
    }

    void OnEnable()
    {
        Application.logMessageReceived += OnLog;
        Add($"overlay ready — {ToggleKey} toggles, {CopyKey} copies to clipboard");
    }

    void OnDisable()
    {
        Application.logMessageReceived -= OnLog;
    }

    void OnLog(string message, string stackTrace, LogType type)
    {
        bool isProblem = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        if (!isProblem && Filter.Length > 0 && !message.Contains(Filter))
            return;

        Add(isProblem ? $"{type}: {message}" : message);
    }

    void Add(string message)
    {
        lines.Add($"[{Time.realtimeSinceStartup,7:0.0}] {message}");
        if (lines.Count > MaxLines)
            lines.RemoveAt(0);

        scroll.y = float.MaxValue;
    }

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
            visible = !visible;

        if (Input.GetKeyDown(CopyKey))
        {
            GUIUtility.systemCopyBuffer = string.Join("\n", lines);
            copiedAt = Time.realtimeSinceStartup;
        }
    }

    void OnGUI()
    {
        if (!visible)
        {
            // Always show the hint, so a tester who has never been told still finds it.
            GUI.Label(new Rect(8, 8, 400, 20), $"{ToggleKey}: debug log");
            return;
        }

        float w = Mathf.Min(760f, Screen.width - 16f);
        float h = Mathf.Min(340f, Screen.height * 0.5f);

        GUI.Box(new Rect(8, 8, w, h), $"Debug log — {CopyKey} copies to clipboard");

        GUILayout.BeginArea(new Rect(16, 30, w - 16, h - 40));
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (string line in lines)
            GUILayout.Label(line);
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        if (Time.realtimeSinceStartup - copiedAt < 2f)
            GUI.Label(new Rect(16, h - 4, 400, 24), $"copied {lines.Count} lines");
    }
}
