using Overpower.Match;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Escape's pop-up: during a match a menu (MENU, the Sound section, a line, Leave match in red and Close) whose Leave match
    /// opens a second card ("Leave the match?", Yes, leave / Stay); "Close the game?" outside a match on a desktop build, and only
    /// the volume in the browser outside a match (BackToNameScreenRules). A scene-level runtime component (one
    /// instance, like TestRangePanel and ConnectionLostPanel), a clickable overlay canvas with a GraphicRaycaster built in code.
    /// A raw Keyboard.current poll, not an InputAction (a tool key, like every other Escape consumer here).
    /// OWNERSHIP: LoadoutScreen and the chat panel close THEMSELVES on Escape in the same frame and Update() order
    /// is not fixed, so this asks EscapeOwnershipRule (this frame OR the last). While open it claims tool focus on the
    /// local player's PlayerInputRouter (no moving or shooting while deciding; the match keeps running), the
    /// "resolve the local player, claim/release keyed by this" recipe TestRangePanel uses. The master volume row sits above
    /// Yes / No (MasterVolume: applied while dragging, remembered on release).
    /// </summary>
    [DisallowMultipleComponent]
    public class QuitConfirmPanel : MonoBehaviour
    {
        [Tooltip("Read for the pop-up's words (quitPromptText, leaveMatchPromptText, escapeMenuTitleText, quitYesText, quitNoText, " +
                 "escapeMenuCloseText, the master volume row) and its card look.")]
        [SerializeField] private UiTheme theme;

        private LobbyUiKit kit;
        private GameObject uiRoot;
        private TextMeshProUGUI promptLabel;
        private GameObject menuCard;
        private GameObject soundHeader;
        private GameObject divider;
        private GameObject confirmCard;
        private TextMeshProUGUI volumeLabel;
        private Slider volumeSlider;
        private LobbyButton yesButton;
        private LobbyButton noButton;
        private bool visible;
        private bool leaving; // the leave is under way: the scene is about to be rebuilt, Escape no longer opens anything

        private bool shopOpenLastFrame;
        private bool chatOpenLastFrame;
        private bool pageOpenLastFrame;

        private GameObject cachedLocalPlayer;
        private PlayerInputRouter claimedRouter;

        private void Awake()
        {
            BuildUi();
        }

        private void OnDisable()
        {
            claimedRouter?.SetToolFocus(this, false);
            claimedRouter = null;
        }

        private void OnDestroy() => kit?.Dispose();

        private void Update()
        {
            if (leaving)
                return;
            bool shopOpenNow = LoadoutScreen.IsOpen;
            bool chatOpenNow = PhotonChat.IsOpen;
            bool pageOpenNow = Overpower.UI.LobbyOverlayPanel.AnyPageOpen; // How to play / mode info close themselves on Escape
            bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

            if (!visible)
            {
                if (escapePressed && !EscapeOwnershipRule.BelongsToShopOrChat(shopOpenNow, shopOpenLastFrame, chatOpenNow, chatOpenLastFrame, pageOpenNow, pageOpenLastFrame))
                    Open();
            }
            else if (escapePressed)
            {
                Close(); // from the second card too: Escape always goes back to the game
            }

            shopOpenLastFrame = shopOpenNow;
            chatOpenLastFrame = chatOpenNow;
            pageOpenLastFrame = pageOpenNow;
        }

        private void Open()
        {
            visible = true;
            bool matchRunning = BackToNameScreenRules.IsMatchRunning(PhotonNetwork.InRoom, LobbyStageOfRoom());
            string prompt;
            bool showYes = true;
            bool menu = BackToNameScreenRules.EscapeLeavesTheMatch(matchRunning);
            if (menu)
                prompt = theme.escapeMenuMatchTitleText;
            else if (BackToNameScreenRules.OffersQuitGame(matchRunning, Application.platform == RuntimePlatform.WebGLPlayer))
                prompt = theme.quitPromptText;
            else
            {
                prompt = theme.escapeMenuTitleText;
                showYes = false;
            }
            if (promptLabel != null)
                promptLabel.text = prompt;
            soundHeader?.SetActive(menu);
            divider?.SetActive(menu);
            yesButton?.Root.SetActive(showYes);
            if (yesButton != null)
            {
                yesButton.Label.text = menu ? theme.escapeMenuLeaveMatchText : theme.quitYesText;
                Color fill = menu ? theme.escapeMenuLeaveButtonColor : theme.lobbyPurpleColor;
                yesButton.Fill.color = yesButton.EnabledFill = fill;
                if (yesButton.Border != null) yesButton.Border.color = fill;
                LobbyUiKit.WidenToLabel(yesButton, theme.dominionBreakButtonPadding);
            }
            if (noButton != null)
            {
                noButton.Label.text = showYes && !menu ? theme.quitNoText : theme.escapeMenuCloseText;
                LobbyUiKit.WidenToLabel(noButton, theme.dominionBreakButtonPadding);
            }
            menuCard.SetActive(true);
            confirmCard.SetActive(false);
            if (volumeSlider != null)
            {
                volumeSlider.SetValueWithoutNotify(AudioListener.volume);
                OnVolumeChanged(AudioListener.volume);
            }
            uiRoot.SetActive(true);

            PlayerInputRouter router = ResolveLocalPlayer()?.GetComponentInChildren<PlayerInputRouter>(true);
            if (router != claimedRouter)
            {
                claimedRouter?.SetToolFocus(this, false);
                claimedRouter = router;
            }
            claimedRouter?.SetToolFocus(this, true);
        }

        private void Close()
        {
            visible = false;
            uiRoot.SetActive(false);
            claimedRouter?.SetToolFocus(this, false);
            claimedRouter = null;
        }

        private GameObject ResolveLocalPlayer()
        {
            if (cachedLocalPlayer != null)
                return cachedLocalPlayer;

            if (PhotonNetwork.LocalPlayer == null)
                return null;

            PhotonView view = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            cachedLocalPlayer = view != null ? view.gameObject : null;
            return cachedLocalPlayer;
        }

        // ---- Yes / No ----

        /// <summary>During a match (Leave match): the second card, which alone leaves. Otherwise GameQuit.Quit, which stops Play Mode in
        /// the Editor (or quits the built Player): NEVER press Yes outside a match in an automated check.</summary>
        private void OnYesClicked()
        {
            if (BackToNameScreenRules.EscapeLeavesTheMatch(BackToNameScreenRules.IsMatchRunning(PhotonNetwork.InRoom, LobbyStageOfRoom())))
            {
                ShowLeaveConfirm();
                return;
            }
            GameQuit.Quit();
        }

        /// <summary>The pop-up hides but keeps the player's input claimed: the leave can take seconds before the scene is rebuilt,
        /// and the body must not move or shoot meanwhile.</summary>
        private void LeaveMatch()
        {
            leaving = true;
            visible = false;
            uiRoot.SetActive(false);
            RoomManager manager = FindFirstObjectByType<RoomManager>();
            if (manager != null)
                manager.ReturnToLobbyList();
            else
                Debug.LogWarning("[LEAVE] no RoomManager in the scene - cannot go back to the lobby list");
        }

        private void OnNoClicked() => Close();

        private void ShowLeaveConfirm()
        {
            menuCard.SetActive(false);
            confirmCard.SetActive(true);
        }

        private void OnConfirmLeaveClicked() => LeaveMatch();

        private void OnStayClicked() => Close();

        /// <summary>Whether the second card ("Leave the match?") is the one showing, for checks.</summary>
        public bool IsAskingToLeave => visible && confirmCard != null && confirmCard.activeSelf;

        private static int LobbyStageOfRoom()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return 0;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(Overpower.Lobby.LobbyKeys.Stage, out object stage) && stage is int value ? value : 0;
        }

        // ---- master volume ----

        private void OnVolumeChanged(float volume)
        {
            MasterVolume.Apply(volume);
            if (volumeLabel != null && theme != null)
                volumeLabel.text = MasterVolume.PercentText(theme.masterVolumePercentFormat, volume);
        }

        /// <summary>Saved only when the slider is let go: a drag writes the volume every frame, the registry once.</summary>
        private void OnVolumeReleased() => MasterVolume.Save(volumeSlider != null ? volumeSlider.value : AudioListener.volume);

        /// <summary>The Escape pop-up's slider (checks and captures drive it).</summary>
        public Slider VolumeSlider => volumeSlider;
        public bool IsOpen => visible;

        // ---- UI construction (PopupCard: the card look of the round bar and result card) ----

        private void BuildUi()
        {
            theme = PopupCard.ThemeOrDefault(theme);
            kit = new LobbyUiKit(theme);
            Canvas canvas = PopupCard.CreateCanvas(transform, "Quit Confirm Canvas", UiLayers.QuitConfirm, theme, true);
            uiRoot = canvas.gameObject;
            kit.Backdrop(canvas.transform, "Dim", theme.popupDimColor);

            RectTransform card = PopupCard.Card(kit, canvas.transform, "Panel", theme.popupCardWidth, theme.spectatorResultCardPadding, theme.spectatorResultCardGap);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            menuCard = card.gameObject;
            promptLabel = PopupCard.Line(kit, card, "Prompt", theme.quitPromptText, kit.Display, theme.popupTitleSize, theme.lobbyOffWhiteColor, false);
            soundHeader = PopupCard.Line(kit, card, "Sound", theme.escapeMenuSoundHeaderText, kit.Bold, theme.popupBodySize, theme.lobbyMutedColor, false).gameObject;

            HorizontalLayoutGroup row = LobbyUiKit.HGroup(card, "Volume", theme.lobbyListColumnGap, TextAnchor.MiddleCenter);
            LobbyUiKit.Size(row.gameObject, -1f, theme.masterVolumeRowHeight);
            TextMeshProUGUI label = kit.Text(row.transform, "Label", theme.masterVolumeLabel, kit.Bold, theme.popupBodySize, theme.lobbyMutedColor,
                TextAlignmentOptions.MidlineLeft, theme.lobbyLabelSpacing);
            label.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Size(label.gameObject, label.GetPreferredValues(theme.masterVolumeLabel).x);
            volumeSlider = PopupCard.MakeSlider(kit, row.transform, "Slider", theme.masterVolumeRowHeight);
            volumeLabel = kit.Text(row.transform, "Percent", MasterVolume.PercentText(theme.masterVolumePercentFormat, 1f), kit.Bold, theme.popupBodySize,
                theme.lobbyOffWhiteColor, TextAlignmentOptions.MidlineRight);
            volumeLabel.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Size(volumeLabel.gameObject, volumeLabel.GetPreferredValues("100%").x);
            volumeSlider.SetValueWithoutNotify(AudioListener.volume);
            OnVolumeChanged(AudioListener.volume);
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            volumeSlider.gameObject.AddComponent<PointerUpRelay>().Released = OnVolumeReleased;

            divider = new GameObject("Line", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(card, false);
            divider.GetComponent<Image>().color = theme.lobbyBorderColor;
            divider.GetComponent<Image>().raycastTarget = false;
            LobbyUiKit.Size(divider, -1f, 2f);

            Transform buttons = PopupCard.ButtonRow(card, theme.lobbyListColumnGap);
            yesButton = PopupCard.Primary(kit, buttons, theme.quitYesText, OnYesClicked);
            noButton = PopupCard.Secondary(kit, buttons, theme.quitNoText, OnNoClicked);

            RectTransform confirm = PopupCard.Card(kit, canvas.transform, "Leave Confirm", theme.popupCardWidth, theme.spectatorResultCardPadding, theme.spectatorResultCardGap);
            confirm.anchorMin = confirm.anchorMax = confirm.pivot = new Vector2(0.5f, 0.5f);
            confirmCard = confirm.gameObject;
            PopupCard.Line(kit, confirm, "Prompt", theme.leaveMatchPromptText, kit.Display, theme.popupTitleSize, theme.lobbyOffWhiteColor, false);
            PopupCard.Line(kit, confirm, "Note", theme.leaveMatchRejoinNoteText, kit.Bold, theme.popupBodySize, theme.lobbyMutedColor, true);
            Transform confirmButtons = PopupCard.ButtonRow(confirm, theme.lobbyListColumnGap);
            LobbyButton leave = PopupCard.Primary(kit, confirmButtons, theme.leaveMatchYesText, OnConfirmLeaveClicked);
            leave.Fill.color = leave.EnabledFill = theme.escapeMenuLeaveButtonColor;
            if (leave.Border != null) leave.Border.color = theme.escapeMenuLeaveButtonColor;
            PopupCard.Secondary(kit, confirmButtons, theme.leaveMatchStayText, OnStayClicked);
            confirmCard.SetActive(false);

            uiRoot.SetActive(false);
        }
    }
}
