using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// The card between rounds (board DomBreak A): "ROUND 1 · PURPLE WINS · 540 / 620" (a tie: "TIED"; a shared round: "SHARED ROUND" and who gets a win), the round-win dots,
    /// the match score and MATCH POINT from round 2 on, what the next round opens in the shop, a PICK YOUR BUILD (P) button and "ROUND 2 STARTS IN 14". The last seconds (Break Countdown Seconds in the Dominion Config)
    /// read big in the same place. The break before round 1 has no result: GET READY, the same opens line and countdown.
    /// It sits high so the arena stays in view and draws BELOW the shop (its canvas sorts under the shop's): the shop opens on top and nothing of the
    /// card blocks a click on it. Only its button takes the mouse; a spectator has no shop, so no button. Words, sizes, colours are UiTheme fields.
    /// </summary>
    public sealed class BreakCard
    {
        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform parent;
        private RectTransform card;
        private TextMeshProUGUI header, headline, sharedLine, scoreLine, matchPointLine, opens, countdown;
        private bool countdownIsBig;
        private LobbyButton pick;
        private GameObject pickRow;
        private GameObject pointsRow, winsRow;
        private readonly List<TextMeshProUGUI> pointsTexts = new List<TextMeshProUGUI>();
        private readonly List<Image> winDots = new List<Image>();
        private readonly List<int> winDotTeam = new List<int>();
        private TextMeshProUGUI pointsLabel;
        private int builtTeamCount, builtDotCount;
        // What the card was last drawn from: compared as whole numbers each frame, words rebuilt only when one moved (a string key would allocate every frame).
        private bool drawn;
        private int drawnRound;
        private bool drawnFirstBreak, drawnCanPick, drawnInSuddenDeath;
        private int[] drawnPoints = System.Array.Empty<int>(), drawnWins = System.Array.Empty<int>(), drawnWinners = System.Array.Empty<int>();
        private bool pulsing;
        private int pulsedRound = -1;
        private float pulseStart;
        private bool countdownDrawn;
        private int drawnSeconds, drawnCountdownRound, drawnBigFrom;

        public bool IsShowing => card != null && card.gameObject.activeSelf;
        public string HeaderText => header != null ? header.text : "";
        public string HeadlineText => headline != null ? headline.text : "";
        public string SharedLineText => sharedLine != null && sharedLine.gameObject.activeSelf ? sharedLine.text : "";
        public string ScoreText => scoreLine != null && scoreLine.gameObject.activeSelf ? scoreLine.text : "";
        public string MatchPointText => matchPointLine != null && matchPointLine.gameObject.activeSelf ? matchPointLine.text : "";
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

        /// <summary>round is the round the break leads to; points are the finished round's final points (the room keeps them through the break);
        /// winners are the teams the room recorded as winning it (several = shared round, none = tie); inSuddenDeath = its own sudden death decided it;
        /// canPick is false for a spectator (no shop).</summary>
        public void Refresh(int[] teams, int round, bool firstBreak, int[] points, int[] winners, bool inSuddenDeath, int[] wins, int secondsLeft, int bigFromSeconds,
                            int[] depthByRound, int[] armorByRound, string[] teamNames, bool canPick, System.Action onPick)
        {
            if (teams == null || teams.Length < 2) { SetVisible(false); return; }
            int dots = Mathf.Max(1, dotsToWin);
            if (card == null || builtTeamCount != teams.Length || builtDotCount != dots) Build(teams, dots);
            SetVisible(true);

            if (!drawn || round != drawnRound || firstBreak != drawnFirstBreak || canPick != drawnCanPick || inSuddenDeath != drawnInSuddenDeath
                || !SameInts(points, drawnPoints) || !SameInts(wins, drawnWins) || !SameInts(winners, drawnWinners))
            {
                drawn = true;
                drawnRound = round; drawnFirstBreak = firstBreak; drawnCanPick = canPick; drawnInSuddenDeath = inSuddenDeath;
                drawnPoints = points != null ? (int[])points.Clone() : System.Array.Empty<int>();
                drawnWins = wins != null ? (int[])wins.Clone() : System.Array.Empty<int>();
                drawnWinners = winners != null ? (int[])winners.Clone() : System.Array.Empty<int>();

                int finished = Mathf.Max(1, round - 1);
                // The room's record of who won the round decides the words (a shared round names every winner), never the points leader.
                bool shared = !firstBreak && winners != null && winners.Length >= 2;
                int winner = firstBreak || shared || winners == null || winners.Length == 0 ? -1 : winners[0];
                header.text = string.Format(System.Globalization.CultureInfo.InvariantCulture, theme.dominionBreakHeaderFormat, firstBreak ? round : finished);
                headline.text = firstBreak ? theme.dominionBreakFirstText
                    : shared ? theme.dominionBreakSharedText
                    : DominionHudText.BreakHeadline(winner, teamNames, theme.dominionBreakWinsFormat, theme.dominionBreakTiedText);
                string[] hex = TeamHex();
                bool suddenDeathLine = !firstBreak && !shared && inSuddenDeath && winner >= 0;
                sharedLine.gameObject.SetActive(shared || suddenDeathLine);
                if (shared)
                    sharedLine.text = DominionHudText.SharedRoundLine(theme.dominionBreakSharedLineFormat, winners, teamNames, hex, theme.dominionBreakNamesSeparator, theme.dominionBreakNamesLast);
                else if (suddenDeathLine)
                    sharedLine.text = theme.dominionBreakSuddenDeathLine;
                scoreLine.gameObject.SetActive(!firstBreak);
                scoreLine.text = firstBreak ? "" : DominionHudText.MatchScoreLine(teams, wins, teamNames, hex, theme.dominionBreakScoreDash);
                string matchPoint = firstBreak ? "" : DominionHudText.MatchPointLine(theme.dominionBreakMatchPointFormat,
                    DominionHudText.MatchPointTeams(teams, wins, dotsToWin), teamNames, hex, theme.dominionBreakNamesSeparator);
                matchPointLine.gameObject.SetActive(matchPoint.Length > 0);
                matchPointLine.text = matchPoint;
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
                switch (DominionHudText.PulseStepOnRedraw(shared, round, pulsedRound))
                {
                    case DominionHudText.PulseStep.Stop:
                        pulsing = false;
                        ResetDotScales();
                        break;
                    case DominionHudText.PulseStep.Start:
                        pulsing = true;
                        pulsedRound = round;
                        pulseStart = Time.unscaledTime;
                        break;
                }
                opens.text = DominionHudText.OpensLine(round, depthByRound, armorByRound,
                    new OpensTexts(theme.dominionOpensFormat, theme.dominionOpensFirstText, theme.dominionOpensWeaponFamily, theme.dominionOpensWeaponUpgrade,
                                   theme.dominionOpensArmorOne, theme.dominionOpensArmorMore, theme.dominionOpensAnd, theme.dominionOpensNothing));
                pickRow.SetActive(canPick);
                onPickAction = onPick;
            }

            ApplyPulse();

            if (!countdownDrawn || secondsLeft != drawnSeconds || round != drawnCountdownRound || bigFromSeconds != drawnBigFrom)
            {
                countdownDrawn = true;
                drawnSeconds = secondsLeft; drawnCountdownRound = round; drawnBigFrom = bigFromSeconds;
                string line = DominionHudText.BreakCountdown(theme.dominionBreakStartsFormat, theme.dominionBreakBigFormat, round, secondsLeft, bigFromSeconds, out bool isBig);
                // The last seconds read big: the same line grows in the card instead of a second text over the arena, so nothing is covered.
                if (isBig != countdownIsBig)
                {
                    countdownIsBig = isBig;
                    float size = isBig ? theme.dominionBreakBigSize : theme.dominionBreakCountdownSize;
                    countdown.fontSize = size;
                    LobbyUiKit.Size(countdown.gameObject, -1f, size * 1.5f);
                }
                countdown.text = line;
            }
        }

        /// <summary>The newest round-win dot of every team that shared the round pops once, then every dot is back at normal size.</summary>
        private void ApplyPulse()
        {
            if (!pulsing) return;
            float elapsed = Time.unscaledTime - pulseStart;
            for (int i = 0; i < winDots.Count; i++)
            {
                int team = winDotTeam[i];
                int slot = i % Mathf.Max(1, dotsToWin);
                bool pulses = DominionHudText.PulsesDot(drawnWinners, team, slot, team < drawnWins.Length ? drawnWins[team] : 0);
                float scale = pulses ? DominionHudText.PulseScale(elapsed, theme.dominionBreakPulseSeconds, theme.dominionBreakPulseScale) : 1f;
                winDots[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
            if (elapsed >= theme.dominionBreakPulseSeconds)
            {
                pulsing = false;
                ResetDotScales();
            }
        }

        private void ResetDotScales()
        {
            for (int i = 0; i < winDots.Count; i++) winDots[i].rectTransform.localScale = Vector3.one;
        }

        private string[] TeamHex()
        {
            Color[] colours = theme.dominionTeamTextColors;
            int count = colours != null ? colours.Length : 0;
            var hex = new string[count];
            for (int i = 0; i < count; i++) hex[i] = ColorUtility.ToHtmlStringRGB(colours[i]);
            return hex;
        }

        private static bool SameInts(int[] a, int[] b)
        {
            int la = a != null ? a.Length : 0, lb = b != null ? b.Length : 0;
            if (la != lb) return false;
            for (int i = 0; i < la; i++) if (a[i] != b[i]) return false;
            return true;
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
            pulsing = false;
            pulsedRound = -1;
            pointsTexts.Clear(); winDots.Clear(); winDotTeam.Clear();
            drawn = false;
            countdownDrawn = false;
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

            header = Line("Header", kit.Bold, theme.dominionBreakHeaderSize, theme.dominionMutedColor, theme.dominionBreakHeaderSize * theme.resultHeadingSpacingShare);
            headline = Line("Headline", kit.Display, theme.dominionBreakHeadlineSize, theme.lobbyOffWhiteColor, 0f);
            sharedLine = Line("Shared Line", kit.Body, theme.dominionBreakSharedLineSize, theme.lobbyOffWhiteColor, 0f);

            // The two (or three) teams' points. With two teams the small word "points" sits between them like the board; with three it sits under the
            // scores (a word trailing after the third number reads as part of it).
            Transform pointsParent = card;
            if (teams.Length != 2)
            {
                VerticalLayoutGroup block = LobbyUiKit.VGroup(card, "Points Block", 0f, TextAnchor.UpperCenter);
                pointsParent = block.transform;
                pointsRow = block.gameObject;
            }
            HorizontalLayoutGroup points = LobbyUiKit.HGroup(pointsParent, "Points", theme.dominionBreakPointsGap, TextAnchor.MiddleCenter);
            if (teams.Length == 2) pointsRow = points.gameObject;
            LobbyUiKit.Size(points.gameObject, -1f, theme.dominionBreakPointsSize * 1.5f);
            pointsTexts.Clear();
            for (int i = 0; i < teams.Length; i++)
            {
                if (teams.Length == 2 && i == 1)
                {
                    pointsLabel = kit.Text(points.transform, "Points Label", theme.dominionBreakPointsLabel, kit.Display, theme.dominionBreakPointsLabelSize, theme.dominionDimColor, TextAlignmentOptions.Midline);
                    pointsLabel.overflowMode = TextOverflowModes.Overflow;
                }
                TextMeshProUGUI value = kit.Text(points.transform, "Points " + teams[i], "0", kit.Display, theme.dominionBreakPointsSize,
                    teams.Length == 2 && i == 0 ? theme.lobbyOffWhiteColor : TextColour(teams[i]), TextAlignmentOptions.Midline);
                value.overflowMode = TextOverflowModes.Overflow;
                pointsTexts.Add(value);
            }
            if (teams.Length != 2)
            {
                pointsLabel = kit.Text(pointsParent, "Points Label", theme.dominionBreakPointsLabel, kit.Display, theme.dominionBreakPointsLabelSize, theme.dominionDimColor, TextAlignmentOptions.Midline);
                pointsLabel.overflowMode = TextOverflowModes.Overflow;
                LobbyUiKit.Size(pointsLabel.gameObject, -1f, theme.dominionBreakPointsLabelSize * 1.5f);
            }

            // "Round wins ○○ · ●○": one group of dots per team in its colour.
            HorizontalLayoutGroup wins = LobbyUiKit.HGroup(card, "Round Wins", theme.dominionBreakWinsGap, TextAnchor.MiddleCenter);
            winsRow = wins.gameObject;
            LobbyUiKit.Size(winsRow, -1f, theme.dominionBreakSmallSize * 1.5f);
            TextMeshProUGUI winsLabel = kit.Text(wins.transform, "Label", theme.dominionBreakWinsLabel, kit.Body, theme.dominionBreakSmallSize, theme.lobbyMutedColor, TextAlignmentOptions.Midline);
            winsLabel.overflowMode = TextOverflowModes.Overflow;
            winDots.Clear(); winDotTeam.Clear();
            for (int t = 0; t < teams.Length; t++)
            {
                if (t > 0)
                {
                    TextMeshProUGUI sep = kit.Text(wins.transform, "Separator", theme.dominionBreakWinsSeparator, kit.Body, theme.dominionBreakSmallSize, theme.lobbyMutedColor, TextAlignmentOptions.Midline);
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

            scoreLine = Line("Match Score", kit.Display, theme.dominionBreakScoreSize, theme.lobbyOffWhiteColor, 0f);
            matchPointLine = Line("Match Point", kit.Bold, theme.dominionBreakMatchPointSize, theme.dominionGoldColor, 0f);

            var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(card, false);
            divider.GetComponent<Image>().color = theme.lobbyBorderColor;
            divider.GetComponent<Image>().raycastTarget = false;
            LobbyUiKit.Size(divider, -1f, theme.dominionBreakDividerThickness);

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

            drawn = false;
            countdownDrawn = false;
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
