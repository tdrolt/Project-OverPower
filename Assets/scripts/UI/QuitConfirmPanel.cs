using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Escape asks "Close the game?": a scene-level runtime component (one instance, like TestRangePanel and
    /// ConnectionLostPanel), a clickable overlay canvas with a GraphicRaycaster built in code. A raw Keyboard.current
    /// poll, not an InputAction (a tool key, like every other Escape consumer here).
    /// OWNERSHIP: LoadoutScreen and the chat panel close THEMSELVES on Escape in the same frame and Update() order
    /// is not fixed, so this asks EscapeOwnershipRule (this frame OR the last). While open it claims tool focus on the
    /// local player's PlayerInputRouter (no moving or shooting while deciding; the match keeps running), the
    /// "resolve the local player, claim/release keyed by this" recipe TestRangePanel uses. The master volume row sits above
    /// Yes / No (MasterVolume: applied while dragging, remembered on release).
    /// </summary>
    [DisallowMultipleComponent]
    public class QuitConfirmPanel : MonoBehaviour
    {
        [Tooltip("Read for the pop-up's words (quitPromptText, quitYesText, quitNoText, the master volume row) and its card look.")]
        [SerializeField] private UiTheme theme;

        private LobbyUiKit kit;
        private GameObject uiRoot;
        private TextMeshProUGUI promptLabel;
        private TextMeshProUGUI volumeLabel;
        private Slider volumeSlider;
        private bool visible;

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
                Close();
            }

            shopOpenLastFrame = shopOpenNow;
            chatOpenLastFrame = chatOpenNow;
            pageOpenLastFrame = pageOpenNow;
        }

        private void Open()
        {
            visible = true;
            if (promptLabel != null && theme != null)
                promptLabel.text = theme.quitPromptText;
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

        /// <summary>NEVER call this from an automated verification pass - it stops Play Mode in the
        /// Editor (or quits the built Player). Check this wiring by reading it instead.</summary>
        private void OnYesClicked() => GameQuit.Quit();

        private void OnNoClicked() => Close();

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
            promptLabel = PopupCard.Line(kit, card, "Prompt", theme.quitPromptText, kit.Display, theme.popupTitleSize, theme.lobbyOffWhiteColor, false);

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

            Transform buttons = PopupCard.ButtonRow(card, theme.lobbyListColumnGap);
            PopupCard.Primary(kit, buttons, theme.quitYesText, OnYesClicked);
            PopupCard.Secondary(kit, buttons, theme.quitNoText, OnNoClicked);

            uiRoot.SetActive(false);
        }
    }
}
