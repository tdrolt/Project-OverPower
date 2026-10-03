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
    /// A page that opens over a lobby screen: a dark shade over everything, a card with a title and a close button at the top right (lobby Task 10).
    /// The game mode info page and the How to play page are this card with different contents (ModeInfoPanel, HowToPlayPanel: lobby Task 12 fills them).
    /// Built in code from UiTheme (LobbyUiKit); the card's Body is where the contents go. Shown and hidden with Show / Hide; the close button hides it.
    /// </summary>
    public abstract class LobbyOverlayPanel : MonoBehaviour
    {
        protected LobbyUiKit Kit;
        protected UiTheme Theme;
        private GameObject root;
        private TextMeshProUGUI titleLabel;
        private LobbyButton closeButton;

        /// <summary>Where the page's contents go (below the title).</summary>
        protected RectTransform Body { get; private set; }

        public bool IsShowing => root != null && root.activeSelf;

        // Every page that is open right now: Escape belongs to an open page (QuitConfirmPanel must not open on top of it).
        private static readonly HashSet<LobbyOverlayPanel> OpenPages = new HashSet<LobbyOverlayPanel>();

        /// <summary>True while any How to play / mode info page is open.</summary>
        public static bool AnyPageOpen => OpenPages.Count > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ForgetPages() => OpenPages.Clear();

        private void Update()
        {
            if (!IsShowing) return;
            ApplyMatchState(PhotonNetwork.InRoom, MatchDirector.LobbyStageNow);
            CloseOnEscape(Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
        }

        private void OnDisable() => OpenPages.Remove(this);

        private void OnDestroy() => OpenPages.Remove(this);

        /// <summary>The title as drawn.</summary>
        public string TitleText => titleLabel != null ? titleLabel.text : "";

        /// <summary>The close button (what a click on the cross does).</summary>
        public LobbyButton CloseButton => closeButton;

        protected void BuildCard(LobbyUiKit kit, string rootName)
        {
            Kit = kit;
            Theme = kit.Theme;
            root = kit.Backdrop(transform, rootName, Theme.lobbyRoomOverlayShade).gameObject;
            root.AddComponent<Button>().transition = Selectable.Transition.None; // a click on the shade does nothing, and does not reach what is behind it

            LobbyBox card = kit.Box(root.transform, "Card", Theme.lobbyDarkColor, Theme.lobbyCardRadius, Theme.lobbyCardBorderColor, Theme.lobbyBorderWidth);
            RectTransform cardRect = card.Outer;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(Theme.lobbyRoomOverlayWidth, Theme.lobbyRoomOverlayHeight);

            VerticalLayoutGroup column = card.Inner.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = LobbyUiKit.Pad(Theme.createCardPadding, Theme.createCardPadding, Theme.createCardPadding * 0.8f, Theme.createCardPadding);
            column.spacing = Theme.createCardGap;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            titleLabel = Kit.Text(card.Inner, "Title", "", Kit.Display, Theme.lobbyRoomOverlayTitleSize, Theme.lobbyOffWhiteColor, TextAlignmentOptions.MidlineLeft);
            LobbyUiKit.Size(titleLabel.gameObject, -1f, Theme.lobbyRoomOverlayTitleSize * 1.5f);

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(card.Inner, false);
            LobbyUiKit.Size(body, -1f, 0f, 1f, 1f);
            Body = (RectTransform)body.transform;

            closeButton = Kit.MakeButton(card.Outer, "Close", Theme.lobbyRoomCloseText, Kit.Bold, Theme.lobbyRoomOverlayCloseSize * 0.5f,
                Theme.lobbyOffWhiteColor, Theme.lobbyPanelColor, Theme.lobbyCornerRadius, Theme.lobbyBorderColor, Theme.lobbyBorderWidth);
            closeButton.Root.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform closeRect = closeButton.Rect;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(Theme.lobbyRoomOverlayCloseSize, Theme.lobbyRoomOverlayCloseSize);
            closeRect.anchoredPosition = new Vector2(-Theme.createCardPadding * 0.5f, -Theme.createCardPadding * 0.5f);
            closeButton.Button.onClick.AddListener(Hide);
            root.SetActive(false);
        }

        /// <summary>Opens the page with this title. Refused (false, a log line) while a match is under way in the room this client is in: the
        /// page's shade would block every shot.</summary>
        public bool Show(string title)
        {
            if (!LobbyScreenRules.OverlayMayBeShown(PhotonNetwork.InRoom, MatchDirector.LobbyStageNow))
            {
                Debug.Log("[LOBBY] a page cannot be opened during a match");
                return false;
            }
            titleLabel.text = title ?? "";
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

        /// <summary>Escape closes an open page, as a player expects of a big overlay. True when it did.</summary>
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
