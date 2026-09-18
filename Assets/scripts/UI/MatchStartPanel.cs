using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Overpower.Match;

namespace Overpower.UI
{
    /// <summary>
    /// 2.7b step 8: the warm-up line, the countdown, and the host's Start button. PlayerHud builds the warm-up
    /// LABEL itself (its own AddLabel, so it shares the HUD's font/shadow/outline material - see
    /// PlayerHud.BuildWarmupLine) and hands it to Create; this class only decides what that label says and
    /// whether it shows, and separately builds its OWN clickable button canvas - copying LoadoutScreen.Builder.
    /// BuildToggleButtonCanvas's recipe, because the HUD's own canvas deliberately has no GraphicRaycaster (see
    /// PlayerHud's class comment) and a click on this button must not also reach PlayerInputRouter as a shot.
    ///
    /// The host is the Photon master (Decision 15): the button's onClick calls MatchDirector.HostStartMatch()
    /// LOCALLY - the presser IS the master, so this is a plain method call, no RPC. The countdown itself is one
    /// server time written to the room once (MatchDirector.Live.cs's mLiveAt Room Property), so every client
    /// counts down to the same moment on its own synced clock without an RPC either.
    ///
    /// Subscribes to MatchDirector.LiveStateChanged (see that event's own doc comment on MatchDirector.Live.cs)
    /// so the rare Warmup/CountingDown/Live edge redraws the instant it is seen, rather than only on this
    /// component's own next Update tick. Update still polls State/TeamsWithPlayersNow/HostMayStartNow/
    /// CountdownSecondsShown every single frame regardless - those can all change with no state edge at all (a
    /// second player joining during the warm-up, or the countdown's own number ticking down) - so the event is a
    /// belt-and-braces immediate refresh, not a replacement for the poll.
    /// </summary>
    public class MatchStartPanel : MonoBehaviour
    {
        // A fixed visual gap under the warm-up line box, not a per-match tuning value - so it is not one of the
        // 11 designer fields step 8 added to UiTheme (Rule 10 reserves that asset for values a designer tunes).
        private const float ButtonGapBelowLine = 8f;

        private UiTheme theme;
        private TextMeshProUGUI label; // Built (and owned) by PlayerHud.BuildWarmupLine - this class only writes to it.
        private GameObject buttonGo;
        private Material textMaterial;

        private MatchDirector subscribedDirector;
        private WarmupMessage lastMessage = (WarmupMessage)(-1); // Never a real value - forces the first Update to write.
        private int lastSecondsShown = -1;
        private bool lastShowButton;
        private bool loggedBadCountdownFormat;

        public static MatchStartPanel Create(Transform parent, UiTheme theme, TextMeshProUGUI label)
        {
            var go = new GameObject("Match Start Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            MatchStartPanel panel = go.AddComponent<MatchStartPanel>();
            panel.theme = theme;
            panel.label = label;
            panel.BuildButtonCanvas();
            return panel;
        }

        private void OnDestroy()
        {
            if (subscribedDirector != null)
                subscribedDirector.LiveStateChanged -= HandleLiveStateChanged;
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        private void Update()
        {
            MatchDirector d = MatchDirector.Instance;
            if (d != subscribedDirector)
            {
                if (subscribedDirector != null)
                    subscribedDirector.LiveStateChanged -= HandleLiveStateChanged;
                subscribedDirector = d;
                if (subscribedDirector != null)
                    subscribedDirector.LiveStateChanged += HandleLiveStateChanged;
            }

            Refresh(d);
        }

        private void HandleLiveStateChanged() => Refresh(MatchDirector.Instance);

        private void Refresh(MatchDirector d)
        {
            if (d == null || theme == null || label == null)
                return;

            WarmupMessage message = MatchStartRules.WarmupMessageFor(d.State, d.TeamsWithPlayersNow, PhotonNetwork.IsMasterClient);
            int secondsShown = message == WarmupMessage.Countdown ? d.CountdownSecondsShown : 0;

            // The label's text and active state change ONLY when the message or the countdown's own number
            // changed - no string built (FormatCountdown allocates) on a frame nothing actually moved.
            if (message != lastMessage || (message == WarmupMessage.Countdown && secondsShown != lastSecondsShown))
            {
                ApplyMessage(message, secondsShown);
                lastMessage = message;
                lastSecondsShown = secondsShown;
            }

            bool showButton = d.HostMayStartNow;
            if (showButton != lastShowButton)
            {
                buttonGo.SetActive(showButton);
                lastShowButton = showButton;
            }
        }

        private void ApplyMessage(WarmupMessage message, int secondsShown)
        {
            bool shown = message != WarmupMessage.None;
            label.gameObject.SetActive(shown);
            if (!shown)
                return; // Live: nothing to say - ShowMatchLiveToast (MatchDirector.ReactToRoomState) covers it.

            label.text = message switch
            {
                WarmupMessage.Countdown => FormatCountdown(secondsShown),
                WarmupMessage.HostMayStart => theme.warmupHostText,
                WarmupMessage.WaitingForHost => theme.warmupGuestText,
                _ => theme.warmupWaitingText, // WaitingForTeams
            };
        }

        /// <summary>Review fix: a theme edit that drops the {0} (or leaves a stray brace) must not throw every
        /// single frame the countdown is up - string.Format is guarded here, a bad format is logged ONCE rather
        /// than every frame, and the raw text is shown instead so something legible stays on screen either way.</summary>
        private string FormatCountdown(int secondsShown)
        {
            string format = theme.matchCountdownText;
            try
            {
                return string.Format(format, secondsShown);
            }
            catch (System.FormatException)
            {
                if (!loggedBadCountdownFormat)
                {
                    loggedBadCountdownFormat = true;
                    Debug.LogError($"[MatchStartPanel] UiTheme.matchCountdownText (\"{format}\") is not a valid format " +
                                    "string - it needs exactly one {0}. Showing the raw text instead.");
                }
                return format;
            }
        }

        /// <summary>Copies LoadoutScreen.Builder.BuildToggleButtonCanvas's recipe (overlay canvas, overrideSorting at
        /// order -10, a GraphicRaycaster) so PlayerInputRouter.pointerOverUi - true only for a raycast-target Graphic
        /// on a canvas that HAS a GraphicRaycaster - picks up a hover/click here and gates PrimaryHeld/EquipmentHeld
        /// off, exactly like the loadout toggle button already does. The HUD's own canvas (PlayerHud.BuildUi)
        /// deliberately carries none, because nothing on it is clickable; this one is, so it must.</summary>
        private void BuildButtonCanvas()
        {
            EnsureEventSystem();

            GameObject canvasGo = new GameObject("Match Start Button Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = -10;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject buttonObject = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            buttonObject.name = "Start Match Button";
            buttonObject.transform.SetParent(canvasGo.transform, false);
            RectTransform buttonRt = buttonObject.GetComponent<RectTransform>();
            buttonRt.anchorMin = buttonRt.anchorMax = buttonRt.pivot = new Vector2(0.5f, 1f);
            buttonRt.sizeDelta = theme.matchStartButtonSize;
            // Directly under the warm-up line box - same top-centre anchor and the same two theme numbers
            // (Warmup Top Offset, Warmup Line Size) PlayerHud.BuildWarmupLine positions that box with, plus one
            // fixed gap, so the two always line up regardless of screen size.
            buttonRt.anchoredPosition = new Vector2(0f, -(theme.warmupTopOffset + theme.warmupLineSize.y + ButtonGapBelowLine));
            buttonObject.GetComponent<Image>().color = theme.matchStartButtonColor;

            TextMeshProUGUI buttonLabel = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
            buttonLabel.text = theme.matchStartButtonText;
            if (theme.font != null)
                buttonLabel.font = theme.font;
            buttonLabel.fontSize = theme.bodyTextSize;
            buttonLabel.color = theme.textColor;
            ApplyOutline(buttonLabel);

            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => MatchDirector.Instance?.HostStartMatch());
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            buttonObject.SetActive(false); // Refresh turns this on only while HostMayStartNow.
            buttonGo = buttonObject;
        }

        /// <summary>Same reasoning as PlayerHud.ApplyOutline/LoadoutScreen.ApplyOutline: one shared Material
        /// instance for every text this panel builds (today just the button label), instead of letting TMP
        /// auto-clone one per label. The outline/weight/shadow numbers live on UiTheme.ApplyHudTextStyle.</summary>
        private void ApplyOutline(TextMeshProUGUI tmp)
        {
            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(textMaterial);
            }
            tmp.fontSharedMaterial = textMaterial;
        }

        /// <summary>Same safety net as LoadoutScreen.Builder.EnsureEventSystem - the scene already carries one in
        /// the normal path, so this only matters if this panel is ever built before that.</summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}
