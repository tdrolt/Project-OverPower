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
    ///
    /// Two-team lobby, Task 3 (Tudor, 2026-09-26): a second button sits beside Start, on the same clickable
    /// canvas - the host's own switch between MatchStartRules.TwoTeams/ThreeTeams (MatchDirector.LobbyMode,
    /// HostSetLobbyMode). Shown to the host only, only in the warm-up (Decision L4/L7): its label is always the
    /// OTHER mode's text, and it greys out (with its own warm-up-line reason) only when switching TO two teams
    /// would leave a 7th player with nowhere to go - switching back needs nothing but the warm-up still running.
    /// </summary>
    public class MatchStartPanel : MonoBehaviour
    {
        // A fixed visual gap under the warm-up line box, not a per-match tuning value - so it is not one of the
        // 11 designer fields step 8 added to UiTheme (Rule 10 reserves that asset for values a designer tunes).
        private const float ButtonGapBelowLine = 8f;
        // Fixed horizontal gap between the Start button and the lobby-mode switch button beside it - same
        // reasoning as ButtonGapBelowLine: a layout constant, not something a designer tunes per match.
        private const float ButtonHorizontalGap = 12f;

        private UiTheme theme;
        private TextMeshProUGUI label; // Built (and owned) by PlayerHud.BuildWarmupLine - this class only writes to it.
        private GameObject buttonGo;
        private GameObject switchButtonGo;
        private Button switchButton;
        private TextMeshProUGUI switchButtonLabel;
        private Material textMaterial;

        private MatchDirector subscribedDirector;
        private WarmupMessage lastMessage = (WarmupMessage)(-1); // Never a real value - forces the first Update to write.
        private int lastSecondsShown = -1;
        private bool lastTooManyForHost;
        private bool lastShowButton;
        private bool lastShowSwitchButton;
        private int lastSwitchButtonMode = -1; // Never a real LobbyMode value - forces the first Update to write its label.
        private bool lastSwitchButtonInteractable;
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

            bool isHost = PhotonNetwork.IsMasterClient;
            int mode = d.LobbyMode;
            WarmupMessage message = MatchStartRules.WarmupMessageFor(d.State, d.TeamsWithPlayersNow, isHost, mode);
            int secondsShown = message == WarmupMessage.Countdown ? d.CountdownSecondsShown : 0;
            bool tooManyForHost = ShowTooManyForHost(isHost, d.State, mode, d.HostMaySwitchToTwoTeamsNow);

            // The label's text and active state change ONLY when the message, the too-many reason or the
            // countdown's own number changed - no string built (FormatCountdown allocates) on a frame nothing
            // actually moved.
            if (message != lastMessage || tooManyForHost != lastTooManyForHost
                || (message == WarmupMessage.Countdown && secondsShown != lastSecondsShown))
            {
                ApplyMessage(message, secondsShown, tooManyForHost);
                lastMessage = message;
                lastSecondsShown = secondsShown;
                lastTooManyForHost = tooManyForHost;
            }

            bool showButton = d.HostMayStartNow;
            if (showButton != lastShowButton)
            {
                buttonGo.SetActive(showButton);
                lastShowButton = showButton;
            }

            // The switch button: host only, warm-up only (Decision L4/L7) - never while counting down or live,
            // whether or not Start itself is showing (Start also needs both open teams filled; the switch does not).
            bool showSwitch = isHost && d.State == StartState.Warmup;
            if (showSwitch != lastShowSwitchButton)
            {
                switchButtonGo.SetActive(showSwitch);
                lastShowSwitchButton = showSwitch;
            }
            if (showSwitch)
            {
                bool switchInteractable = SwitchButtonInteractable(mode, d.HostMaySwitchToTwoTeamsNow, d.HostMaySwitchToThreeTeamsNow);
                if (mode != lastSwitchButtonMode || switchInteractable != lastSwitchButtonInteractable)
                {
                    switchButtonLabel.text = OtherLobbyMode(mode) == MatchStartRules.TwoTeams
                        ? theme.lobbyTwoTeamsButtonText
                        : theme.lobbyThreeTeamsButtonText;
                    switchButton.interactable = switchInteractable;
                    lastSwitchButtonMode = mode;
                    lastSwitchButtonInteractable = switchInteractable;
                }
            }
        }

        /// <summary>Decision L1/L4: the mode the switch button would move the room TO - its own label always
        /// names this one, and clicking it passes this straight to HostSetLobbyMode. Pure (no Photon, no
        /// MonoBehaviour) so an edit-mode test can cover it directly - see MatchStartPanelTests.</summary>
        internal static int OtherLobbyMode(int mode) =>
            mode == MatchStartRules.TwoTeams ? MatchStartRules.ThreeTeams : MatchStartRules.TwoTeams;

        /// <summary>Decision L4: whether the switch button is clickable right now - switching to two needs
        /// MaySwitchToTwoTeams (7+ already in the room greys it); switching back to three needs only
        /// MaySwitchToThreeTeams (always true while the button itself is shown, but read here rather than assumed,
        /// in case a stale frame races a master switch). Pure - see MatchStartPanelTests.</summary>
        internal static bool SwitchButtonInteractable(int mode, bool maySwitchToTwoTeams, bool maySwitchToThreeTeams) =>
            mode == MatchStartRules.TwoTeams ? maySwitchToThreeTeams : maySwitchToTwoTeams;

        /// <summary>Decision L7/brief: while switching TO two teams is refused (7+ already in the room), the host
        /// sees the reason (UiTheme.lobbyTwoTeamsTooManyText) instead of the ordinary warm-up line - the switch
        /// button greys out beside it. Never true for a guest, while counting down/live, or already in two-team
        /// mode (there is nothing to refuse switching back). Pure - see MatchStartPanelTests.</summary>
        internal static bool ShowTooManyForHost(bool isHost, StartState state, int mode, bool maySwitchToTwoTeams) =>
            isHost && state == StartState.Warmup && mode == MatchStartRules.ThreeTeams && !maySwitchToTwoTeams;

        /// <summary>Which warm-up line the panel shows, as a key rather than the UiTheme string itself, so the
        /// decision (which message wins, and that the too-many reason overrides all of them) is testable with no
        /// UiTheme asset or MonoBehaviour involved - see MatchStartPanelTests. ApplyMessage below is the only
        /// place that turns a key into the actual theme.* text (and, for Countdown, the formatted string).</summary>
        internal enum WarmupLineKey { Waiting, Host, Guest, TwoTeamsWaiting, TwoTeamsHost, TwoTeamsGuest, TooManyForHost, Countdown }

        internal static WarmupLineKey WarmupLineFor(WarmupMessage message, bool tooManyForHost)
        {
            if (tooManyForHost)
                return WarmupLineKey.TooManyForHost;
            return message switch
            {
                WarmupMessage.Countdown => WarmupLineKey.Countdown,
                WarmupMessage.HostMayStart => WarmupLineKey.Host,
                WarmupMessage.WaitingForHost => WarmupLineKey.Guest,
                WarmupMessage.TwoTeamsHostMayStart => WarmupLineKey.TwoTeamsHost,
                WarmupMessage.TwoTeamsWaitingForHost => WarmupLineKey.TwoTeamsGuest,
                WarmupMessage.TwoTeamsWaitingForPlayers => WarmupLineKey.TwoTeamsWaiting,
                _ => WarmupLineKey.Waiting, // WaitingForTeams
            };
        }

        private void ApplyMessage(WarmupMessage message, int secondsShown, bool tooManyForHost)
        {
            bool shown = message != WarmupMessage.None;
            label.gameObject.SetActive(shown);
            if (!shown)
                return; // Live: nothing to say - ShowMatchLiveToast (MatchDirector.ReactToRoomState) covers it.

            WarmupLineKey key = WarmupLineFor(message, tooManyForHost);
            label.text = key switch
            {
                WarmupLineKey.Countdown => FormatCountdown(secondsShown),
                WarmupLineKey.Host => theme.warmupHostText,
                WarmupLineKey.Guest => theme.warmupGuestText,
                WarmupLineKey.TwoTeamsHost => theme.warmupTwoTeamsHostText,
                WarmupLineKey.TwoTeamsGuest => theme.warmupTwoTeamsGuestText,
                WarmupLineKey.TwoTeamsWaiting => theme.warmupTwoTeamsWaitingText,
                WarmupLineKey.TooManyForHost => theme.lobbyTwoTeamsTooManyText,
                _ => theme.warmupWaitingText, // Waiting
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
        /// deliberately carries none, because nothing on it is clickable; this one is, so it must.
        ///
        /// Two-team lobby, Task 3: the switch button sits on this SAME canvas, beside Start (left of it, same
        /// row) - reusing Start's own size/colour tokens (Decision, Task 3 brief: no new UiTheme token unless
        /// clearly needed - it wasn't).</summary>
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

            // Directly under the warm-up line box - same top-centre anchor and the same two theme numbers
            // (Warmup Top Offset, Warmup Line Size) PlayerHud.BuildWarmupLine positions that box with, plus one
            // fixed gap, so the row always lines up under it regardless of screen size. The two buttons sit side
            // by side, centred as a pair on that same point (Start on the right, the switch on the left).
            float rowY = -(theme.warmupTopOffset + theme.warmupLineSize.y + ButtonGapBelowLine);
            float rowOffset = (theme.matchStartButtonSize.x + ButtonHorizontalGap) / 2f;

            buttonGo = BuildRowButton(canvasGo.transform, "Start Match Button", rowOffset, rowY, theme.matchStartButtonText);
            buttonGo.GetComponent<Button>().onClick.AddListener(() => MatchDirector.Instance?.HostStartMatch());
            buttonGo.SetActive(false); // Refresh turns this on only while HostMayStartNow.

            switchButtonGo = BuildRowButton(canvasGo.transform, "Lobby Mode Switch Button", -rowOffset, rowY, theme.lobbyTwoTeamsButtonText);
            switchButton = switchButtonGo.GetComponent<Button>();
            switchButtonLabel = switchButtonGo.GetComponentInChildren<TextMeshProUGUI>();
            switchButton.onClick.AddListener(() =>
            {
                MatchDirector d = MatchDirector.Instance;
                if (d == null)
                    return;
                d.HostSetLobbyMode(OtherLobbyMode(d.LobbyMode));
            });
            switchButtonGo.SetActive(false); // Refresh turns this on only for the host, in the warm-up.
        }

        /// <summary>One row button - Start and the lobby-mode switch button share this build (same size/colour
        /// tokens, same outline material, same no-navigation setting) and differ only in name, position and their
        /// starting label.</summary>
        private GameObject BuildRowButton(Transform parent, string name, float xOffset, float y, string labelText)
        {
            GameObject buttonObject = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            buttonObject.name = name;
            buttonObject.transform.SetParent(parent, false);
            RectTransform buttonRt = buttonObject.GetComponent<RectTransform>();
            buttonRt.anchorMin = buttonRt.anchorMax = buttonRt.pivot = new Vector2(0.5f, 1f);
            buttonRt.sizeDelta = theme.matchStartButtonSize;
            buttonRt.anchoredPosition = new Vector2(xOffset, y);
            buttonObject.GetComponent<Image>().color = theme.matchStartButtonColor;

            TextMeshProUGUI buttonLabel = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
            buttonLabel.text = labelText;
            if (theme.font != null)
                buttonLabel.font = theme.font;
            buttonLabel.fontSize = theme.bodyTextSize;
            buttonLabel.color = theme.textColor;
            ApplyOutline(buttonLabel);

            buttonObject.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };

            return buttonObject;
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
