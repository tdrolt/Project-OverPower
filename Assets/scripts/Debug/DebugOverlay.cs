using System.Collections.Generic;
using UnityEngine;
using Overpower.UI;

/// <summary>
/// On-screen log overlay, so a playtester running a BUILD can see diagnostics and send them back.
/// Debug.Log is invisible in a build unless someone digs out Player.log, which testers will not do.
///
/// F1 toggles the overlay. F2 copies everything it has captured to the clipboard, ready to paste
/// straight into Discord.
///
/// Self-installing: no scene setup, no prefab, nothing to remember before making a build.
/// Captures anything containing <see cref="Filter"/>, plus every error and exception.
///
/// The log draws on the RIGHT, under the corner minimap, so it and the F1 test range panel (top-left) never
/// overlap (HUD step 6, Tudor 2026-09-17: "you can keep the dummy and console log opening next to each other
/// in the right side or the easy fix is to just bind them to different things" - moving the log is the better
/// half of that offer, since one key for "show me the tools" is fewer things for a playtester to remember).
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    /// Tags this overlay captures, on top of every error and exception.
    /// Deliberately short: each of these fires once per event, never per frame or per hit, so a
    /// whole match produces a readable page rather than a wall of text.
    ///   [VIS]   alive / dead visibility changes
    ///   [TEAM]  which team a player resolved to
    ///   [TOWER] tower ownership changes
    ///   [DMG]   damage refused, reported once per reason per player
    /// Set to an empty array to capture every log line.
    static readonly string[] Filters = { "[VIS]", "[TEAM]", "[TOWER]", "[DMG]" };

    const int MaxLines = 60;
    const KeyCode ToggleKey = KeyCode.F1;
    const KeyCode CopyKey = KeyCode.F2;

    // Used before any minimap exists to read the theme from - the menu, or the seconds before a player spawns.
    // They are what this overlay drew at before UiTheme had any say, so nothing gets worse in that case.
    const float FallbackWidthPixels = 420f;
    const float FallbackMaxHeightFraction = 0.45f;
    const float FallbackMarginPixels = 8f;
    const float HintHeightPixels = 20f;

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
        if (!isProblem && Filters.Length > 0 && !MatchesFilter(message))
            return;

        if (!isProblem)
        {
            Add(message);
            return;
        }

        // Errors and exceptions carry the first couple of stack frames, otherwise a
        // NullReferenceException in a build tells you nothing about where it came from.
        Add($"{type}: {message}");

        if (string.IsNullOrEmpty(stackTrace))
            return;

        string[] frames = stackTrace.Split('\n');
        for (int i = 0; i < frames.Length && i < 2; i++)
        {
            if (!string.IsNullOrWhiteSpace(frames[i]))
                Add("    at " + frames[i].Trim());
        }
    }

    static bool MatchesFilter(string message)
    {
        for (int i = 0; i < Filters.Length; i++)
            if (message.Contains(Filters[i]))
                return true;

        return false;
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

    /// <summary>Where the log (and its hint) draw: the right-hand side, below the corner minimap, clamped to the
    /// screen (Tudor, 2026-09-17 - it used to open in the top-left corner, straight on top of the F1 test range
    /// panel, which is why he offered to rebind one of them; moving it is the better half of that offer, since
    /// one key for "show me the tools" is fewer things for a playtester to remember).
    ///
    /// The numbers come from UiTheme, reached through the local player's minimap - which is also the thing being
    /// cleared. This component installs itself at runtime (see Install) and has nothing serialized, so there is
    /// no Inspector slot to put a theme in; borrowing the minimap's is honest rather than inventing a static
    /// somewhere for one caller.</summary>
    Rect LogRect()
    {
        MinimapView minimap = MinimapView.Local;
        UiTheme theme = minimap != null ? minimap.Theme : null;
        if (theme == null)
            return HudScreenLayout.DebugLogRect(Screen.width, Screen.height, 0f, FallbackWidthPixels,
                                                FallbackMaxHeightFraction, FallbackMarginPixels, 0f);

        float scale = HudScreenLayout.CanvasScaleFactor(theme.referenceResolution, theme.matchWidthOrHeight,
                                                        Screen.width, Screen.height);
        float band = HudScreenLayout.MinimapBandBottomPixels(theme.minimapCornerMargin, theme.minimapFrameWidth,
                                                             theme.minimapCornerSize, scale);
        return HudScreenLayout.DebugLogRect(Screen.width, Screen.height, band, theme.debugLogWidthPixels,
                                            theme.debugLogMaxHeightFraction, theme.debugLogScreenMarginPixels,
                                            theme.debugLogGapBelowMinimapPixels);
    }

    void OnGUI()
    {
        Rect area = LogRect();

        if (!visible)
        {
            // Always show the hint, so a tester who has never been told still finds it - in the log's own
            // column, so it can no longer land on the F1 test range panel in the top-left corner.
            GUI.Label(new Rect(area.x, area.y, area.width, HintHeightPixels), $"{ToggleKey}: debug log");
            return;
        }

        GUI.Box(area, $"Debug log — {CopyKey} copies to clipboard");

        GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 22f, area.width - 16f, area.height - 32f));
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (string line in lines)
            GUILayout.Label(line);
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        if (Time.realtimeSinceStartup - copiedAt < 2f)
        {
            float y = Mathf.Min(area.yMax + 2f, Screen.height - HintHeightPixels);
            GUI.Label(new Rect(area.x, y, area.width, HintHeightPixels), $"copied {lines.Count} lines");
        }
    }
}
