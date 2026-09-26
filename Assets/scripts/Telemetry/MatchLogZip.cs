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
    /// Playtest extras P5 (2026-09-26): zips THIS client's own match-log files (its own
    /// "{actor}_*.jsonl" and "bug_{actor}_*.png" - see MatchLogZipRule) into
    /// "&lt;Telemetry folder&gt;/OverPower-log_&lt;match folder name&gt;_&lt;nick&gt;.zip", so each
    /// tester sends one file. Called from two places, both fine to call more than once and both
    /// through the same ZipNow (2026-09-26 fix - see its own comment on why there is no longer a
    /// "skip if already zipped" variant): MatchUI.ShowMatchResult (this client's own win/lose panel
    /// appearing) and GameQuit.Quit() (before it disconnects - see GameQuit's own comment on order).
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

        private GameObject overlayRoot;
        private TextMeshProUGUI savedLabel;

        // The last match folder this client actually knew about - set here AND in HandleBeforeClose
        // (which runs while MatchTelemetry.CurrentFolder is still valid, before OnLeftRoom can clear
        // it - see that method's own comment). MatchLogZipRule.ResolveZipFolder falls back to this
        // when CurrentFolder has already gone empty, and OnOpenFolderClicked reads it for the overlay's
        // own button.
        private string lastKnownFolder;

        // One shared TMP material for both overlay button labels (playtest extras P6 follow-up, item 2)
        // - same reasoning as QuitConfirmPanel/MatchStartPanel's own ApplyOutline.
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

        /// <summary>2026-09-26 fix (the zip-name-fix brief): captures MatchTelemetry.CurrentFolder into
        /// lastKnownFolder WHILE it is still valid - MatchTelemetry raises BeforeClose (this event)
        /// BEFORE it does anything that could clear CurrentFolder (OnLeftRoom's own "next match" reset;
        /// OnApplicationQuit/OnDestroy never clear it at all), so this always sees a real folder when
        /// there is one. The two-client check found the actual failure this guards against: GameQuit.
        /// Quit()'s own PhotonNetwork.Disconnect() can raise OnLeftRoom (BeforeClose, then
        /// CurrentFolder = null) before this component's OWN OnApplicationQuit gets to run its
        /// re-zip - that second TryZip call used to find CurrentFolder already empty and skip zipping
        /// a late-arriving line entirely. Now it falls back to whatever this method last captured (see
        /// MatchLogZipRule.ResolveZipFolder, used by TryZip below).</summary>
        private void HandleBeforeClose()
        {
            MatchTelemetry mt = MatchTelemetry.Instance;
            if (mt != null && !string.IsNullOrEmpty(mt.CurrentFolder))
                lastKnownFolder = mt.CurrentFolder;
        }

        /// <summary>Playtest extras P6 follow-up (item 3): most testers close the window (X / Alt+F4) or
        /// quit mid-match, never touching the result panel or the Escape pop-up's Yes - this is the one
        /// path that catches them. Always re-zips, like ZipNow - TryZip names the file after the match
        /// folder itself (MatchLogZipRule.ZipFileName), not a clock read, so a repeat here simply
        /// overwrites the same file (the brief's own "zip AGAIN even if the result panel already
        /// zipped": a late bug mark or a leaving error can be logged after that first zip).
        ///
        /// Order with MatchTelemetry.OnApplicationQuit (same GameObject, no guaranteed order between two
        /// different components' OnApplicationQuit): if that already ran, its writer is closed and
        /// IsRecording now reads false, but Close() flushes before closing (TelemetryWriter.Close), so
        /// the file on disk is already complete - TryZip's own guard reads CurrentFolder (falling back
        /// to lastKnownFolder), not IsRecording, precisely so it does not bail out in that order. If this
        /// runs first instead, the writer is still open and FlushNow() below writes the buffer as normal.
        /// Either order finds a file to zip; TryZip's own try/catch keeps this exception-safe like every
        /// other call into it.</summary>
        private void OnApplicationQuit() => ZipNow();

        /// <summary>MatchUI.ShowMatchResult's own call AND GameQuit.Quit()'s own call (2026-09-26 fix -
        /// there used to be a second, "skip if already zipped" entry point, ZipIfNeeded, but naming the
        /// file after the match folder instead of a clock read (MatchLogZipRule.ZipFileName) already
        /// makes every call idempotent - the same file is simply overwritten - so the skip added nothing
        /// but a chance to miss a line logged between the two calls). Always tries.</summary>
        public void ZipNow() => TryZip();

        private void TryZip()
        {
            try
            {
                MatchTelemetry mt = MatchTelemetry.Instance;
                // ResolveZipFolder: prefer the live folder; fall back to the last one this client knew
                // about if MatchTelemetry.OnLeftRoom already cleared CurrentFolder - see
                // HandleBeforeClose's own comment on when and why that race happens.
                string folder = MatchLogZipRule.ResolveZipFolder(mt != null ? mt.CurrentFolder : null, lastKnownFolder);
                if (!MatchLogZipRule.ShouldAttemptZip(folder))
                    return; // See ShouldAttemptZip's own comment on why this is CurrentFolder, not IsRecording.
                if (PhotonNetwork.LocalPlayer == null)
                    return;

                mt?.FlushNow(); // The brief's own ordering: flush THIS client's buffer to disk before reading the folder.

                int actor = PhotonNetwork.LocalPlayer.ActorNumber;

                string[] allNames = Directory.GetFiles(folder).Select(Path.GetFileName).ToArray();
                List<string> ownNames = MatchLogZipRule.SelectOwnFiles(allNames, actor);
                if (ownNames.Count == 0)
                    return; // IsRecording was true but somehow no matching file exists - be safe, do nothing.

                string sanitizedNick = MatchTelemetry.Sanitize(PhotonNetwork.LocalPlayer.NickName);
                // Named after the match folder itself, not a clock read (2026-09-26 fix - see
                // MatchLogZipRule.ZipFileName's own comment on the real bug this replaces): stable for
                // the whole match, so every zip of it - result panel, then maybe again on quit - comes
                // out under the SAME name no matter what the clock reads when each call happens to run.
                string zipName = MatchLogZipRule.ZipFileName(Path.GetFileName(folder), sanitizedNick);
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
                // Item 2: Path.Combine above uses this platform's separator throughout, but folder
                // itself (MatchTelemetry.ResolveMatchFolder, built from Application.persistentDataPath)
                // always comes back with forward slashes on Windows too - Unity's own doing, not ours -
                // so the raw zipPath read "C:/Users/...\Telemetry\..." (the brief's own capture). Display
                // only: GetFullPath normalises every separator to this platform's own without touching
                // the actual path used to write the file above.
                savedLabel.text = string.Format(theme.matchLogSavedText, Path.GetFullPath(zipPath));
            }
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
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.95f); // Opaque (item 2) - 0.8 let the HUD's "not ready" labels show through it.
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            // Item 2: top-centre, under the warm-up line's own spot (theme.warmupTopOffset) - the exact
            // anchor PlayerHud.BuildWarmupLine uses for that line - rather than bottom-centre, which sat
            // directly over the ability bar's slots (the brief's own capture,
            // zip_overlay_matchlog_saved.png). By the time this overlay can show (a result panel, or a
            // quit), the warm-up line and the Start/switch buttons that shared this spot are always
            // hidden - the match is over - so nothing there is free to overlap. Canvas units, so this
            // holds at 1920x1080 and 1280x720 alike, same as everything else built against this theme.
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 1f);
            panelRt.anchoredPosition = new Vector2(0f, -(theme != null ? theme.warmupTopOffset : 130f));
            panelRt.sizeDelta = new Vector2(760f, 0f);

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
            // All four off (item 2, same reasoning and same measured bug as QuitConfirmPanel.BuildUi): a
            // freshly-added HorizontalLayoutGroup defaults childForceExpandHeight to true, which collapsed
            // both buttons to 0 height even with width left alone. Each button keeps its own fixed size
            // (theme's Start button size) via a LayoutElement - see AddButton below.
            rowLayout.childControlWidth = rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;

            // Real buttons (item 2), same recipe and same colour split as QuitConfirmPanel's Yes/No (item
            // 1): Open Folder is the affirmative action (Start's own green), Dismiss is the neutral/close
            // one (Bar Track Colour, the same grey Loadout's own buttons already reuse) - both used to
            // render as dim, barely-readable default-sprite buttons over the HUD's own slots showing
            // through (zip_overlay_matchlog_saved.png).
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

        /// <summary>Item 2: a real button - filled background, sized and coloured from the Start
        /// button's own UiTheme tokens - not just a label on the bare default sprite. Instance method
        /// now (was static): the fill colour still comes from the caller, but the label's font/size/
        /// outline come from this component's own `theme`/`textMaterial`.</summary>
        private void AddButton(Transform parent, TMP_DefaultControls.Resources res, string label, Color fillColor,
                                UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(res);
            go.transform.SetParent(parent, false);
            Vector2 size = theme != null ? theme.matchStartButtonSize : new Vector2(260f, 52f);
            go.GetComponent<RectTransform>().sizeDelta = size;
            // See QuitConfirmPanel.AddButton's own comment: the row's LayoutGroup needs an ILayoutElement
            // to read a size from for its own preferred-height calculation, or it measures 0.
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

        /// <summary>Same reasoning as QuitConfirmPanel.ApplyOutline/MatchStartPanel.ApplyOutline: one
        /// shared Material instance for every button label this overlay builds.</summary>
        private void ApplyOutline(TextMeshProUGUI tmp)
        {
            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(textMaterial);
            }
            tmp.fontSharedMaterial = textMaterial;
        }

        /// <summary>The brief's own wording: "Open folder" button, Application.OpenURL of the match
        /// folder. NEVER call this from an automated verification pass - it opens Explorer on
        /// whoever is sitting at this machine's screen; check this wiring by reading it instead.</summary>
        private void OnOpenFolderClicked()
        {
            if (!string.IsNullOrEmpty(lastKnownFolder))
                Application.OpenURL(lastKnownFolder);
        }

        private void OnDismissClicked()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }
    }
}
