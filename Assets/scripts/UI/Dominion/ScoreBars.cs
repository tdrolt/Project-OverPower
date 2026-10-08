using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// Dominion Task 17 (Tudor A52), the score bars in the bottom-right corner during a round and its overtime: one bar per team (two in 2v2, three in
    /// 3v3v3) in the team's colour, the leading team's full and the others filled by their share of the leader's points, with the points as a small
    /// number in a box in front of each bar. Spectators see the same bars. The bars stand just above the gold readout over the "Loadout (P)" button
    /// (DominionScoreBarRules.GoldReadoutTop), right-aligned with it, so nothing else in that corner is covered. Sizes, colours and gaps are UiTheme
    /// fields (Dominion HUD: score bars); DominionScoreBarRules decides every fill. A fill or a number is only rewritten when its value moved.
    /// </summary>
    public sealed class ScoreBars
    {
        private sealed class Row
        {
            public int Team;
            public RectTransform Fill;
            public TextMeshProUGUI Number;
            public float ShownFill = -1f;
            public int ShownPoints = int.MinValue;
        }

        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform parent;
        private readonly List<Row> rows = new List<Row>();
        private RectTransform root;
        private int builtTeamCount;

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

        /// <summary>One frame. teams are the match's team ids in order; points are the room's points per team id.</summary>
        public void Refresh(int[] teams, int[] points)
        {
            if (teams == null || teams.Length < 2) { SetVisible(false); return; }
            if (root == null || builtTeamCount != teams.Length || !SameTeams(teams)) Build(teams);
            SetVisible(true);

            float[] fills = DominionScoreBarRules.Fills(teams, points);
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (!Mathf.Approximately(fills[i], row.ShownFill))
                {
                    row.Fill.anchorMax = new Vector2(fills[i], 1f);
                    row.ShownFill = fills[i];
                }
                int shown = DominionScoreBarRules.PointsOf(points, row.Team);
                if (shown != row.ShownPoints)
                {
                    row.Number.text = shown.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    row.ShownPoints = shown;
                }
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

        private void Build(int[] teams)
        {
            Destroy();
            builtTeamCount = teams.Length;

            float rowHeight = theme.dominionScoreBarHeight;
            float total = teams.Length * rowHeight + (teams.Length - 1) * theme.dominionScoreBarGap;
            float width = theme.dominionScoreBarNumberWidth + theme.dominionScoreBarWidth;

            var go = new GameObject("Score Bars", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            root = (RectTransform)go.transform;
            // Bottom-right, right edge level with the Loadout (P) button (its margin, scaled like the rest of that corner), standing on top of the gold readout.
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
            root.sizeDelta = new Vector2(width, total);
            float bottom = DominionScoreBarRules.GoldReadoutTop(theme.loadoutToggleButtonMargin, theme.loadoutToggleButtonHeight, theme.goldShopGap,
                theme.bodyTextSize, theme.hudScale) + theme.dominionScoreBarsAboveGold;
            root.anchoredPosition = new Vector2(-theme.loadoutToggleButtonMargin * theme.hudScale, bottom);

            for (int i = 0; i < teams.Length; i++)
                rows.Add(BuildRow(teams[i], i * (rowHeight + theme.dominionScoreBarGap), rowHeight));
        }

        private Row BuildRow(int team, float top, float height)
        {
            var row = new Row { Team = team };

            // The number's box, then the bar: both dark, so the team's colour reads as the filled part and the number is readable on every team's colour.
            var box = new GameObject("Team " + team + " Number Box", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(root, false);
            TopLeft((RectTransform)box.transform, 0f, top, theme.dominionScoreBarNumberWidth, height);
            Image boxImage = box.GetComponent<Image>();
            boxImage.color = theme.dominionScoreBarTrackColor;
            boxImage.raycastTarget = false;

            row.Number = kit.Text(box.transform, "Points", "0", kit.Display, theme.dominionScoreBarNumberSize, Pick(theme.dominionTeamTextColors, team), TextAlignmentOptions.Midline);
            row.Number.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Stretch(row.Number.rectTransform);

            var track = new GameObject("Team " + team + " Bar", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(root, false);
            TopLeft((RectTransform)track.transform, theme.dominionScoreBarNumberWidth, top, theme.dominionScoreBarWidth, height);
            Image trackImage = track.GetComponent<Image>();
            trackImage.color = theme.dominionScoreBarTrackColor;
            trackImage.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            row.Fill = (RectTransform)fill.transform;
            row.Fill.anchorMin = Vector2.zero;
            row.Fill.anchorMax = new Vector2(0f, 1f); // Refresh sets the right edge from the fill share
            row.Fill.offsetMin = row.Fill.offsetMax = Vector2.zero;
            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = Pick(theme.dominionTeamColors, team);
            fillImage.raycastTarget = false;
            return row;
        }

        private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static Color Pick(Color[] colours, int index) =>
            colours != null && colours.Length > 0 ? colours[Mathf.Clamp(index, 0, colours.Length - 1)] : Color.white;
    }
}
