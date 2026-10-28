using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// What a dead player sees: a small card, RESPAWN IN n counting down (with the capital-under-attack note under it), or the waiting card with
    /// its Quit button when no respawn is coming; and a light dark edge on the world. MatchUi shows and hides it (owner only, built on first use);
    /// the seconds come from PlayerLifecycle. The card sorts under the shop and takes no clicks except the waiting card's Quit; every frame
    /// DeathCardRules.Place keeps it clear of the open shop, the Dominion HUD's top band, the HUD's toast and the ability bar, and while the shop
    /// is open it shrinks to fit between the shop and the ability bar. The vignette sorts under every panel.
    /// </summary>
    public sealed class DeathCard : MonoBehaviour
    {
        private UiTheme theme;
        private LobbyUiKit kit;
        private Canvas cardCanvas, vignetteCanvas;
        private RectTransform card;
        private TextMeshProUGUI title, note;
        private VerticalLayoutGroup column;
        private GameObject noteStrip, quitRow;
        private LobbyButton quit;

        private PlayerLifecycle lifecycle;
        private LoadoutScreen shop;
        private PlayerHud hud;

        private bool respawnShown, waitingShown;
        private string noteText = "";
        private const int WaitingTitle = -1; // drawnSeconds while the waiting text is drawn (a countdown is never below 0)
        private const int NothingDrawn = int.MinValue;
        private int drawnSeconds = NothingDrawn;
        private bool drawnSmall;

        public bool IsShowing => respawnShown || waitingShown;
        public bool IsWaiting => waitingShown;
        public string TitleText => title != null ? title.text : "";
        public string NoteShown => noteStrip != null && noteStrip.activeSelf ? note.text : "";
        public RectTransform Card => card;
        public LobbyButton Quit => quit;
        /// <summary>Where the card was placed this frame, in screen pixels (zero width: hidden, nowhere was clear of the shop).</summary>
        public Rect PlacedOnScreen { get; private set; }

        public void Init(UiTheme uiTheme) => theme = PopupCard.ThemeOrDefault(uiTheme);

        public void SetRespawn(bool on)
        {
            respawnShown = on;
            Refresh();
        }

        public void SetWaiting(bool on)
        {
            waitingShown = on;
            Refresh();
        }

        public void SetNote(string text)
        {
            noteText = text ?? "";
            Refresh();
        }

        private void Refresh()
        {
            bool shown = IsShowing;
            if (shown) EnsureBuilt();
            if (cardCanvas == null) { enabled = false; return; }
            cardCanvas.gameObject.SetActive(shown);
            vignetteCanvas.gameObject.SetActive(shown);
            quitRow.SetActive(waitingShown);
            note.text = noteText;
            noteStrip.SetActive(!waitingShown && respawnShown && noteText.Length > 0);
            drawnSeconds = NothingDrawn;
            enabled = shown;
            if (shown) LateUpdate();
        }

        private void LateUpdate()
        {
            if (card == null) return;
            bool shopOpen = LoadoutScreen.IsOpen;
            bool relayout = false;
            if (shopOpen != drawnSmall || drawnSeconds == NothingDrawn)
            {
                drawnSmall = shopOpen;
                title.fontSize = shopOpen ? theme.deathCardShopOpenTitleSize : theme.deathCardTitleSize;
                Vector2 padding = shopOpen ? theme.deathCardShopOpenPadding : theme.deathCardPadding;
                column.padding = LobbyUiKit.Pad(padding.x, padding.x, padding.y, padding.y);
                relayout = true;
            }
            // The words change once a second at most: build them only then, not every frame.
            int seconds = waitingShown ? WaitingTitle : DeathCardRules.WholeSeconds(lifecycle != null ? lifecycle.RespawnSecondsLeft : 0f);
            if (seconds != drawnSeconds)
            {
                title.text = waitingShown ? DeathCardRules.OneLine(theme.waitingPanelText) : DeathCardRules.Text(theme.deathCardRespawnFormat, seconds);
                drawnSeconds = seconds;
                relayout = true;
            }
            if (relayout)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
                card.sizeDelta = new Vector2(card.sizeDelta.x, column.preferredHeight);
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            }

            float scale = cardCanvas.scaleFactor;
            Rect shopRect = shop != null ? ScreenRects.Of(shop.PanelRect) : Rect.zero;
            Rect band = DominionHud.Instance != null ? DominionHud.Instance.TopBandOnScreen() : Rect.zero;
            bool hudShown = hud != null && hud.isActiveAndEnabled;
            Rect bar = hudShown ? ScreenRects.Of(hud.PanelRect) : Rect.zero;
            Rect toast = hudShown ? ScreenRects.Of(hud.ToastRect) : Rect.zero;
            float margin = shopOpen ? theme.deathCardShopOpenMargin : theme.deathCardMargin;
            Rect placed = DeathCardRules.Place(shopOpen, shopRect, ScreenRects.Union(band, toast), bar, cardCanvas.pixelRect.size,
                card.rect.size * scale, margin * scale);
            PlacedOnScreen = placed;
            bool visible = placed.width > 0f;
            if (card.gameObject.activeSelf != visible) card.gameObject.SetActive(visible);
            if (visible) card.anchoredPosition = placed.position / scale;
        }

        private void EnsureBuilt()
        {
            if (cardCanvas != null) return;
            if (theme == null) theme = PopupCard.ThemeOrDefault(null);
            lifecycle = GetComponent<PlayerLifecycle>();
            shop = GetComponent<LoadoutScreen>();
            hud = GetComponent<PlayerHud>();
            kit = new LobbyUiKit(theme);

            vignetteCanvas = PopupCard.CreateCanvas(transform, "Death Vignette Canvas", UiLayers.DeathVignette, theme, false);
            Image edge = kit.Backdrop(vignetteCanvas.transform, "Vignette", theme.deathVignetteColor);
            edge.sprite = GeneratedSprites.Vignette;
            edge.raycastTarget = false;

            // Clickable only for the waiting card's Quit button: every other graphic on this canvas has its raycast off, so the shop above and
            // the world below get every other click.
            cardCanvas = PopupCard.CreateCanvas(transform, "Death Card Canvas", UiLayers.DeathCard, theme, true);
            card = PopupCard.Card(kit, cardCanvas.transform, "Death Card", theme.deathCardWidth, theme.deathCardPadding, theme.deathCardGap);
            card.anchorMin = card.anchorMax = card.pivot = Vector2.zero;
            column = card.GetComponent<VerticalLayoutGroup>();
            // Height from the column alone (LateUpdate): the card's rounded Image reports its sprite's height as a preferred height too, and a
            // fitter takes the larger, which would keep the small shop-open card too tall for the strip under the shop.
            card.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            foreach (Image image in card.GetComponentsInChildren<Image>(true)) image.raycastTarget = false;
            int team = Overpower.Net.Teams.TryGetTeam(Photon.Pun.PhotonNetwork.LocalPlayer, out int t) ? t : -1;
            LobbyUiKit.TopStripe(card, TeamTextColour(team), theme.dominionBarEdgeThickness, theme.dominionCardRadius);

            title = PopupCard.Line(kit, card, "Title", "", kit.Display, theme.deathCardTitleSize, theme.lobbyOffWhiteColor, true);

            LobbyBox strip = kit.Box(card, "Under Attack Note", theme.capitalUnderAttackNoteBackingColor, theme.lobbyCornerRadius, Color.clear, 0f);
            strip.Fill.raycastTarget = false;
            noteStrip = strip.Outer.gameObject;
            VerticalLayoutGroup noteColumn = noteStrip.AddComponent<VerticalLayoutGroup>();
            noteColumn.padding = LobbyUiKit.Pad(theme.deathCardGap, theme.deathCardGap, theme.deathCardGap * 0.5f, theme.deathCardGap * 0.5f);
            noteColumn.childControlWidth = noteColumn.childControlHeight = true;
            noteColumn.childForceExpandWidth = true;
            noteColumn.childForceExpandHeight = false;
            note = PopupCard.Line(kit, noteStrip.transform, "Text", "", kit.Body, theme.capitalUnderAttackNoteFontSize, theme.capitalUnderAttackNoteColor, true);

            quitRow = PopupCard.ButtonRow(card, 0f).gameObject;
            quit = PopupCard.Primary(kit, quitRow.transform, theme.resultButtonLeaveMatch, null);
            quit.Root.AddComponent<QuitButton>(); // the panels' own Quit: wires itself, and its label follows what it will do
        }

        private Color TeamTextColour(int team) => TeamPalette.Of(theme.dominionTeamTextColors, team, theme.lobbyBorderColor);

        private void OnDestroy()
        {
            kit?.Dispose();
            if (cardCanvas != null) Destroy(cardCanvas.gameObject);
            if (vignetteCanvas != null) Destroy(vignetteCanvas.gameObject);
        }
    }
}
