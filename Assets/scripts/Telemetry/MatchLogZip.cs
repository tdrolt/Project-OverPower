using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Playtest extras P5 (2026-09-26): zips THIS client's own match-log files (its own
    /// "{actor}_*.jsonl" and "bug_{actor}_*.png" - see MatchLogZipRule) into
    /// "&lt;Telemetry folder&gt;/OverPower-log_&lt;dateStamp&gt;_&lt;nick&gt;.zip", so each tester
    /// sends one file. Called from two places, both fine to call more than once - see ZipNow/
    /// ZipIfNeeded: MatchUI.ShowMatchResult (this client's own win/lose panel appearing) and
    /// GameQuit.Quit() (on quit, only if the result-panel path has not already zipped since the
    /// match started - see hasZippedThisMatch).
    ///
    /// Scene-level singleton living next to MatchTelemetry/ConsoleTelemetry (the BuildingManager
    /// object) - not per-player like BugMarkerKey, but every zip step already only ever touches
    /// PhotonNetwork.LocalPlayer's own actor number and MatchTelemetry.Instance's own folder, so it
    /// is inherently "this client only" the same way ConsoleTelemetry/MatchTelemetry are.
    ///
    /// Every step is exception-safe (the brief): a failed zip logs one warning and changes nothing
    /// else - no exception from in here ever reaches gameplay code.
    /// </summary>
    [DisallowMultipleComponent]
    public class MatchLogZip : MonoBehaviour
    {
        [Tooltip("Read for the saved-log overlay's text (matchLogSavedText/openLogFolderText).")]
        [SerializeField] private UiTheme theme;

        public static MatchLogZip Instance { get; private set; }

        // Computed once per match (the first time either ZipNow/ZipIfNeeded actually zips) and
        // reused after - see ZipFileName's own comment on why: the SAME zip name must come out both
        // times (result shown, then quit moments later) so the second call overwrites the first
        // rather than minting a second file if the two calls happen to land in different clock
        // minutes.
        private string cachedDateStamp;

        // Guards GameQuit's own call (ZipIfNeeded): once the result-panel path has already zipped
        // for this match, quitting moments later does not need to redo the same IO - see ZipIfNeeded.
        // Reset in HandleBeforeClose, so the NEXT match (if this client plays another) starts fresh.
        private bool hasZippedThisMatch;

        private GameObject overlayRoot;
        private TextMeshProUGUI savedLabel;
        private string lastZipFolder;

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

        private void HandleBeforeClose()
        {
            cachedDateStamp = null;
            hasZippedThisMatch = false;
        }

        /// <summary>MatchUI.ShowMatchResult's own call - the result panel appearing always deserves
        /// an up-to-date zip, so this always tries (idempotent: same file name, safely overwritten -
        /// see cachedDateStamp).</summary>
        public void ZipNow() => TryZip();

        /// <summary>GameQuit.Quit()'s own call - skips the IO entirely if ZipNow() already zipped
        /// this match (the common "result shown, then quit a moment later" path); still zips if the
        /// match never showed a result at all (a player quitting mid-match).</summary>
        public void ZipIfNeeded()
        {
            if (!hasZippedThisMatch)
                TryZip();
        }

        private void TryZip()
        {
            try
            {
                MatchTelemetry mt = MatchTelemetry.Instance;
                if (mt == null || !mt.IsRecording || string.IsNullOrEmpty(mt.CurrentFolder))
                    return; // Nothing has ever been written for this client yet - nothing to zip.
                if (PhotonNetwork.LocalPlayer == null)
                    return;

                mt.FlushNow(); // The brief's own ordering: flush THIS client's buffer to disk before reading the folder.

                int actor = PhotonNetwork.LocalPlayer.ActorNumber;
                string folder = mt.CurrentFolder;

                string[] allNames = Directory.GetFiles(folder).Select(Path.GetFileName).ToArray();
                List<string> ownNames = MatchLogZipRule.SelectOwnFiles(allNames, actor);
                if (ownNames.Count == 0)
                    return; // IsRecording was true but somehow no matching file exists - be safe, do nothing.

                cachedDateStamp ??= DateTime.Now.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
                string sanitizedNick = MatchTelemetry.Sanitize(PhotonNetwork.LocalPlayer.NickName);
                string zipName = MatchLogZipRule.ZipFileName(cachedDateStamp, sanitizedNick);
                string zipPath = Path.Combine(folder, zipName);

                // "Overwriting its own earlier zip of the same match" (the brief) - delete first
                // rather than open in Update mode, so a shrunk file list (should never happen, but
                // cheap to guard) can never leave a stale entry behind from an earlier zip.
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                using (FileStream fs = new FileStream(zipPath, FileMode.Create))
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (string name in ownNames)
                        archive.CreateEntryFromFile(Path.Combine(folder, name), name, System.IO.Compression.CompressionLevel.Optimal);
                }

                hasZippedThisMatch = true;
                lastZipFolder = folder;
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

            lastZipFolder = folder;
            if (savedLabel != null && theme != null)
                savedLabel.text = string.Format(theme.matchLogSavedText, zipPath);
            overlayRoot.SetActive(true);
        }

        /// <summary>Built once, lazily, the first time this client ever zips - a match that never
        /// reaches a result panel and never quits with anything to zip never builds this at all.
        /// Recipe copied from TestRangePanel.BuildUi/AddButton (a clickable overlay canvas needs a
        /// GraphicRaycaster; buttons get Navigation.Mode.None so Enter/chat can't resubmit one).</summary>
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
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0f);
            panelRt.anchoredPosition = new Vector2(0f, 40f);
            panelRt.sizeDelta = new Vector2(760f, 0f);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var res = new TMP_DefaultControls.Resources();
            savedLabel = AddLabel(panel.transform, "");

            GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = rowLayout.childForceExpandWidth = true;

            AddButton(buttonRow.transform, res, theme != null ? theme.openLogFolderText : "Open folder", OnOpenFolderClicked);
            AddButton(buttonRow.transform, res, "Dismiss", OnDismissClicked);

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

        private static void AddButton(Transform parent, TMP_DefaultControls.Resources res, string label,
                                       UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(res);
            go.transform.SetParent(parent, false);
            go.GetComponentInChildren<TextMeshProUGUI>().text = label;
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        }

        /// <summary>The brief's own wording: "Open folder" button, Application.OpenURL of the match
        /// folder. NEVER call this from an automated verification pass - it opens Explorer on
        /// whoever is sitting at this machine's screen; check this wiring by reading it instead.</summary>
        private void OnOpenFolderClicked()
        {
            if (!string.IsNullOrEmpty(lastZipFolder))
                Application.OpenURL(lastZipFolder);
        }

        private void OnDismissClicked()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }
    }
}
