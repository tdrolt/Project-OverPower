using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// Dominion Task 9, the card between rounds (board DomBreak A): "ROUND 1 · PURPLE WINS · 540 / 620" (a tied round: "TIED"), the round-win dots,
    /// what the next round opens in the shop, a PICK YOUR BUILD (P) button, and "ROUND 2 STARTS IN 14". The last few seconds (Break Countdown
    /// Seconds in the Dominion Config) read big, in the same place: "Round 2 starts in 5…". The break before round 1 has no
    /// result: it reads GET READY and the same opens line and countdown.
    ///
    /// The card sits high on the screen so the arena stays in view, and it draws BELOW the shop (its canvas sorts under the shop's), so the shop
    /// opens on top of it and nothing of the card blocks a click on the shop. Only its button takes the mouse. A spectator has no shop, so the
    /// button is not shown for them. All words, sizes and colours are UiTheme fields (Dominion HUD).
    /// </summary>
    public sealed class BreakCard
    {
        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform parent;
        private RectTransform card;
        private TextMeshProUGUI header, headline, opens, countdown;
        private bool countdownIsBig;
        private LobbyButton pick;
        private GameObject pickRow;
        private GameObject pointsRow, winsRow;
        private readonly List<TextMeshProUGUI> pointsTexts = new List<TextMeshProUGUI>();
        private readonly List<Image> winDots = new List<Image>();
        private readonly List<int> winDotTeam = new List<int>();
        private TextMeshProUGUI pointsLabel;
        private int builtTeamCount, builtDotCount;
        private string shownKey;

        public bool IsShowing => card != null && card.gameObject.activeSelf;
        public string HeaderText => header != null ? header.text : "";
        public string HeadlineText => headline != null ? headline.text : "";
        public string OpensText => opens != null ? opens.text : "";
        public string CountdownText => countdown != null && !countdownIsBig ? countdown.text : "";
        public string BigText => countdown != null && countdownIsBig ? countdown.text : "";
        public bool PickShown => pick != null && pick.Root.activeInHierarchy;
        public string PointsText => pointsTexts.Count == 0 ? "" : string.Join("/", pointsTexts.ConvertAll(t => t.text));

        public BreakCard(LobbyUiKit kit, Transform parent)
        {
            this.kit = kit;
            theme = kit.Theme;
            this.parent = parent;
        }

        public void SetVisible(bool visible)
        {
            if (card != null && card.gameObject.activeSelf != visible) card.gameObject.SetActive(visible);
        }

        /// <summary>One frame of the card. round is the round the break leads to; points are the finished round's final points (the room keeps
        /// them through the break); firstBreak is the break before round 1; canPick is true for a player with a body (a spectator has no shop).</summary>
        public void Refresh(int[] teams, int round, bool firstBreak, int[] points, int[] wins, int secondsLeft, int bigFromSeconds,
                            int[] depthByRound, int[] armorByRound, string[] teamNames, bool canPick, System.Action onPick)
        {
            if (teams == null || teams.Length < 2) { SetVisible(false); return; }
            int dots = Mathf.Max(1, dotsToWin);
            if (card == null || builtTeamCount != teams.Length || builtDotCount != dots) Build(teams, dots);
            SetVisible(true);

            int finished = Mathf.Max(1, round - 1);
            int winner = firstBreak ? -1 : DominionRules.RoundWinner(points);
            string key = $"{round}|{firstBreak}|{winner}|{string.Join(",", points ?? new int[0])}|{string.Join(",", wins ?? new int[0])}|{canPick}";
            if (key != shownKey)
            {
                shownKey = key;
                header.text = string.Format(System.Globalization.CultureInfo.InvariantCulture, theme.dominionBreakHeaderFormat, firstBreak ? round : finished);
                headline.text = firstBreak ? theme.dominionBreakFirstText : DominionHudText.BreakHeadline(winner, teamNames, theme.dominionBreakWinsFormat, theme.dominionBreakTiedText);
                headline.color = firstBreak || winner < 0 ? theme.lobbyOffWhiteColor : TextColour(winner);
                pointsRow.SetActive(!firstBreak);
                for (int i = 0; i < teams.Length; i++)
                    pointsTexts[i].text = (points != null && teams[i] < points.Length ? points[teams[i]] : 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                for (int i = 0; i < winDots.Count; i++)
                {
                    int team = winDotTeam[i];
                    int slot = i % dots;
                    bool won = wins != null && team < wins.Length && slot < wins[team];
                    winDots[i].sprite = won ? GeneratedSprites.Disc : DominionHudSprites.EmptyDot(theme);
                    winDots[i].color = won ? Pick(theme.dominionTeamColors, team) : theme.dominionDimColor;
                }
                opens.text = DominionHudText.OpensLine(round, depthByRound, armorByRound,
                    new OpensTexts(theme.dominionOpensFormat, theme.dominionOpensFirstText, theme.dominionOpensWeaponFamily, theme.dominionOpensWeaponUpgrade,
                                   theme.dominionOpensArmorOne, theme.dominionOpensArmorMore, theme.dominionOpensAnd, theme.dominionOpensNothing));
                pickRow.SetActive(canPick);
                onPickAction = onPick;
            }

            string line = DominionHudText.BreakCountdown(theme.dominionBreakStartsFormat, theme.dominionBreakBigFormat, round, secondsLeft, bigFromSeconds, out bool isBig);
            // The last seconds read big: the same line grows (in the card, under the button) instead of a second text over the arena, so nothing is covered.
            if (isBig != countdownIsBig)
            {
                countdownIsBig = isBig;
                float size = isBig ? theme.dominionBreakBigSize : theme.dominionBreakCountdownSize;
                countdown.fontSize = size;
                LobbyUiKit.Size(countdown.gameObject, -1f, size * 1.5f);
            }
            if (countdown.text != line) countdown.text = line;
        }

        private int dotsToWin = 2;
        private System.Action onPickAction;

        /// <summary>How many round-win dots a team has (the wins it takes to win the match).</summary>
        public void SetDotsToWin(int dots) => dotsToWin = Mathf.Clamp(dots, 1, 5);

        public void Destroy()
        {
            if (card != null) Object.Destroy(card.gameObject);
            card = null;
            countdownIsBig = false;
            pointsTexts.Clear(); winDots.Clear(); winDotTeam.Clear();
            shownKey = null;
        }

        // ---------------------------------------------------------------- building

        private void Build(int[] teams, int dots)
        {
            Destroy();
            builtTeamCount = teams.Length;
            builtDotCount = dots;

            LobbyBox box = kit.Box(parent, "Break Card", theme.dominionCardFill, theme.dominionCardRadius, Color.clear, 0f);
            box.Fill.raycastTarget = false;
            card = box.Outer;
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -theme.dominionBreakTop);
            card.sizeDelta = new Vector2(theme.dominionBreakWidth, 0f);
            VerticalLayoutGroup column = card.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperCenter;
            column.spacing = theme.dominionCardGap;
            column.padding = LobbyUiKit.Pad(theme.dominionCardPadding.x, theme.dominionCardPadding.x, theme.dominionCardPadding.y, theme.dominionCardPadding.y);
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            ContentSizeFitter fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            header = Line("Header", kit.Bold, theme.dominionBreakHeaderSize, theme.dominionMutedColor, theme.dominionBreakHeaderSize * 0.23f);
            headline = Line("Headline", kit.Display, theme.dominionBreakHeadlineSize, theme.lobbyOffWhiteColor, 0f);

            // The two (or three) teams' points; with two teams the small word "points" sits between them like the board.
            HorizontalLayoutGroup points = LobbyUiKit.HGroup(card, "Points", 28f, TextAnchor.MiddleCenter);
            pointsRow = points.gameObject;
            LobbyUiKit.Size(pointsRow, -1f, theme.dominionBreakPointsSize * 1.5f);
            pointsTexts.Clear();
            for (int i = 0; i < teams.Length; i++)
            {
                if (teams.Length == 2 && i == 1)
                {
                    pointsLabel = kit.Text(points.transform, "Points Label", theme.dominionBreakPointsLabel, kit.Display, theme.dominionBreakSmallSize + 2f, theme.dominionDimColor, TextAlignmentOptions.Midline);
                    pointsLabel.overflowMode = TextOverflowModes.Overflow;
                }
                TextMeshProUGUI value = kit.Text(points.transform, "Points " + teams[i], "0", kit.Display, theme.dominionBreakPointsSize,
                    teams.Length == 2 && i == 0 ? theme.lobbyOffWhiteColor : TextColour(teams[i]), TextAlignmentOptions.Midline);
                value.overflowMode = TextOverflowModes.Overflow;
                pointsTexts.Add(value);
            }
            if (teams.Length != 2)
            {
                pointsLabel = kit.Text(points.transform, "Points Label", theme.dominionBreakPointsLabel, kit.Display, theme.dominionBreakSmallSize + 2f, theme.dominionDimColor, TextAlignmentOptions.Midline);
                pointsLabel.overflowMode = TextOverflowModes.Overflow;
            }

            // "Round wins ○○ · ●○": one group of dots per team in its colour.
            HorizontalLayoutGroup wins = LobbyUiKit.HGroup(card, "Round Wins", 8f, TextAnchor.MiddleCenter);
            winsRow = wins.gameObject;
            LobbyUiKit.Size(winsRow, -1f, theme.dominionBreakSmallSize * 1.5f);
            TextMeshProUGUI winsLabel = kit.Text(wins.transform, "Label", theme.dominionBreakWinsLabel, kit.Body, theme.dominionBreakSmallSize, theme.lobbyMutedColor, TextAlignmentOptions.Midline);
            winsLabel.overflowMode = TextOverflowModes.Overflow;
            winDots.Clear(); winDotTeam.Clear();
            for (int t = 0; t < teams.Length; t++)
            {
                if (t > 0)
                {
                    TextMeshProUGUI sep = kit.Text(wins.transform, "Separator", "·", kit.Body, theme.dominionBreakSmallSize, theme.lobbyMutedColor, TextAlignmentOptions.Midline);
                    sep.overflowMode = TextOverflowModes.Overflow;
                }
                for (int d = 0; d < dots; d++)
                {
                    var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
                    dotGo.transform.SetParent(wins.transform, false);
                    LobbyUiKit.Size(dotGo, theme.dominionDotSize, theme.dominionDotSize);
                    Image dot = dotGo.GetComponent<Image>();
                    dot.raycastTarget = false;
                    winDots.Add(dot);
                    winDotTeam.Add(teams[t]);
                }
            }

            var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(card, false);
            divider.GetComponent<Image>().color = theme.lobbyBorderColor;
            divider.GetComponent<Image>().raycastTarget = false;
            LobbyUiKit.Size(divider, -1f, 2f);

            opens = Line("Opens", kit.Body, theme.dominionBreakOpensSize, theme.lobbyOffWhiteColor, 0f);
            opens.enableWordWrapping = true;
            opens.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Size(opens.gameObject, -1f, theme.dominionBreakOpensSize * 2.6f);

            // The button sits in its own centred row: a button straight in the card's column would be stretched to the card's full width.
            HorizontalLayoutGroup pickLayout = LobbyUiKit.HGroup(card, "Pick Row", 0f, TextAnchor.MiddleCenter);
            pickRow = pickLayout.gameObject;
            pick = kit.MakeButton(pickLayout.transform, "Pick Your Build", theme.dominionBreakPickText, kit.Display, theme.dominionBreakButtonSize, theme.lobbyOffWhiteColor,
                theme.lobbyPurpleColor, theme.lobbyCornerRadius, theme.lobbyPurpleColor, 0f, theme.lobbyButtonSpacing);
            LobbyUiKit.Size(pick.Root, -1f, theme.dominionBreakButtonHeight);
            LobbyUiKit.WidenToLabel(pick, theme.dominionBreakButtonPadding);
            pick.Button.onClick.AddListener(() => onPickAction?.Invoke());

            countdown = Line("Countdown", kit.Display, theme.dominionBreakCountdownSize, theme.dominionGoldColor, 0f);

            shownKey = null;
        }

        private TextMeshProUGUI Line(string name, TMPro.TMP_FontAsset font, float size, Color colour, float spacing)
        {
            TextMeshProUGUI text = kit.Text(card, name, "", font, size, colour, TextAlignmentOptions.Midline, spacing);
            text.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Size(text.gameObject, -1f, size * 1.5f);
            return text;
        }

        private Color TextColour(int team) => Pick(theme.dominionTeamTextColors, team);

        private static Color Pick(Color[] colours, int index) =>
            colours != null && colours.Length > 0 ? colours[Mathf.Clamp(index, 0, colours.Length - 1)] : Color.white;
    }
}
