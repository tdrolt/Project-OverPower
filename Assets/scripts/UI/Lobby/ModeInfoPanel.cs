using System.Collections.Generic;
using Overpower.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The game mode info page (board 9, lobby Task 12), opened by the mode button of the lobby room: the mode's name as the heading under a small
    /// GAME MODE caption, and the mode's info cards (GameModeDefinition.InfoCards) as a grid, three side by side: each card has its title, its text
    /// and its accent colour as a thick edge across its top. The cross at the top right closes it (so do Escape and a match starting). All the
    /// cards share one text size: the largest at which every row of cards fits the page, so a mode with more to say gets smaller text instead of
    /// text running off its card. Built in code from UiTheme (LobbyUiKit); the cards are rebuilt for the mode it is opened for.
    /// </summary>
    public sealed class ModeInfoPanel : LobbyOverlayPanel
    {
        private sealed class CardView
        {
            public TextMeshProUGUI Title, Text;
            public Image Edge;
            public Color Accent;
        }

        private TextMeshProUGUI kickerLabel;
        private RectTransform grid;
        private TextMeshProUGUI emptyLabel;
        private readonly List<CardView> cards = new List<CardView>();
        private GameModeDefinition shownFor;
        private bool built;
        private float textSize;
        private bool allFit = true;
        private int rows;

        // ---- what the page shows (for checks and drivers) ----

        public int CardCount => cards.Count;
        public string KickerText => kickerLabel != null ? kickerLabel.text : "";
        public int Columns => cards.Count == 0 ? 0 : Mathf.Min(Mathf.Max(1, Theme.modeInfoColumns), cards.Count);
        public int Rows => rows;

        /// <summary>The line shown when the mode has no cards; null when there are cards.</summary>
        public string EmptyText => emptyLabel != null && emptyLabel.gameObject.activeSelf ? emptyLabel.text : null;

        /// <summary>The size every card's text is drawn at, and whether every card fits at it.</summary>
        public float CardTextSizeUsed => textSize;
        public bool AllCardsFit => allFit;

        public string CardTitle(int index) => cards[index].Title.text;
        public string CardText(int index) => cards[index].Text.text;
        public Color CardAccent(int index) => cards[index].Accent;

        /// <summary>The colour the card's top edge is drawn in.</summary>
        public Color CardEdgeColour(int index) => cards[index].Edge.color;

        public static ModeInfoPanel Create(Transform canvas, LobbyUiKit kit)
        {
            var go = new GameObject("Mode Info", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            LobbyUiKit.Stretch((RectTransform)go.transform);
            ModeInfoPanel panel = go.AddComponent<ModeInfoPanel>();
            panel.BuildShell(kit, "Mode Info Page");
            panel.BuildFrame();
            return panel;
        }

        /// <summary>Opens the page for this mode (a null mode opens it empty). False when a match is under way: it refuses to open then.</summary>
        public bool Show(GameModeDefinition mode)
        {
            if (!Show(mode != null ? mode.DisplayName : "")) return false;
            if (!built || mode != shownFor) BuildCards(mode);
            return true;
        }

        // ---- building ----

        private void BuildFrame()
        {
            VerticalLayoutGroup column = Content.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = LobbyUiKit.Pad(Theme.howToPlayCardPaddingX, Theme.howToPlayCardPaddingX, Theme.howToPlayCardPaddingY, Theme.howToPlayCardPaddingY);
            column.spacing = Theme.howToPlayContentGap;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            VerticalLayoutGroup header = LobbyUiKit.VGroup(Content, "Header", Theme.modeInfoHeaderGap);
            kickerLabel = Kit.Text(header.transform, "Kicker", Theme.modeInfoKickerText, Kit.Bold, Theme.modeInfoKickerSize, Theme.lobbyDimColor,
                TextAlignmentOptions.MidlineLeft, Theme.modeInfoKickerSpacing);
            LobbyUiKit.Size(kickerLabel.gameObject, -1f, Theme.modeInfoKickerSize * Theme.howToPlayLineHeightFactor);
            Heading = Kit.Text(header.transform, "Mode name", "", Kit.Display, Theme.howToPlayTitleSize, Theme.lobbyOffWhiteColor, TextAlignmentOptions.MidlineLeft,
                0f, false, false);
            LobbyUiKit.Size(Heading.gameObject, -1f, Theme.howToPlayTitleSize * Theme.howToPlayLineHeightFactor);

            var gridGo = new GameObject("Cards", typeof(RectTransform));
            gridGo.transform.SetParent(Content, false);
            LobbyUiKit.Size(gridGo, -1f, 0f, 1f, 1f);
            grid = (RectTransform)gridGo.transform;
            VerticalLayoutGroup rowsGroup = gridGo.AddComponent<VerticalLayoutGroup>();
            rowsGroup.spacing = Theme.modeInfoGap;
            rowsGroup.childControlWidth = rowsGroup.childControlHeight = true;
            rowsGroup.childForceExpandWidth = true;
            rowsGroup.childForceExpandHeight = false;

            emptyLabel = Kit.Text(Content, "Nothing to read", Theme.modeInfoNoCardsText, Kit.Body, Theme.howToPlayTextSize, Theme.lobbyDimColor,
                TextAlignmentOptions.TopLeft);
            LobbyUiKit.Size(emptyLabel.gameObject, -1f, Theme.howToPlayTextSize * Theme.howToPlayLineHeightFactor);
            emptyLabel.gameObject.SetActive(false);
        }

        private void BuildCards(GameModeDefinition mode)
        {
            shownFor = mode;
            built = true;
            // The old rows go at once (Destroy only takes them at the end of the frame, DestroyImmediate is what edit mode allows).
            for (int i = grid.childCount - 1; i >= 0; i--)
            {
                GameObject old = grid.GetChild(i).gameObject;
                old.transform.SetParent(null, false);
                if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
            }
            cards.Clear();
            rows = 0;
            textSize = Theme.modeInfoCardTextSize;
            allFit = true;

            IReadOnlyList<GameModeDefinition.InfoCard> infoCards = mode != null ? mode.InfoCards : null;
            int count = infoCards != null ? infoCards.Count : 0;
            emptyLabel.gameObject.SetActive(count == 0);
            if (count == 0) return;

            int columns = Mathf.Min(Mathf.Max(1, Theme.modeInfoColumns), count);
            rows = Mathf.CeilToInt(count / (float)columns);
            float border = Theme.lobbyBorderWidth;
            float pad = Theme.modeInfoCardPadding;
            float titleHeight = Theme.modeInfoCardTitleSize * Theme.howToPlayLineHeightFactor;

            // The room a card's text has: the card's width without its padding, and what is left of the page once the heading, the gaps and the
            // other rows are taken (the rows share the height by what their text needs).
            float cardWidth = (Theme.lobbyRoomOverlayWidth - border * 2f - Theme.howToPlayCardPaddingX * 2f - Theme.modeInfoGap * (columns - 1)) / columns;
            float textWidth = cardWidth - pad * 2f;
            float headerHeight = Theme.modeInfoKickerSize * Theme.howToPlayLineHeightFactor + Theme.modeInfoHeaderGap + Theme.howToPlayTitleSize * Theme.howToPlayLineHeightFactor;
            float gridHeight = Theme.lobbyRoomOverlayHeight - border * 2f - Theme.howToPlayCardPaddingY * 2f - headerHeight - Theme.howToPlayContentGap;
            float chrome = Theme.modeInfoAccentHeight + pad * 2f + titleHeight + Theme.modeInfoCardGap + Theme.modeInfoFitSlack; // everything in a card but its text, and a little to spare

            // Build the card texts first (measuring needs them), then pick the one size at which the rows fit.
            var rowObjects = new List<RectTransform>();
            for (int r = 0; r < rows; r++)
            {
                HorizontalLayoutGroup row = LobbyUiKit.HGroup(grid, "Row " + (r + 1), Theme.modeInfoGap, TextAnchor.UpperLeft, null, true);
                row.childForceExpandWidth = true;
                rowObjects.Add((RectTransform)row.transform);
            }
            for (int i = 0; i < count; i++)
                BuildCard(rowObjects[i / columns], infoCards[i]);
            for (int i = count; i < rows * columns; i++)
            {
                var filler = new GameObject("Empty slot", typeof(RectTransform));
                filler.transform.SetParent(rowObjects[i / columns], false);
                LobbyUiKit.Size(filler, 0f, 0f, 1f, 0f);
            }

            var rowNeeds = new float[rows];
            float chosen = Theme.modeInfoCardTextMinSize;
            bool fits = false;
            for (float size = Theme.modeInfoCardTextSize; size >= Theme.modeInfoCardTextMinSize - 0.001f; size -= 0.5f)
            {
                MeasureRows(infoCards, columns, size, textWidth, chrome, rowNeeds);
                float total = Theme.modeInfoGap * (rows - 1);
                foreach (float need in rowNeeds) total += need;
                if (total <= gridHeight + 0.01f)
                {
                    chosen = size;
                    fits = true;
                    break;
                }
            }
            if (!fits) MeasureRows(infoCards, columns, chosen, textWidth, chrome, rowNeeds);
            allFit = fits;
            textSize = chosen;
            if (!fits) Debug.LogWarning("[LOBBY] the info cards of " + mode.DisplayName + " do not fit the page even at the smallest text size");

            float spare = Mathf.Max(0f, gridHeight - Theme.modeInfoGap * (rows - 1));
            foreach (float need in rowNeeds) spare -= need;
            spare = Mathf.Max(0f, spare);
            for (int r = 0; r < rows; r++)
                LobbyUiKit.Size(rowObjects[r].gameObject, -1f, rowNeeds[r] + spare / rows);
            foreach (CardView card in cards)
                card.Text.fontSize = chosen;
        }

        /// <summary>How tall each row of cards needs to be at this text size: its tallest card's text plus the card's own chrome.</summary>
        private void MeasureRows(IReadOnlyList<GameModeDefinition.InfoCard> infoCards, int columns, float size, float textWidth, float chrome, float[] rowNeeds)
        {
            for (int r = 0; r < rowNeeds.Length; r++) rowNeeds[r] = 0f;
            TextMeshProUGUI probe = cards[0].Text;
            for (int i = 0; i < infoCards.Count; i++)
            {
                float height = LobbyUiKit.WrappedHeight(probe, infoCards[i].text, size, textWidth);
                rowNeeds[i / columns] = Mathf.Max(rowNeeds[i / columns], height + chrome);
            }
        }

        private void BuildCard(Transform row, GameModeDefinition.InfoCard info)
        {
            float pad = Theme.modeInfoCardPadding;
            LobbyBox box = Kit.Box(row, "Card", Theme.lobbyPanelColor, Theme.modeInfoCardRadius, Color.clear, 0f);
            LobbyUiKit.Size(box.Outer.gameObject, 0f, -1f, 1f);
            VerticalLayoutGroup group = box.Inner.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = LobbyUiKit.Pad(pad, pad, pad + Theme.modeInfoAccentHeight, pad);
            group.spacing = Theme.modeInfoCardGap;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            LobbyUiKit.TopStripe(box.Outer, info.accent, Theme.modeInfoAccentHeight, Theme.modeInfoCardRadius);
            Image edge = box.Outer.Find("Edge/Colour").GetComponent<Image>();

            TextMeshProUGUI title = Kit.Text(box.Inner, "Title", info.title, Kit.Display, Theme.modeInfoCardTitleSize, Theme.lobbyOffWhiteColor,
                TextAlignmentOptions.MidlineLeft, 0f, false, false);
            LobbyUiKit.Size(title.gameObject, -1f, Theme.modeInfoCardTitleSize * Theme.howToPlayLineHeightFactor);
            TextMeshProUGUI text = Kit.Text(box.Inner, "Text", info.text, Kit.Body, Theme.modeInfoCardTextSize, Theme.lobbyRoomHeadingColor,
                TextAlignmentOptions.TopLeft, 0f, true, false);
            text.lineSpacing = Theme.modeInfoCardTextLineSpacing;
            LobbyUiKit.Size(text.gameObject, -1f, -1f, 1f, 1f);
            cards.Add(new CardView { Title = title, Text = text, Edge = edge, Accent = info.accent });
        }
    }
}
