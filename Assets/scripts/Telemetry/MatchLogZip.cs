using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.UI;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Zips THIS client's own match-log files (its "{actor}_*.jsonl" and "bug_{actor}_*.png", see MatchLogZipRule) into
    /// "&lt;Match logs root&gt;/OverPower-log_&lt;match folder name&gt;_&lt;actor&gt;_&lt;nick&gt;.zip", directly in the root rather than the dated
    /// per-match subfolder (see TryZip), so each tester finds and sends one file. Called from MatchUI.ShowMatchResult (the win/lose
    /// panel), GameQuit.Quit() (before it disconnects) and OnApplicationQuit, all through ZipNow, and fine to call more than once.
    ///
    /// Scene-level singleton next to MatchTelemetry/ConsoleTelemetry (the BuildingManager object); inherently "this client only"
    /// because every step touches only PhotonNetwork.LocalPlayer's actor and MatchTelemetry.Instance's folder. Every step is
    /// exception-safe: a failed zip logs one warning and no exception reaches gameplay code.
    /// </summary>
    [DisallowMultipleComponent]
    public class MatchLogZip : MonoBehaviour
    {
        [Tooltip("Read for the saved-log overlay's text (matchLogSavedText/openLogFolderText).")]
        [SerializeField] private UiTheme theme;

        public static MatchLogZip Instance { get; private set; }

        private GameObject overlayRoot;
        private TextMeshProUGUI savedLabel;

        // The last match folder this client knew: set here AND in HandleBeforeClose (MatchTelemetry.CurrentFolder is still valid
        // there, before OnLeftRoom clears it). MatchLogZipRule.ResolveZipFolder falls back to it, and OnOpenFolderClicked reads it
        // for the overlay's button.
        private string lastKnownFolder;

        // The actor number and nick while still in the room: after a deliberate leave (back to the name screen) Photon reports the
        // actor as -1, and the last zip must still find this client's own files.
        private int lastKnownActor = -1;
        private string lastKnownNick = "";

        // One shared TMP material for both overlay button labels (as QuitConfirmPanel/ConnectionLostPanel's ApplyOutline).
        private Material textMaterial;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogError("[MatchLogZip] a second MatchLogZip exists - destroying it. There must be exactly one.", this);
                Destroy(this);
            }
        }

        private void OnEnable()
        {
            if (MatchTelemetry.Instance != null)
                MatchTelemetry.Instance.BeforeClose += HandleBeforeClose;
        }

        private void OnDisable()
        {
            if (MatchTelemetry.Instance != null)
                MatchTelemetry.Instance.BeforeClose -= HandleBeforeClose;
        }

        private void OnDestroy()
        {
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        /// <summary>Captures MatchTelemetry.CurrentFolder into lastKnownFolder WHILE it is still valid: MatchTelemetry raises
        /// BeforeClose before anything that could clear CurrentFolder (OnLeftRoom's reset; OnApplicationQuit/OnDestroy never clear
        /// it). GameQuit.Quit()'s PhotonNetwork.Disconnect() can raise OnLeftRoom (BeforeClose, then CurrentFolder = null) before this
        /// component's OnApplicationQuit re-zip runs, which would then find no folder; TryZip falls back to this capture
        /// (MatchLogZipRule.ResolveZipFolder).</summary>
        private void HandleBeforeClose()
        {
            MatchTelemetry mt = MatchTelemetry.Instance;
            if (mt != null && !string.IsNullOrEmpty(mt.CurrentFolder))
                lastKnownFolder = mt.CurrentFolder;
            if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber > 0)
            {
                lastKnownActor = PhotonNetwork.LocalPlayer.ActorNumber;
                lastKnownNick = PhotonNetwork.LocalPlayer.NickName;
            }
        }

        /// <summary>Most testers close the window (X / Alt+F4) or quit mid-match, never touching the result panel or the Escape
        /// pop-up's Yes - this is the path that catches them. Always re-zips, like ZipNow: a repeat overwrites the same file
        /// (MatchLogZipRule.ZipFileName), and a late bug mark or leaving error can be logged after the first zip.
        ///
        /// Order with MatchTelemetry.OnApplicationQuit (same GameObject, no guaranteed order): if that ran first its writer is closed
        /// and IsRecording reads false, but Close() flushes first, so the file is complete; TryZip's guard reads CurrentFolder
        /// (falling back to lastKnownFolder), not IsRecording, so it does not bail out in that order. If this runs first, FlushNow()
        /// writes the buffer as normal.</summary>
        private void OnApplicationQuit() => ZipNow();

        /// <summary>MatchUI.ShowMatchResult's and GameQuit.Quit()'s call. Always tries: naming the file after the match folder
        /// (MatchLogZipRule.ZipFileName) makes every call idempotent, so a "skip if already zipped" variant would only risk missing a
        /// line logged between two calls.</summary>
        public void ZipNow() => TryZip();

        private void TryZip()
        {
            try
            {
                MatchTelemetry mt = MatchTelemetry.Instance;
                // Prefer the live folder; fall back to the last known one if MatchTelemetry.OnLeftRoom already cleared CurrentFolder (see HandleBeforeClose).
                string folder = MatchLogZipRule.ResolveZipFolder(mt != null ? mt.CurrentFolder : null, lastKnownFolder);
                if (!MatchLogZipRule.ShouldAttemptZip(folder))
                    return; // CurrentFolder, not IsRecording (see ShouldAttemptZip).
                if (PhotonNetwork.LocalPlayer == null)
                    return;

                mt?.FlushNow(); // Flush THIS client's buffer to disk before reading the folder.

                // Remember them HERE too, while in the room: Photon sets the actor to -1 before OnLeftRoom on a deliberate leave, so
                // HandleBeforeClose never sees a valid one there - the zip made at the button press does.
                int actor = MatchLogZipRule.ResolveActor(PhotonNetwork.LocalPlayer.ActorNumber, lastKnownActor);
                lastKnownActor = actor;
                string nick = MatchLogZipRule.ResolveNick(PhotonNetwork.LocalPlayer.NickName, lastKnownNick, PhotonNetwork.LocalPlayer.ActorNumber);
                lastKnownNick = nick;

                string[] allNames = Directory.GetFiles(folder).Select(Path.GetFileName).ToArray();
                List<string> ownNames = MatchLogZipRule.SelectOwnFiles(allNames, actor);
                if (ownNames.Count == 0)
                    return; // No matching file exists: do nothing.

                string sanitizedNick = MatchTelemetry.Sanitize(nick);
                // Named after the match folder, not a clock read (see MatchLogZipRule.ZipFileName): every zip of the match comes out
                // under the SAME name.
                string zipName = MatchLogZipRule.ZipFileName(Path.GetFileName(folder), sanitizedNick, actor);
                // The zip goes directly in the "Match logs" ROOT (the folder's parent), not the dated per-match subfolder, so testers find
                // it at once. Falls back to folder itself if it has no parent (ResolveMatchFolder always creates it one level under a root).
                string zipFolder = Directory.GetParent(folder)?.FullName ?? folder;
                string zipPath = Path.Combine(zipFolder, zipName);

                // Overwrite its own earlier zip of the same match: delete first rather than open in Update mode, so a shrunk file list
                // can never leave a stale entry behind.
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                using (FileStream fs = new FileStream(zipPath, FileMode.Create))
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (string name in ownNames)
                        archive.CreateEntryFromFile(Path.Combine(folder, name), name, System.IO.Compression.CompressionLevel.Optimal);
                }

                lastKnownFolder = folder;
                ShowSavedOverlay(zipPath, folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MatchLogZip] could not zip this client's match log: {e.Message}");
            }
        }

        // ---------------------------------------------------------------- the saved-log overlay

        private void ShowSavedOverlay(string zipPath, string folder)
        {
            if (overlayRoot == null)
                BuildOverlay();

            lastKnownFolder = folder;
            if (savedLabel != null && theme != null)
            {
                // Path.Combine uses this platform's separator, but folder (MatchTelemetry.ResolveMatchFolder, via
                // TelemetryPaths.ResolveMatchLogsRoot) comes back with forward slashes on Windows too (Unity's doing), so the raw zipPath
                // read "C:/Users/...\Match logs\...". Display only: GetFullPath normalises every separator without touching the path
                // used to write the file. Shows the match folder (inside the Match logs root) and the zip, each readable and wrapping.
                savedLabel.text = string.Format(theme.matchLogSavedText,
                    MatchLogZipRule.WrappablePath(Path.GetFullPath(folder)), MatchLogZipRule.WrappablePath(Path.GetFullPath(zipPath)));
            }
            overlayRoot.SetActive(true);
        }

        /// <summary>Built lazily the first time this client zips, so a match that never zips never builds it. Recipe from
        /// TestRangePanel.BuildUi/AddButton (a clickable overlay canvas needs a GraphicRaycaster; buttons get Navigation.Mode.None so
        /// Enter/chat can't resubmit one).</summary>
        private void BuildOverlay()
        {
            var canvasGo = new GameObject("Match Log Saved Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // Above MatchUI's win/lose panels (sortingOrder 0, no override) - shown "with the result".
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();
            overlayRoot = canvasGo;

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.95f); // Opaque: 0.8 let the HUD's "not ready" labels show through.
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            // Bottom left (theme.matchLogSavedOffset), not top centre: the result screen's YOU WIN / YOU LOSE title sits at the top and
            // this overlay covered it; the bottom left is clear of the title, the result button and the ability slots. Canvas units, so
            // this holds at 1920x1080 and 1280x720 alike.
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0f, 0f);
            panelRt.anchoredPosition = theme != null ? theme.matchLogSavedOffset : new Vector2(24f, 70f);
            panelRt.sizeDelta = new Vector2(theme != null ? theme.matchLogSavedWidth : 640f, 0f);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var res = new TMP_DefaultControls.Resources();
            savedLabel = AddLabel(panel.transform, "");

            GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            // All four off (same measured bug as QuitConfirmPanel.BuildUi): a fresh HorizontalLayoutGroup defaults
            // childForceExpandHeight to true, which collapsed both buttons to 0 height. Each button keeps its own fixed size via a
            // LayoutElement (see AddButton).
            rowLayout.childControlWidth = rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;

            // Real buttons, same recipe and colour split as QuitConfirmPanel's Yes/No: Open Folder is the affirmative action (Start's
            // green), Dismiss the neutral one (Bar Track Colour, the grey Loadout's buttons reuse).
            AddButton(buttonRow.transform, res, theme != null ? theme.openLogFolderText : "Open folder",
                theme != null ? theme.matchStartButtonColor : new Color(0.16f, 0.45f, 0.25f, 0.95f), OnOpenFolderClicked);
            AddButton(buttonRow.transform, res, "Dismiss",
                theme != null ? theme.barTrackColor : new Color(0.18f, 0.18f, 0.2f, 1f), OnDismissClicked);

            overlayRoot.SetActive(false);
        }

        private static TextMeshProUGUI AddLabel(Transform parent, string text)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 20f;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        /// <summary>A real button - filled background, sized and coloured from the Start button's UiTheme tokens. The label's
        /// font/size/outline come from this component's `theme`/`textMaterial`.</summary>
        private void AddButton(Transform parent, TMP_DefaultControls.Resources res, string label, Color fillColor,
                                UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(res);
            go.transform.SetParent(parent, false);
            Vector2 size = theme != null ? theme.matchStartButtonSize : new Vector2(260f, 52f);
            go.GetComponent<RectTransform>().sizeDelta = size;
            // See QuitConfirmPanel.AddButton: the row's LayoutGroup needs an ILayoutElement to read a size from, or it measures 0.
            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            go.GetComponent<Image>().color = fillColor;

            TextMeshProUGUI buttonLabel = go.GetComponentInChildren<TextMeshProUGUI>();
            buttonLabel.text = label;
            if (theme != null)
            {
                if (theme.font != null)
                    buttonLabel.font = theme.font;
                buttonLabel.fontSize = theme.bodyTextSize;
                buttonLabel.color = theme.textColor;
                ApplyOutline(buttonLabel);
            }

            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        }

        /// <summary>As QuitConfirmPanel.ApplyOutline/ConnectionLostPanel.ApplyOutline: one shared Material for every button label.</summary>
        private void ApplyOutline(TextMeshProUGUI tmp)
        {
            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(textMaterial);
            }
            tmp.fontSharedMaterial = textMaterial;
        }

        /// <summary>Application.OpenURL of the match folder. NEVER call this from an automated verification pass - it opens Explorer
        /// on whoever is sitting at this machine's screen; check this wiring by reading it instead.</summary>
        private void OnOpenFolderClicked()
        {
            bool windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
            // One tested method decides what runs (MatchLogZipRule.OpenFolder): explorer.exe with the plain path on Windows (a file://
            // address breaks on '#', '%' and non-ASCII letters), the address elsewhere.
            OpenFolderCommand command = MatchLogZipRule.OpenFolder(windows, lastKnownFolder);
            switch (command.Kind)
            {
                case OpenFolderKind.Explorer:
                    try { System.Diagnostics.Process.Start("explorer.exe", command.Argument); }
                    catch (Exception e) { Debug.LogWarning($"[MatchLogZip] could not open the match folder: {e.Message}"); }
                    break;
                case OpenFolderKind.Url:
                    Application.OpenURL(command.Argument);
                    break;
            }
        }

        private void OnDismissClicked()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }
    }
}
