using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// The score bars at the top right, left of the corner minimap (ScoreBarLayout): one row per team in team order, the team's points in a small box,
    /// its bar in the team's colour (the leader's full, the others by their share of the leader's points), then its round-win dots, filled for the
    /// rounds it has won. Spectators see the same rows. In sudden death only the dots show. Sizes, colours and gaps are UiTheme fields (Dominion HUD:
    /// score bars, and the round-win dots of the round bar); DominionScoreBarRules decides every fill. A value is only rewritten when it moved.
    /// </summary>
    public sealed class ScoreBars
    {
        private sealed class Row
        {
            public int Team;
            public GameObject Box, Track;
            public RectTransform Fill;
            public TextMeshProUGUI Number;
            public Image[] Dots;
            public float ShownFill = -1f;
            public int ShownPoints = int.MinValue;
            public int ShownWins = -1;
        }

        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform parent;
        private readonly List<Row> rows = new List<Row>();
        private RectTransform root;
        private int builtTeamCount, builtDotCount;
        private bool pointsShowing = true;
        private float[] fillBuffer = new float[0]; // reused every frame: the bars allocate nothing once built

        public bool IsShowing => root != null && root.gameObject.activeSelf;

        /// <summary>The fill (0 to 1) the team's bar shows now; -1 when it has not been drawn. Recorders and wiring tests read it.</summary>
        public float ShownFillOf(int team)
        {
            foreach (Row row in rows) if (row.Team == team) return row.ShownFill;
            return -1f;
        }

        /// <summary>The points number the team's bar shows now (int.MinValue before the first draw).</summary>
        public int ShownPointsOf(int team)
        {
            foreach (Row row in rows) if (row.Team == team) return row.ShownPoints;
            return int.MinValue;
        }

        /// <summary>How many of the team's dots are filled now (-1 before the first draw).</summary>
        public int ShownWinsOf(int team)
        {
            foreach (Row row in rows) if (row.Team == team) return row.ShownWins;
            return -1;
        }

        public ScoreBars(LobbyUiKit kit, Transform parent)
        {
            this.kit = kit;
            theme = kit.Theme;
            this.parent = parent;
        }

        public void SetVisible(bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        /// <summary>teams are the match's team ids in order; points and wins are the room's per team id; winsToWin is how many dots a row has.
        /// pointsShown false leaves only the dots (sudden death).</summary>
        public void Refresh(int[] teams, int[] points, int[] wins, int winsToWin, bool pointsShown)
        {
            if (teams == null || teams.Length < 2) { SetVisible(false); return; }
            int dotCount = Mathf.Clamp(winsToWin, 1, 5);
            if (root == null || builtTeamCount != teams.Length || builtDotCount != dotCount || !SameTeams(teams)) Build(teams, dotCount);
            SetVisible(true);

            if (pointsShown != pointsShowing)
            {
                foreach (Row row in rows) { row.Box.SetActive(pointsShown); row.Track.SetActive(pointsShown); }
                pointsShowing = pointsShown;
            }

            DominionScoreBarRules.FillsInto(teams, points, fillBuffer);
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (!Mathf.Approximately(fillBuffer[i], row.ShownFill))
                {
                    row.Fill.anchorMax = new Vector2(fillBuffer[i], 1f);
                    row.ShownFill = fillBuffer[i];
                }
                int shown = DominionScoreBarRules.PointsOf(points, row.Team);
                if (shown != row.ShownPoints)
                {
                    row.Number.text = shown.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    row.ShownPoints = shown;
                }
                int teamWins = DominionScoreBarRules.PointsOf(wins, row.Team);
                if (teamWins != row.ShownWins) { PaintDots(row, teamWins); row.ShownWins = teamWins; }
            }
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            rows.Clear();
        }

        private bool SameTeams(int[] teams)
        {
            if (rows.Count != teams.Length) return false;
            for (int i = 0; i < teams.Length; i++) if (rows[i].Team != teams[i]) return false;
            return true;
        }

        private void Build(int[] teams, int dotCount)
        {
            Destroy();
            builtTeamCount = teams.Length;
            builtDotCount = dotCount;
            pointsShowing = true;
            fillBuffer = new float[teams.Length];

            float rowHeight = theme.dominionScoreBarHeight;
            float dotsWidth = ScoreBarLayout.DotsWidth(dotCount, theme.dominionDotSize, theme.dominionDotGap);
            float width = ScoreBarLayout.RowWidth(theme.dominionScoreBarNumberWidth, theme.dominionScoreBarWidth, theme.dominionScoreDotsGap, dotsWidth);
            float height = ScoreBarLayout.StackHeight(teams.Length, rowHeight, theme.dominionScoreBarGap);

            var go = new GameObject("Score Bars", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            root = (RectTransform)go.transform;
            root.anchorMin = root.anchorMax = root.pivot = Vector2.one; // scales toward the top right, so the gap to the minimap holds at any HUD scale
            root.sizeDelta = new Vector2(width, height);
            root.localScale = Vector3.one * theme.hudScale; // the HUD's size, like the Gold Corner
            Rect minimap = HudScreenLayout.CornerMinimapRect(theme.minimapCornerMargin, theme.minimapFrameWidth, theme.minimapCornerSize);
            Rect block = ScoreBarLayout.Block(minimap, theme.dominionScoreBarsMinimapGap, theme.dominionScoreBarsTop, width * theme.hudScale, height * theme.hudScale);
            root.anchoredPosition = new Vector2(block.xMax, block.yMax);

            for (int i = 0; i < teams.Length; i++)
                rows.Add(BuildRow(teams[i], i * (rowHeight + theme.dominionScoreBarGap), rowHeight, dotCount));
        }

        private Row BuildRow(int team, float top, float height, int dotCount)
        {
            var row = new Row { Team = team };

            // The number's box, then the bar: both dark, so the team's colour reads as the filled part and the number is readable on every team's colour.
            row.Box = new GameObject("Team " + team + " Number Box", typeof(RectTransform), typeof(Image));
            row.Box.transform.SetParent(root, false);
            TopLeft((RectTransform)row.Box.transform, 0f, top, theme.dominionScoreBarNumberWidth, height);
            Image boxImage = row.Box.GetComponent<Image>();
            boxImage.color = theme.dominionScoreBarTrackColor;
            boxImage.raycastTarget = false;

            row.Number = kit.Text(row.Box.transform, "Points", "0", kit.Display, theme.dominionScoreBarNumberSize, Pick(theme.dominionTeamTextColors, team), TextAlignmentOptions.Midline);
            row.Number.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Stretch(row.Number.rectTransform);

            row.Track = new GameObject("Team " + team + " Bar", typeof(RectTransform), typeof(Image));
            row.Track.transform.SetParent(root, false);
            TopLeft((RectTransform)row.Track.transform, theme.dominionScoreBarNumberWidth, top, theme.dominionScoreBarWidth, height);
            Image trackImage = row.Track.GetComponent<Image>();
            trackImage.color = theme.dominionScoreBarTrackColor;
            trackImage.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(row.Track.transform, false);
            row.Fill = (RectTransform)fill.transform;
            row.Fill.anchorMin = Vector2.zero;
            row.Fill.anchorMax = new Vector2(0f, 1f); // Refresh sets the right edge from the fill share
            row.Fill.offsetMin = row.Fill.offsetMax = Vector2.zero;
            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = Pick(theme.dominionTeamColors, team);
            fillImage.raycastTarget = false;

            // The dots are the root's children, not the bar's: they stay up when sudden death hides the bar.
            row.Dots = new Image[dotCount];
            float left = ScoreBarLayout.DotsLeft(theme.dominionScoreBarNumberWidth, theme.dominionScoreBarWidth, theme.dominionScoreDotsGap);
            float dotTop = top + (height - theme.dominionDotSize) * 0.5f;
            for (int i = 0; i < dotCount; i++)
            {
                var dotGo = new GameObject("Team " + team + " Dot " + (i + 1), typeof(RectTransform), typeof(Image));
                dotGo.transform.SetParent(root, false);
                TopLeft((RectTransform)dotGo.transform, left + i * (theme.dominionDotSize + theme.dominionDotGap), dotTop, theme.dominionDotSize, theme.dominionDotSize);
                Image dot = dotGo.GetComponent<Image>();
                dot.raycastTarget = false;
                row.Dots[i] = dot;
            }
            return row;
        }

        private void PaintDots(Row row, int wins)
        {
            for (int i = 0; i < row.Dots.Length; i++)
            {
                bool won = i < wins;
                Image dot = row.Dots[i];
                dot.sprite = won ? GeneratedSprites.Disc : DominionHudSprites.EmptyDot(theme);
                dot.color = won ? Pick(theme.dominionTeamColors, row.Team) : theme.dominionDimColor;
            }
        }

        private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private Color Pick(Color[] colours, int team) => TeamPalette.Of(colours, team, theme.dominionDimColor);
    }
}
