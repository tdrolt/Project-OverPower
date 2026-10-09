using Overpower.Lobby;
using Overpower.Match;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The warm-up bar (board 4): at the top of the arena while the lobby is in its warm-up. "WARM-UP" and "Free shop · nothing counts ·
    /// N players", and for the host an END WARM-UP button (greyed, with the team that has nobody, while a team of the mode has no player present);
    /// everyone else reads "&lt;host&gt; ends the warm-up". During the countdown it reads "MATCH STARTS IN 3" in yellow with "Everything resets when it
    /// goes live"; once the match is live, or before Start game, it is not there. It is not part of a player's body, so a spectator seat (and a
    /// host on one) has it too. What it says comes from MatchStartRules.WarmupMessageFor; the button calls MatchDirector.HostStartMatch.
    /// Built in code from UiTheme (LobbyUiKit).
    /// </summary>
    public sealed class WarmupBar : MonoBehaviour
    {
        /// <summary>Where the bar's canvas sorts: below the Loadout screen (-5), under the scoreboard (30), like the spectator bar.</summary>
        public const int SortingOrder = -10;

        private LobbyUiKit kit;
        private UiTheme theme;
        private GameObject bar;
        private HorizontalLayoutGroup row;
        private TextMeshProUGUI titleLabel, infoLabel, reasonLabel;
        private LobbyButton endButton;
        private VerticalLayoutGroup textColumn;

        // What the bar shows now: rewritten only when one of these changes (the bar is polled every frame).
        private WarmupMessage shownMessage = (WarmupMessage)(-1);
        private int shownNumber = -1;
        private string shownName;
        private int shownBlocked = -2;
        private bool shownVisible;

        public bool IsShowing => bar != null && bar.activeSelf;
        public string TitleText => titleLabel != null ? titleLabel.text : "";
        public string InfoText => infoLabel != null && infoLabel.gameObject.activeSelf ? infoLabel.text : "";

        /// <summary>The reason End warm-up is greyed ("Cyan has no player"); empty when it is not.</summary>
        public string ReasonText => reasonLabel != null && reasonLabel.gameObject.activeSelf ? reasonLabel.text : "";

        public LobbyButton EndButton => endButton;
        public bool EndVisible => IsShowing && endButton != null && endButton.Root.activeSelf;
        public bool EndInteractable => EndVisible && endButton.Button.interactable;

        public static WarmupBar Create(Transform canvas, LobbyUiKit kit)
        {
            var go = new GameObject("Warm-up Bar", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            LobbyUiKit.Stretch((RectTransform)go.transform);
            // Its own canvas, below the shop (-5) and the scoreboard (30): on the lobby canvas (sort order 100) the bar drew over both.
            Canvas own = go.AddComponent<Canvas>();
            own.overrideSorting = true;
            own.sortingOrder = SortingOrder;
            go.AddComponent<GraphicRaycaster>();
            WarmupBar panel = go.AddComponent<WarmupBar>();
            panel.kit = kit;
            panel.theme = kit.Theme;
            panel.Build();
            return panel;
        }

        /// <summary>Presses End warm-up the way a click does. False when the button is not there or greyed.</summary>
        public bool PressEnd()
        {
            if (!EndInteractable) return false;
            endButton.Press();
            return true;
        }

        private void Build()
        {
            LobbyBox box = kit.Box(transform, "Bar", theme.warmupBarFill, theme.warmupBarRadius, Color.clear, 0f);
            bar = box.Outer.gameObject;
            box.Fill.raycastTarget = false; // only the button takes the mouse: the bar must not turn a shot into a click on nothing
            RectTransform rect = box.Outer;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -theme.warmupTopOffset);
            rect.sizeDelta = Vector2.zero;
            ContentSizeFitter fit = bar.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            row = bar.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = theme.warmupBarGap;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            textColumn = LobbyUiKit.VGroup(bar.transform, "Text", 0f, TextAnchor.MiddleCenter, null, false);
            VerticalLayoutGroup text = textColumn;
            titleLabel = kit.Text(text.transform, "Title", "", kit.Display, theme.warmupBarTitleSize, theme.lobbyOffWhiteColor, TextAlignmentOptions.Midline);
            LobbyUiKit.Size(titleLabel.gameObject, -1f, theme.warmupBarTitleSize * 1.5f);
            infoLabel = kit.Text(text.transform, "Info", "", kit.Body, theme.warmupBarInfoSize, theme.lobbyMutedColor, TextAlignmentOptions.Midline);
            LobbyUiKit.Size(infoLabel.gameObject, -1f, theme.warmupBarInfoSize * 1.5f);
            reasonLabel = kit.Text(text.transform, "Reason", "", kit.Bold, theme.warmupBarInfoSize, theme.lobbyErrorColor, TextAlignmentOptions.Midline);
            LobbyUiKit.Size(reasonLabel.gameObject, -1f, theme.warmupBarInfoSize * 1.5f);

            endButton = kit.MakeButton(bar.transform, "End warm-up", theme.warmupBarButtonText, kit.Display, theme.warmupBarButtonTextSize, theme.lobbyOffWhiteColor,
                theme.lobbyPurpleColor, theme.lobbyCornerRadius, theme.lobbyPurpleColor, 0f, theme.lobbyButtonSpacing);
            LobbyUiKit.Size(endButton.Root, -1f, theme.warmupBarButtonHeight);
            LobbyUiKit.WidenToLabel(endButton, theme.warmupBarButtonPadding);
            endButton.Button.onClick.AddListener(() => MatchDirector.Instance?.HostStartMatch());
            bar.SetActive(false);
        }

        private void Update()
        {
            MatchDirector d = MatchDirector.Instance;
            bool inWarmup = d != null && PhotonNetwork.InRoom && MatchDirector.LobbyStageNow == LobbySeatRules.LobbyWarmup && d.State != StartState.Live;
            if (!inWarmup)
            {
                Tick(false, WarmupMessage.None, 0, "", -1, 0);
                return;
            }

            WarmupMessage message = MatchStartRules.WarmupMessageFor(d.State, PhotonNetwork.IsMasterClient, d.HostMayStartNow);
            int number = message == WarmupMessage.Countdown ? d.CountdownSecondsShown : message == WarmupMessage.WaitingForHost ? 0 : d.PlayersNow;
            string hostName = PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.NickName : "";
            int blocked = message == WarmupMessage.HostBlocked ? d.EndWarmupBlockedTeam ?? -1 : -1;
            Tick(true, message, number, hostName, blocked, d.PlayersNow);
        }

        /// <summary>One frame of the bar, from what the match director says: hidden outside the warm-up, otherwise redrawn (through
        /// WarmupBarRules) only when the message, the number, the blocked team or the host's name changed.</summary>
        internal void Tick(bool inWarmup, WarmupMessage message, int number, string hostName, int blocked, int playersNow)
        {
            if (!inWarmup)
            {
                if (shownVisible)
                {
                    bar.SetActive(false);
                    shownVisible = false;
                    shownMessage = (WarmupMessage)(-1);
                }
                return;
            }

            if (message == WarmupMessage.None) return;
            if (shownVisible && message == shownMessage && number == shownNumber && blocked == shownBlocked && (message != WarmupMessage.WaitingForHost || hostName == shownName))
                return;

            Apply(message, number, hostName, blocked, playersNow);
            shownMessage = message;
            shownNumber = number;
            shownName = hostName;
            shownBlocked = blocked;
            if (!shownVisible)
            {
                bar.SetActive(true);
                shownVisible = true;
            }
        }

        private void Apply(WarmupMessage message, int number, string hostName, int blockedTeam, int playersNow)
        {
            WarmupBarView view = WarmupBarRules.ViewFor(message, blockedTeam, playersNow);
            bool countdown = view.Countdown;
            bool hostButton = view.ButtonShown;

            titleLabel.text = countdown ? Format(theme.matchCountdownText, number) : theme.warmupBarTitleText;
            titleLabel.color = countdown ? theme.lobbyYellowColor : theme.lobbyOffWhiteColor;
            infoLabel.text = countdown ? theme.warmupBarCountdownInfo
                : message == WarmupMessage.WaitingForHost ? Format(theme.warmupBarGuestFormat, hostName)
                : Format(theme.warmupBarInfoFormat, number == 1 ? theme.warmupBarOnePlayerText : Format(theme.warmupBarPlayersText, number));

            reasonLabel.gameObject.SetActive(view.ReasonShown);
            if (view.ReasonShown)
                reasonLabel.text = Format(theme.warmupBarBlockedFormat, LobbyRoomRules.TeamName(theme.scoreboardTeamNames, blockedTeam));

            endButton.Root.SetActive(hostButton);
            if (hostButton) endButton.SetEnabled(view.ButtonEnabled);

            // The host's bar sets its two lines flush left beside the button (board 4 A); the others are centred (B, C).
            TextAlignmentOptions align = hostButton ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
            titleLabel.alignment = infoLabel.alignment = reasonLabel.alignment = align;
            textColumn.childAlignment = hostButton ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            Vector2 pad = hostButton ? theme.warmupBarPaddingHost : theme.warmupBarPaddingGuest;
            row.padding = LobbyUiKit.Pad(pad.x, hostButton ? theme.warmupBarHostRightPadding : pad.x, pad.y, pad.y);
        }

        /// <summary>string.Format that a broken theme text cannot turn into an exception every frame: the raw text is shown instead.</summary>
        private static string Format(string format, object argument)
        {
            try
            {
                return string.Format(format, argument);
            }
            catch (System.FormatException)
            {
                return format;
            }
        }
    }
}
