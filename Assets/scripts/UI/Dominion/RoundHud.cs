using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// The round bar at the top centre during a round (board DomHud A, and "3v3v3 version of A"). It carries no points (ScoreBars shows them):
    /// 2v2: a team block either side of the round clock; 3v3v3: three team blocks and the clock at the end. A block is the team's colour line with
    /// its round-win dots. "+200 CYAN" flashes under the holder's block when the centre pays out. DominionHudText builds the words; sizes, colours
    /// and words are UiTheme fields (Dominion HUD). It draws only what
    /// DominionHud hands it each frame and rewrites a text only when its value changed.
    /// </summary>
    public sealed class RoundHud
    {
        private sealed class Block
        {
            public int Team;
            public RectTransform Rect;
            public Image[] Dots;
            public float CentreX;       // the block's centre from the bar's left edge, for the flash
            public int ShownWins = -1;
        }

        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform parent;
        private readonly List<Block> blocks = new List<Block>();
        private RectTransform root;
        private TextMeshProUGUI roundLabel, clockLabel, flashLabel;
        private int builtTeamCount;
        private float flashUntil;
        // The whole numbers the labels were last built from: a label's words are formatted only when one moved.
        private bool roundDrawn, clockDrawn;
        private int drawnRoundNumber, drawnMaxRounds, drawnClockSeconds;
        private bool shownSudden, shownOvertime;

        /// <summary>The words the round label and the clock slot show now. Recorders and wiring tests read them.</summary>
        public string RoundText => roundLabel != null ? roundLabel.text : "";
        public string ClockText => clockLabel != null ? clockLabel.text : "";
        public string FlashTextShown => flashLabel != null && flashLabel.gameObject.activeSelf ? flashLabel.text : "";
        public bool IsShowing => root != null && root.gameObject.activeSelf;

        public RoundHud(LobbyUiKit kit, Transform parent)
        {
            this.kit = kit;
            theme = kit.Theme;
            this.parent = parent;
        }

        public void SetVisible(bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        /// <summary>teams are the match's team ids in order (2 or 3); clockSeconds is the round clock (ignored in sudden death), or the overtime's own
        /// clock when overtime is on: the small heading then says OVERTIME instead of the round.</summary>
        public void Refresh(int[] teams, int round, int maxRounds, int clockSeconds, bool suddenDeath, bool overtime, int[] wins, int winsToWin, string[] teamNames)
        {
            if (teams == null || teams.Length < 2) { SetVisible(false); return; }
            dotCountCache = Mathf.Clamp(winsToWin, 1, 5);
            if (root == null || builtTeamCount != teams.Length || blocks.Count == 0 || blocks[0].Dots.Length != dotCountCache) Build(teams);
            SetVisible(true);

            if (!roundDrawn || round != drawnRoundNumber || maxRounds != drawnMaxRounds || overtime != shownOvertime)
            {
                roundLabel.text = overtime ? theme.dominionBarOvertimeText : DominionHudText.RoundLabel(theme.dominionRoundLabelFormat, round, maxRounds);
                roundLabel.color = overtime ? theme.dominionGoldColor : theme.dominionMutedColor;
                roundDrawn = true; drawnRoundNumber = round; drawnMaxRounds = maxRounds; shownOvertime = overtime;
            }

            // In sudden death the clock slot says SUDDEN DEATH whatever the seconds are, so only the mode matters there.
            if (!clockDrawn || suddenDeath != shownSudden || (!suddenDeath && clockSeconds != drawnClockSeconds))
            {
                clockLabel.text = suddenDeath ? theme.dominionBarSuddenText : DominionHudText.Clock(clockSeconds);
                clockLabel.fontSize = suddenDeath ? theme.dominionBarSuddenSize : ClockSize();
                clockLabel.color = suddenDeath ? theme.suddenDeathColor : theme.lobbyOffWhiteColor;
                clockDrawn = true; drawnClockSeconds = clockSeconds;
                shownSudden = suddenDeath;
            }

            foreach (Block block in blocks)
            {
                int teamWins = Cell(wins, block.Team);
                if (teamWins != block.ShownWins) { PaintDots(block, teamWins); block.ShownWins = teamWins; }
            }

            if (flashLabel.gameObject.activeSelf)
            {
                float left = flashUntil - Time.unscaledTime;
                if (left <= 0f) flashLabel.gameObject.SetActive(false);
                else flashLabel.alpha = Mathf.Clamp01(left / theme.dominionFlashFadeSeconds);
            }
        }

        /// <summary>The centre paid this team: "+200 CYAN" shows under its block for Flash Seconds.</summary>
        public void Flash(int team, int pointsPaid, string[] teamNames)
        {
            if (root == null) return;
            foreach (Block block in blocks)
            {
                if (block.Team != team) continue;
                flashLabel.text = DominionHudText.FlashText(theme.dominionFlashFormat, pointsPaid, teamNames, team);
                flashLabel.color = TextColour(team);
                flashLabel.alpha = 1f;
                RectTransform rect = flashLabel.rectTransform;
                rect.anchoredPosition = new Vector2(block.CentreX, -(theme.dominionBarHeight + theme.dominionFlashGap));
                flashLabel.gameObject.SetActive(true);
                flashUntil = Time.unscaledTime + theme.dominionFlashSeconds;
                return;
            }
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            blocks.Clear();
        }

        // ---------------------------------------------------------------- building

        private float ClockSize() => builtTeamCount >= 3 ? theme.dominionClock3Size : theme.dominionClockSize;

        private void Build(int[] teams)
        {
            Destroy();
            builtTeamCount = teams.Length;
            bool three = teams.Length >= 3;
            float side = Mathf.Max(three ? theme.dominionBar3SideWidth : theme.dominionBarSideWidth, DotsWidth() + 2f * theme.dominionBarDotsMargin);
            float clockWidth = three ? theme.dominionBar3ClockWidth : theme.dominionBarClockWidth;
            float height = theme.dominionBarHeight;
            float total = three ? side * teams.Length + clockWidth : side * 2f + clockWidth;

            LobbyBox box = kit.Box(parent, "Round Bar", theme.dominionBarFill, theme.dominionBarRadius, Color.clear, 0f);
            box.Fill.raycastTarget = false;
            root = box.Outer;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -theme.dominionBarTop);
            root.sizeDelta = new Vector2(total, height);

            float x = 0f;
            if (three)
            {
                for (int i = 0; i < teams.Length; i++)
                {
                    blocks.Add(BuildBlock(teams[i], x, side, height));
                    x += side;
                }
                BuildClock(x, clockWidth, height, rounded: true);
            }
            else
            {
                blocks.Add(BuildBlock(teams[0], x, side, height));
                x += side;
                BuildClock(x, clockWidth, height, rounded: false);
                x += clockWidth;
                blocks.Add(BuildBlock(teams[1], x, side, height));
            }

            flashLabel = kit.Text(root, "Centre Flash", "", kit.Display, theme.dominionFlashSize, theme.dominionGoldColor, TextAlignmentOptions.Midline);
            flashLabel.overflowMode = TextOverflowModes.Overflow;
            RectTransform flashRect = flashLabel.rectTransform;
            flashRect.anchorMin = flashRect.anchorMax = new Vector2(0f, 1f);
            flashRect.pivot = new Vector2(0.5f, 1f);
            flashRect.sizeDelta = new Vector2(side * 1.5f, theme.dominionFlashSize * 1.5f);
            flashLabel.gameObject.SetActive(false);
            roundDrawn = clockDrawn = false;
            shownOvertime = false;
            foreach (Block block in blocks) block.ShownWins = -1;
        }

        private Block BuildBlock(int team, float x, float width, float height)
        {
            var block = new Block { Team = team, CentreX = x + width * 0.5f };
            var go = new GameObject("Team " + team, typeof(RectTransform));
            go.transform.SetParent(root, false);
            block.Rect = (RectTransform)go.transform;
            TopLeft(block.Rect, x, 0f, width, height);

            // The underline in the team's colour; the two outer ends are pulled in by the bar's corner so the line never pokes out of the rounded corner.
            var edgeGo = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            edgeGo.transform.SetParent(block.Rect, false);
            RectTransform edge = (RectTransform)edgeGo.transform;
            edge.anchorMin = new Vector2(0f, 0f);
            edge.anchorMax = new Vector2(1f, 0f);
            edge.pivot = new Vector2(0.5f, 0f);
            edge.sizeDelta = new Vector2(0f, theme.dominionBarEdgeThickness);
            edge.anchoredPosition = Vector2.zero;
            Image edgeImage = edgeGo.GetComponent<Image>();
            edgeImage.color = TeamColour(team);
            edgeImage.raycastTarget = false;

            // One dot per round win needed to win the match, centred above the colour line.
            int dotCount = DotCount();
            block.Dots = new Image[dotCount];
            float first = (width - DotsWidth()) * 0.5f;
            float top = (height - theme.dominionBarEdgeThickness - theme.dominionDotSize) * 0.5f;
            for (int i = 0; i < dotCount; i++)
            {
                var dotGo = new GameObject("Dot " + (i + 1), typeof(RectTransform), typeof(Image));
                dotGo.transform.SetParent(block.Rect, false);
                TopLeft((RectTransform)dotGo.transform, first + i * (theme.dominionDotSize + theme.dominionDotGap), top, theme.dominionDotSize, theme.dominionDotSize);
                Image dot = dotGo.GetComponent<Image>();
                dot.raycastTarget = false;
                block.Dots[i] = dot;
            }
            return block;
        }

        private int dotCountCache = 2;

        private int DotCount() => dotCountCache;

        private float DotsWidth() => DotCount() * theme.dominionDotSize + (DotCount() - 1) * theme.dominionDotGap;

        private void BuildClock(float x, float width, float height, bool rounded)
        {
            LobbyBox clock = kit.Box(root, "Clock", theme.dominionBarClockFill, rounded ? theme.dominionBarRadius : 0f, Color.clear, 0f);
            clock.Fill.raycastTarget = false;
            TopLeft(clock.Outer, x, 0f, width, height);

            roundLabel = kit.Text(clock.Outer, "Round", "", kit.Bold, theme.dominionRoundLabelSize, theme.dominionMutedColor, TextAlignmentOptions.Midline, theme.dominionLabelSpacing);
            roundLabel.overflowMode = TextOverflowModes.Overflow;
            RectTransform roundRect = roundLabel.rectTransform;
            roundRect.anchorMin = new Vector2(0f, 1f);
            roundRect.anchorMax = new Vector2(1f, 1f);
            roundRect.pivot = new Vector2(0.5f, 1f);
            roundRect.sizeDelta = new Vector2(0f, theme.dominionRoundLabelSize * 1.5f);
            roundRect.anchoredPosition = new Vector2(0f, -(height * theme.dominionRoundLabelTopShare));

            clockLabel = kit.Text(clock.Outer, "Clock", "0:00", kit.Display, ClockSize(), theme.lobbyOffWhiteColor, TextAlignmentOptions.Midline);
            clockLabel.overflowMode = TextOverflowModes.Overflow;
            RectTransform clockRect = clockLabel.rectTransform;
            clockRect.anchorMin = new Vector2(0f, 1f);
            clockRect.anchorMax = new Vector2(1f, 1f);
            clockRect.pivot = new Vector2(0.5f, 1f);
            clockRect.sizeDelta = new Vector2(0f, ClockSize() * 1.3f);
            clockRect.anchoredPosition = new Vector2(0f, -(height * theme.dominionRoundLabelTopShare + theme.dominionRoundLabelSize * 1.5f));
        }

        // ---------------------------------------------------------------- the dots

        private void PaintDots(Block block, int wins)
        {
            for (int i = 0; i < block.Dots.Length; i++)
            {
                bool won = i < wins;
                Image dot = block.Dots[i];
                dot.sprite = won ? GeneratedSprites.Disc : DominionHudSprites.EmptyDot(theme);
                dot.color = won ? TeamColour(block.Team) : theme.dominionDimColor;
            }
        }

        // ---------------------------------------------------------------- helpers

        private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static int Cell(int[] values, int index) => values != null && index >= 0 && index < values.Length ? values[index] : 0;

        private Color TeamColour(int team) => Pick(theme.dominionTeamColors, team);
        private Color TextColour(int team) => Pick(theme.dominionTeamTextColors, team);

        private static Color Pick(Color[] colours, int index) =>
            colours != null && colours.Length > 0 ? colours[Mathf.Clamp(index, 0, colours.Length - 1)] : Color.white;
    }
}
