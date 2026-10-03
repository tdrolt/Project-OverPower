using System.Collections.Generic;
using Overpower.Data;
using Overpower.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The How to play wiki (board 5A, lobby Task 12), opened by the cyan How to play buttons of the lobby list and the lobby room only: a page
    /// list on the left (the open page marked with a cyan bar), the page's title, its picture beside its text, a previous and a next button that
    /// name the neighbouring pages, the page counter ("4 / 6") and the cross at the top right on every page. The pages (title, text, picture) are
    /// the HowToPlayPages asset the UiTheme points to; which page comes next is HowToPlayRules. It is the LobbyOverlayPanel card, so it also
    /// closes on Escape and when a match starts, and refuses to open during one. Built in code from UiTheme (LobbyUiKit).
    /// </summary>
    public sealed class HowToPlayPanel : LobbyOverlayPanel
    {
        private sealed class Entry
        {
            public LobbyButton Button;
            public Image Bar;
        }

        private HowToPlayPages pages;
        private List<string> titles = new List<string>();
        private readonly List<Entry> entries = new List<Entry>();
        private Image pictureImage;
        private TextMeshProUGUI bodyLabel, counterLabel, noPagesLabel;
        private LobbyButton previousButton, nextButton;
        private CanvasGroup previousGroup, nextGroup;
        private int index;
        private float textSize;
        private bool allFit = true;

        // ---- what the page shows (for checks and drivers) ----

        public int PageIndex => index;
        public int PageCount => titles.Count;
        public string BodyText => bodyLabel != null ? bodyLabel.text : "";
        public string CounterText => counterLabel != null ? counterLabel.text : "";
        public string PreviousText => previousButton != null ? previousButton.Label.text : "";
        public string NextText => nextButton != null ? nextButton.Label.text : "";
        public bool PreviousEnabled => previousButton != null && previousButton.Button.interactable;
        public bool NextEnabled => nextButton != null && nextButton.Button.interactable;
        public Sprite ShownPicture => pictureImage != null ? pictureImage.sprite : null;
        public int SelectedEntry => titles.Count == 0 ? -1 : index;

        /// <summary>The size every page's text is drawn at, and whether the longest page fits its box at it.</summary>
        public float TextSizeUsed => textSize;
        public bool AllPagesFit => allFit;

        /// <summary>The page list's entry titles, top to bottom.</summary>
        public IReadOnlyList<string> EntryTitles
        {
            get
            {
                var list = new List<string>();
                foreach (Entry entry in entries) list.Add(entry.Button.Label.text);
                return list;
            }
        }

        public LobbyButton EntryButton(int page) => page >= 0 && page < entries.Count ? entries[page].Button : null;

        public static HowToPlayPanel Create(Transform canvas, LobbyUiKit kit)
        {
            var go = new GameObject("How To Play", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            LobbyUiKit.Stretch((RectTransform)go.transform);
            HowToPlayPanel panel = go.AddComponent<HowToPlayPanel>();
            panel.BuildShell(kit, "How To Play Page");
            panel.BuildPages();
            return panel;
        }

        /// <summary>Opens the wiki on the page it was last left on. False when a match is under way (it refuses to open then).</summary>
        public bool Show()
        {
            if (!Show((string)null)) return false;
            ShowPage(index);
            return true;
        }

        /// <summary>Goes to the next page (what the next button does). False on the last page.</summary>
        public bool PressNext()
        {
            if (!NextEnabled) return false;
            nextButton.Press();
            return true;
        }

        /// <summary>Goes to the page before (what the previous button does). False on the first page.</summary>
        public bool PressPrevious()
        {
            if (!PreviousEnabled) return false;
            previousButton.Press();
            return true;
        }

        /// <summary>Opens a page from the page list (what a click on its entry does). False when there is no such page.</summary>
        public bool SelectPage(int page)
        {
            if (page < 0 || page >= entries.Count) return false;
            entries[page].Button.Press();
            return true;
        }

        // ---- building ----

        private void BuildPages()
        {
            pages = Theme.howToPlayPages;
            if (pages != null) titles = pages.Titles();
            else Debug.LogError("[LOBBY] the UiTheme has no How to play pages assigned - the wiki opens empty");

            float border = Theme.lobbyBorderWidth;
            HorizontalLayoutGroup row = Content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            BuildSidebar(Content);

            VerticalLayoutGroup column = LobbyUiKit.VGroup(Content, "Page", Theme.howToPlayContentGap, TextAnchor.UpperLeft,
                LobbyUiKit.Pad(Theme.howToPlayCardPaddingX, Theme.howToPlayCardPaddingX, Theme.howToPlayCardPaddingY, Theme.howToPlayCardPaddingY));
            LobbyUiKit.Size(column.gameObject, 0f, -1f, 1f, 1f);

            float titleHeight = Theme.howToPlayTitleSize * Theme.howToPlayLineHeightFactor;
            Heading = Kit.Text(column.transform, "Title", "", Kit.Display, Theme.howToPlayTitleSize, Theme.lobbyOffWhiteColor, TextAlignmentOptions.MidlineLeft);
            LobbyUiKit.Size(Heading.gameObject, -1f, titleHeight);

            HorizontalLayoutGroup middle = LobbyUiKit.HGroup(column.transform, "Picture and text", Theme.howToPlayPictureGap, TextAnchor.UpperLeft);
            LobbyUiKit.Size(middle.gameObject, -1f, 0f, 1f, 1f);

            LobbyBox picture = Kit.Box(middle.transform, "Picture", Theme.howToPlayPictureColor, Theme.howToPlayPictureRadius, Color.clear, 0f);
            LobbyUiKit.Size(picture.Outer.gameObject, Theme.howToPlayPictureWidth, Theme.howToPlayPictureHeight, 0f, 0f);
            var pictureGo = new GameObject("Image", typeof(RectTransform), typeof(Image));
            pictureGo.transform.SetParent(picture.Inner, false);
            LobbyUiKit.Stretch((RectTransform)pictureGo.transform);
            pictureImage = pictureGo.GetComponent<Image>();
            pictureImage.preserveAspect = true;
            pictureImage.raycastTarget = false;

            bodyLabel = Kit.Text(middle.transform, "Text", "", Kit.Body, Theme.howToPlayTextSize, Theme.howToPlayTextColor, TextAlignmentOptions.TopLeft,
                0f, true);
            bodyLabel.lineSpacing = Theme.howToPlayTextLineSpacing;
            LobbyUiKit.Size(bodyLabel.gameObject, 0f, -1f, 1f, 0f);

            noPagesLabel = Kit.Text(middle.transform, "No pages", Theme.howToPlayNoPagesText, Kit.Body, Theme.howToPlayTextSize, Theme.lobbyDimColor,
                TextAlignmentOptions.TopLeft);
            LobbyUiKit.Size(noPagesLabel.gameObject, 0f, -1f, 1f, 0f);
            noPagesLabel.gameObject.SetActive(titles.Count == 0);

            BuildFooter(column.transform);

            // One text size for every page: the largest at which the longest page fits the room beside the picture.
            float textWidth = Theme.lobbyRoomOverlayWidth - border * 2f - Theme.howToPlaySidebarWidth - Theme.howToPlayCardPaddingX * 2f
                - Theme.howToPlayPictureWidth - Theme.howToPlayPictureGap;
            float textHeight = Theme.lobbyRoomOverlayHeight - border * 2f - Theme.howToPlayCardPaddingY * 2f - titleHeight - Theme.howToPlayContentGap
                - Theme.howToPlayButtonHeight - Theme.howToPlayContentGap;
            var texts = new List<string>();
            if (pages != null) foreach (HowToPlayPages.Page page in pages.Pages) texts.Add(page.text ?? "");
            textSize = LobbyUiKit.FitTextSize(bodyLabel, texts, textWidth, textHeight, Theme.howToPlayTextSize, Theme.howToPlayTextMinSize, out allFit);
            if (!allFit) Debug.LogWarning("[LOBBY] the longest How to play page does not fit its box even at the smallest text size");

            ShowPage(0);
        }

        private void BuildSidebar(Transform parent)
        {
            var go = new GameObject("Page list", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image fill = go.GetComponent<Image>();
            fill.color = Theme.howToPlaySidebarColor;
            fill.raycastTarget = false;
            LobbyUiKit.Round(fill, Theme.lobbyCardRadius);
            LobbyUiKit.Size(go, Theme.howToPlaySidebarWidth, -1f, 0f, 0f);
            // Round on the card's left corners only: the right half is a plain rectangle laid over the rounded one.
            var square = new GameObject("Square edge", typeof(RectTransform), typeof(Image));
            square.transform.SetParent(go.transform, false);
            RectTransform squareRect = (RectTransform)square.transform;
            squareRect.anchorMin = new Vector2(0.5f, 0f);
            squareRect.anchorMax = Vector2.one;
            squareRect.offsetMin = squareRect.offsetMax = Vector2.zero;
            square.AddComponent<LayoutElement>().ignoreLayout = true;
            Image squareImage = square.GetComponent<Image>();
            squareImage.color = Theme.howToPlaySidebarColor;
            squareImage.raycastTarget = false;

            VerticalLayoutGroup list = go.AddComponent<VerticalLayoutGroup>();
            list.padding = LobbyUiKit.Pad(0f, 0f, Theme.howToPlaySidebarPaddingY, Theme.howToPlaySidebarPaddingY);
            list.spacing = Theme.howToPlaySidebarGap;
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;

            TextMeshProUGUI heading = Kit.Text(go.transform, "Heading", Theme.howToPlaySidebarTitle, Kit.Bold, Theme.howToPlaySidebarTitleSize, Theme.lobbyDimColor,
                TextAlignmentOptions.MidlineLeft, Theme.howToPlaySidebarTitleSpacing);
            heading.margin = new Vector4(Theme.howToPlaySidebarPaddingX, 0f, 0f, 0f);
            LobbyUiKit.Size(heading.gameObject, -1f, Theme.howToPlaySidebarTitleSize * Theme.howToPlayLineHeightFactor);
            var gap = new GameObject("Gap", typeof(RectTransform));
            gap.transform.SetParent(go.transform, false);
            LobbyUiKit.Size(gap, 0f, Mathf.Max(0f, Theme.howToPlaySidebarTitleGap - Theme.howToPlaySidebarGap), 0f, 0f);

            for (int i = 0; i < titles.Count; i++)
            {
                int page = i;
                LobbyButton button = Kit.MakeButton(go.transform, "Entry " + (i + 1), titles[i], Kit.Body, Theme.howToPlayEntryTextSize, Theme.lobbyMutedColor,
                    Theme.howToPlaySidebarColor, 0f, Color.clear, 0f);
                LobbyUiKit.Size(button.Root, -1f, Theme.howToPlayEntryHeight);
                button.Label.alignment = TextAlignmentOptions.MidlineLeft;
                button.Label.margin = new Vector4(Theme.howToPlaySidebarPaddingX, 0f, 0f, 0f);
                var barGo = new GameObject("Selected bar", typeof(RectTransform), typeof(Image));
                barGo.transform.SetParent(button.Fill.transform, false);
                RectTransform barRect = (RectTransform)barGo.transform;
                barRect.anchorMin = Vector2.zero;
                barRect.anchorMax = new Vector2(0f, 1f);
                barRect.pivot = new Vector2(0f, 0.5f);
                barRect.sizeDelta = new Vector2(Theme.howToPlaySelectedBarWidth, 0f);
                barRect.anchoredPosition = Vector2.zero;
                Image bar = barGo.GetComponent<Image>();
                bar.color = Theme.lobbyCyanColor;
                bar.raycastTarget = false;
                button.Button.onClick.AddListener(() => ShowPage(page));
                entries.Add(new Entry { Button = button, Bar = bar });
            }
        }

        private void BuildFooter(Transform parent)
        {
            HorizontalLayoutGroup footer = LobbyUiKit.HGroup(parent, "Footer", Theme.howToPlayContentGap, TextAnchor.MiddleLeft);
            LobbyUiKit.Size(footer.gameObject, -1f, Theme.howToPlayButtonHeight);

            previousButton = Kit.MakeButton(footer.transform, "Previous", "", Kit.Body, Theme.howToPlayButtonTextSize, Theme.lobbyOffWhiteColor,
                Theme.lobbyDarkColor, Theme.lobbyCornerRadius, Theme.lobbyBorderColor, Theme.lobbyBorderWidth);
            LobbyUiKit.Size(previousButton.Root, -1f, Theme.howToPlayButtonHeight);
            previousGroup = previousButton.Root.AddComponent<CanvasGroup>();
            previousButton.Button.onClick.AddListener(() => ShowPage(HowToPlayRules.Previous(index, titles.Count)));

            LobbyUiKit.Spacer(footer.transform);
            // The counter is centred on the page whichever buttons are drawn (the first and last page have one button only).
            counterLabel = Kit.Text(footer.transform, "Counter", "", Kit.Body, Theme.howToPlayCounterSize, Theme.lobbyDimColor, TextAlignmentOptions.Midline);
            counterLabel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform counterRect = (RectTransform)counterLabel.transform;
            counterRect.anchorMin = counterRect.anchorMax = counterRect.pivot = new Vector2(0.5f, 0.5f);
            counterRect.sizeDelta = new Vector2(Theme.howToPlayCounterSize * Theme.howToPlayCounterWidthFactor, Theme.howToPlayCounterSize * Theme.howToPlayLineHeightFactor);
            counterRect.anchoredPosition = Vector2.zero;

            nextButton = Kit.MakeButton(footer.transform, "Next", "", Kit.Bold, Theme.howToPlayButtonTextSize, Theme.lobbyDarkTextColor,
                Theme.lobbyOffWhiteColor, Theme.lobbyCornerRadius, Theme.lobbyOffWhiteColor, 0f);
            LobbyUiKit.Size(nextButton.Root, -1f, Theme.howToPlayButtonHeight);
            nextGroup = nextButton.Root.AddComponent<CanvasGroup>();
            nextButton.Button.onClick.AddListener(() => ShowPage(HowToPlayRules.Next(index, titles.Count)));
        }

        // ---- showing a page ----

        private void ShowPage(int page)
        {
            index = HowToPlayRules.Clamp(page, titles.Count);
            bool any = titles.Count > 0 && pages != null;
            HowToPlayPages.Page current = any ? pages.Pages[index] : default;

            Heading.text = any ? current.title ?? "" : "";
            bodyLabel.text = any ? current.text ?? "" : "";
            bodyLabel.gameObject.SetActive(any);
            pictureImage.sprite = any ? current.picture : null;
            pictureImage.enabled = any && current.picture != null;
            counterLabel.text = HowToPlayRules.CounterText(index, titles.Count, Theme.howToPlayCounterFormat);
            counterLabel.gameObject.SetActive(any);

            for (int i = 0; i < entries.Count; i++)
            {
                bool selected = i == index;
                Entry entry = entries[i];
                entry.Bar.enabled = selected;
                entry.Button.Fill.color = selected ? Theme.lobbyPanelColor : Theme.howToPlaySidebarColor;
                entry.Button.EnabledFill = entry.Button.Fill.color;
                entry.Button.Label.color = selected ? Theme.lobbyOffWhiteColor : Theme.lobbyMutedColor;
                entry.Button.Label.font = selected ? Kit.Bold : Kit.Body;
            }

            SetNeighbour(previousButton, previousGroup, HowToPlayRules.PreviousTitle(titles, index), Theme.howToPlayPreviousFormat);
            SetNeighbour(nextButton, nextGroup, HowToPlayRules.NextTitle(titles, index), Theme.howToPlayNextFormat);
        }

        /// <summary>A previous or next button names its neighbouring page; with no neighbour (the first or last page) it cannot be pressed and is
        /// not drawn, but keeps its place so the counter does not shift.</summary>
        private void SetNeighbour(LobbyButton button, CanvasGroup group, string neighbourTitle, string format)
        {
            bool has = neighbourTitle != null;
            button.Label.text = has ? Format(format, neighbourTitle) : "";
            LobbyUiKit.WidenToLabel(button, Theme.howToPlayButtonPadding);
            button.Button.interactable = has;
            group.alpha = has ? 1f : 0f;
            group.blocksRaycasts = has;
        }

        private static string Format(string format, string argument)
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
