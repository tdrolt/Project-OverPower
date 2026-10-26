using System.Collections.Generic;
using Overpower.Lobby;
using Overpower.Match;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// A page that opens over a lobby screen: a dark shade over everything and a big card with a cross at its top right that always closes it.
    /// How to play (HowToPlayPanel) and the game mode info page (ModeInfoPanel) are this shell with different contents: the card's Content is
    /// where they build theirs. Built in code from UiTheme (LobbyUiKit). Shown and hidden with Show / Hide; the cross and Escape hide it, and it
    /// hides itself the moment a match starts in the room.
    /// </summary>
    public abstract class LobbyOverlayPanel : MonoBehaviour
    {
        protected LobbyUiKit Kit;
        protected UiTheme Theme;
        private GameObject root;
        private TextMeshProUGUI titleLabel;
        private LobbyButton closeButton;

        /// <summary>The card the page is drawn on: the page builds its contents here.</summary>
        protected RectTransform Content { get; private set; }

        /// <summary>The page's heading, set by the page once it has built it; Show(title) writes the title into it.</summary>
        protected TextMeshProUGUI Heading
        {
            get => titleLabel;
            set => titleLabel = value;
        }

        public bool IsShowing => root != null && root.activeSelf;

        public string TitleText => titleLabel != null ? titleLabel.text : "";

        public LobbyButton CloseButton => closeButton;

        // Every page that is open right now: Escape belongs to an open page (QuitConfirmPanel must not open on top of it).
        private static readonly HashSet<LobbyOverlayPanel> OpenPages = new HashSet<LobbyOverlayPanel>();

        /// <summary>True while any How to play / mode info page is open. A page that was destroyed or hidden without a callback (edit mode runs
        /// none) is dropped from the list here.</summary>
        public static bool AnyPageOpen
        {
            get
            {
                OpenPages.RemoveWhere(page => page == null || !page.IsShowing);
                return OpenPages.Count > 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ForgetPages() => OpenPages.Clear();

        private bool chatWasOpenLastFrame;

        private void Update()
        {
            Tick(PhotonNetwork.InRoom, MatchDirector.LobbyStageNow, Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame, PhotonChat.IsOpen);
        }

        /// <summary>One frame of the page: closes itself when the match started, and closes on Escape unless the chat is open (this frame or last
        /// frame), because that Escape already closed the chat: one Escape closes one thing.</summary>
        internal void Tick(bool inRoom, int lobbyStage, bool escapePressed, bool chatOpen)
        {
            bool chatBlocks = chatOpen || chatWasOpenLastFrame;
            chatWasOpenLastFrame = chatOpen;
            if (!IsShowing) return;
            ApplyMatchState(inRoom, lobbyStage);
            CloseOnEscape(escapePressed && !chatBlocks);
        }

        private void OnDisable() => OpenPages.Remove(this);

        private void OnDestroy() => OpenPages.Remove(this);

        /// <summary>Builds the shade, the card and the cross. The page fills Content afterwards.</summary>
        protected void BuildShell(LobbyUiKit kit, string rootName)
        {
            Kit = kit;
            Theme = kit.Theme;
            root = kit.Backdrop(transform, rootName, Theme.lobbyRoomOverlayShade).gameObject;
            root.AddComponent<Button>().transition = Selectable.Transition.None; // a click on the shade does nothing, and does not reach what is behind it

            LobbyBox card = kit.Box(root.transform, "Card", Theme.lobbyDarkColor, Theme.lobbyCardRadius, Theme.lobbyCardBorderColor, Theme.lobbyBorderWidth);
            RectTransform cardRect = card.Outer;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(Theme.lobbyRoomOverlayWidth, Theme.lobbyRoomOverlayHeight);
            card.Inner.gameObject.AddComponent<RectMask2D>(); // a page never draws outside its card
            Content = card.Inner;

            // The cross sits at the card's top right, over the contents, and is drawn as two lines (the fonts have no cross).
            closeButton = Kit.MakeButton(card.Outer, "Close", "", Kit.Bold, Theme.lobbyRoomOverlayCloseSize * 0.5f,
                Theme.lobbyOffWhiteColor, Theme.lobbyPanelColor, Theme.lobbyCornerRadius, Theme.lobbyBorderColor, Theme.lobbyBorderWidth);
            closeButton.Root.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform closeRect = closeButton.Rect;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(Theme.lobbyRoomOverlayCloseSize, Theme.lobbyRoomOverlayCloseSize);
            closeRect.anchoredPosition = new Vector2(-Theme.howToPlayCardPaddingX, -Theme.howToPlayCardPaddingY);
            DrawCross(closeButton.Fill.transform);
            closeButton.Button.onClick.AddListener(Hide);
            root.SetActive(false);
        }

        private void DrawCross(Transform parent)
        {
            for (int i = 0; i < 2; i++)
            {
                var line = new GameObject(i == 0 ? "Cross down" : "Cross up", typeof(RectTransform), typeof(Image));
                line.transform.SetParent(parent, false);
                RectTransform rect = (RectTransform)line.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                // a square's diagonal is 1.41 times its side: each line is as long as the cross is wide
                rect.sizeDelta = new Vector2(Theme.lobbyRoomCloseCrossSize * 1.4142f, Theme.lobbyRoomCloseCrossThickness);
                rect.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -45f : 45f);
                Image image = line.GetComponent<Image>();
                image.color = Theme.lobbyOffWhiteColor;
                image.raycastTarget = false;
            }
        }

        /// <summary>Opens the page with this title (a null title keeps the heading as it is). Refused (false, a log line) while a match is under way
        /// in the room this client is in: the page's shade would block every shot.</summary>
        public bool Show(string title) => TryShow(title, PhotonNetwork.InRoom, MatchDirector.LobbyStageNow);

        internal bool TryShow(string title, bool inRoom, int lobbyStage)
        {
            if (!LobbyScreenRules.OverlayMayBeShown(inRoom, lobbyStage))
            {
                Debug.Log("[LOBBY] a page cannot be opened during a match");
                return false;
            }
            if (titleLabel != null && title != null) titleLabel.text = title;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            OpenPages.Add(this);
            return true;
        }

        /// <summary>The page hides itself whenever the match has started in the room this client is in (lS is not the lobby any more): an open page
        /// would otherwise stay up into the arena.</summary>
        internal void ApplyMatchState(bool inRoom, int lobbyStage)
        {
            if (IsShowing && !LobbyScreenRules.OverlayMayBeShown(inRoom, lobbyStage))
            {
                Debug.Log("[LOBBY] the match started: the open page closes");
                Hide();
            }
        }

        /// <summary>Escape closes an open page. True when it did.</summary>
        internal bool CloseOnEscape(bool escapePressed)
        {
            if (!escapePressed || !IsShowing) return false;
            Hide();
            return true;
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
            OpenPages.Remove(this);
        }
    }
}
