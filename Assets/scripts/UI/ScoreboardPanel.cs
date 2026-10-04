using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;
using Overpower.Net;

namespace Overpower.UI
{
    /// <summary>
    /// Tudor's D12: hold Tab and a scoreboard shows, grouped by team (each team's name in its own colour), one line
    /// per player: name, kills, deaths, assists, damage dealt, zones captured. Let go and it closes.
    ///
    /// READS, NEVER WRITES: every player's numbers are their own "sb" Player Property (ScoreboardPublisher writes it;
    /// ScoreboardRules documents the layout), plus the team and nickname already on the room's players. Nothing is
    /// read while the board is closed; while open it re-reads a few times a second (UiTheme.scoreboardRefreshesPerSecond)
    /// and only rewrites a text whose number actually changed, so a steady board allocates nothing.
    ///
    /// Screen-space overlay canvas with no GraphicRaycaster - nothing on it is clickable, so it never swallows a
    /// click meant for the game. Built in code from UiTheme like the HUD (sizes, colours, texts all live there).
    /// Owner only: PlayerHud builds it, and only for the local player.
    /// </summary>
    public sealed class ScoreboardPanel : MonoBehaviour
    {
        private const int ColumnCount = 6;

        private sealed class Line
        {
            public GameObject root;
            public Image selfStrip;
            public TextMeshProUGUI[] cells = new TextMeshProUGUI[ColumnCount];
            // What the line shows now, so a refresh writes only what changed.
            public bool isHeader;
            public int actor = int.MinValue;
            public int team = int.MinValue;
            public string name;
            public int kills = int.MinValue, deaths = int.MinValue, assists = int.MinValue, damage = int.MinValue, captures = int.MinValue;
            public bool isSelf;
        }

        private UiTheme theme;
        private PlayerInputRouter router;
        private GameObject canvasGo;
        private RectTransform panelRect;
        private Material textMaterial;
        private readonly List<Line> lines = new List<Line>();
        private readonly List<ScoreRow> rows = new List<ScoreRow>();
        private bool open;
        private float sinceRefresh;
        private int lastActiveLines = -1;

        public bool IsOpen => open;

        public static ScoreboardPanel Create(Transform parent, UiTheme theme, PlayerInputRouter router)
        {
            var go = new GameObject("Scoreboard Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ScoreboardPanel panel = go.AddComponent<ScoreboardPanel>();
            panel.theme = theme;
            panel.router = router;
            panel.Build();
            if (router != null)
            {
                router.ScoreboardPressed += panel.HandlePressed;
                router.ScoreboardReleased += panel.HandleReleased;
            }
            else
                Debug.LogError("[Scoreboard] No PlayerInputRouter on this player - holding Tab will not show the scoreboard.");
            return panel;
        }

        private void OnDestroy()
        {
            if (router != null)
            {
                router.ScoreboardPressed -= HandlePressed;
                router.ScoreboardReleased -= HandleReleased;
            }
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        private void OnDisable()
        {
            open = false;
            if (canvasGo != null)
                canvasGo.SetActive(false);
        }

        /// <summary>Tab went down (the router already refused it while typing in chat).</summary>
        public void HandlePressed()
        {
            if (canvasGo == null)
                return;

            open = true;
            canvasGo.SetActive(true);
            Refresh();
            sinceRefresh = 0f;
        }

        /// <summary>Tab came up. Always honoured.</summary>
        public void HandleReleased()
        {
            open = false;
            if (canvasGo != null)
                canvasGo.SetActive(false);
        }

        private void Update()
        {
            if (!open)
                return;

            sinceRefresh += Time.unscaledDeltaTime;
            if (sinceRefresh < 1f / Mathf.Max(1f, theme.scoreboardRefreshesPerSecond))
                return;

            sinceRefresh = 0f;
            Refresh();
        }

        // ---------------------------------------------------------------- reading the room

        /// <summary>Re-reads everyone from the room and rewrites only the texts that changed.</summary>
        public void Refresh()
        {
            rows.Clear();
            // The room's own dictionary: its value enumerator is a struct, unlike PhotonNetwork.PlayerList, which
            // builds a sorted array on every call. Sort below already breaks every tie by actor number.
            Room room = PhotonNetwork.CurrentRoom;
            if (room != null)
            {
                foreach (Player p in room.Players.Values)
                {
                // A seat spectator is no row (lobby Task 6). A player whose team has not arrived yet still shows, under "Joining".
                if (Teams.IsSpectator(p))
                    continue;
                int team = Teams.TryGetTeam(p, out int known) ? known : PlayerTeam.NoTeam;

                int[] values = p.CustomProperties.TryGetValue(ScoreboardRules.Key, out object rawStats) ? rawStats as int[] : null;
                rows.Add(ScoreboardRules.RowFrom(p.ActorNumber, team, p.NickName, values));
                }
            }
            ScoreboardRules.Sort(rows);

            int used = 0;
            int currentTeam = int.MinValue;
            int localActor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            for (int i = 0; i < rows.Count; i++)
            {
                ScoreRow row = rows[i];
                if (row.Team != currentTeam)
                {
                    currentTeam = row.Team;
                    ShowTeamHeader(LineAt(used++), currentTeam);
                }
                ShowPlayer(LineAt(used++), row, row.Actor == localActor);
            }

            for (int i = used; i < lines.Count; i++)
            {
                if (lines[i].root.activeSelf)
                    lines[i].root.SetActive(false);
            }

            // A different number of lines changes the panel's height; rebuild now rather than a frame later so a
            // capture taken right after opening is already the right size.
            if (used != lastActiveLines)
            {
                lastActiveLines = used;
                LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            }
        }

        private Line LineAt(int index)
        {
            while (lines.Count <= index)
                lines.Add(BuildLine(panelRect));
            return lines[index];
        }

        private void ShowTeamHeader(Line line, int team)
        {
            if (!line.root.activeSelf)
                line.root.SetActive(true);

            if (line.isHeader && line.team == team)
                return;

            line.isHeader = true;
            line.team = team;
            line.actor = int.MinValue;
            line.name = null;
            line.kills = line.deaths = line.assists = line.damage = line.captures = int.MinValue;
            line.isSelf = false;
            line.selfStrip.enabled = false;

            string[] names = theme.scoreboardTeamNames;
            line.cells[0].text = names != null && team >= 0 && team < names.Length ? names[team] : theme.scoreboardUnknownTeamText;
            line.cells[0].color = theme.ShotColorFor(team);
            line.cells[0].fontSize = theme.scoreboardHeadingTextSize;
            line.cells[0].fontStyle = FontStyles.Bold;
            for (int c = 1; c < ColumnCount; c++)
                line.cells[c].text = string.Empty;
        }

        private void ShowPlayer(Line line, ScoreRow row, bool isSelf)
        {
            if (!line.root.activeSelf)
                line.root.SetActive(true);

            if (line.isHeader)
            {
                // This pooled line was a team title; hand it back to player styling.
                line.isHeader = false;
                line.cells[0].fontSize = theme.scoreboardTextSize;
                line.cells[0].fontStyle = FontStyles.Normal;
                line.actor = int.MinValue;
            }

            if (line.isSelf != isSelf)
            {
                line.isSelf = isSelf;
                line.selfStrip.enabled = isSelf;
            }

            if (line.actor != row.Actor || line.team != row.Team || line.name != row.Name)
            {
                line.actor = row.Actor;
                line.team = row.Team;
                line.name = row.Name;
                line.cells[0].text = string.IsNullOrEmpty(row.Name) ? "Player " + row.Actor : row.Name;
                line.cells[0].color = theme.textColor;
            }
            SetNumber(line.cells[1], ref line.kills, row.Kills);
            SetNumber(line.cells[2], ref line.deaths, row.Deaths);
            SetNumber(line.cells[3], ref line.assists, row.Assists);
            SetNumber(line.cells[4], ref line.damage, row.Damage);
            SetNumber(line.cells[5], ref line.captures, row.Captures);
        }

        private static void SetNumber(TextMeshProUGUI cell, ref int shown, int value)
        {
            if (shown == value)
                return;

            shown = value;
            cell.text = value.ToString();
        }

        // ---------------------------------------------------------------- building the canvas

        private void Build()
        {
            canvasGo = new GameObject("Scoreboard Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = theme.scoreboardSortingOrder;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            // No GraphicRaycaster: nothing here is clickable.

            GameObject panel = new GameObject("Scoreboard", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(theme.scoreboardWidth, 0f);

            Image background = panel.AddComponent<Image>();
            background.color = theme.scoreboardPanelColor;
            background.raycastTarget = false;

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(theme.scoreboardPaddingHorizontal, theme.scoreboardPaddingHorizontal,
                theme.scoreboardPaddingVertical, theme.scoreboardPaddingVertical);
            layout.spacing = theme.scoreboardLineSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Title, then the column headings. Both are plain lines built once and never touched again.
            Line title = BuildLine(panelRect);
            title.cells[0].text = theme.scoreboardTitleText;
            title.cells[0].fontSize = theme.scoreboardHeadingTextSize + theme.scoreboardTitleExtraSize;
            title.cells[0].fontStyle = FontStyles.Bold;

            Line headings = BuildLine(panelRect);
            string[] columnTexts = theme.scoreboardColumnTexts;
            for (int c = 0; c < ColumnCount; c++)
            {
                headings.cells[c].text = columnTexts != null && c < columnTexts.Length ? columnTexts[c] : string.Empty;
                headings.cells[c].fontSize = theme.scoreboardHeadingTextSize;
                headings.cells[c].fontStyle = FontStyles.Bold;
            }
            lines.Clear(); // The two fixed lines above are not part of the player pool.

            canvasGo.SetActive(false);
        }

        private Line BuildLine(Transform parent)
        {
            var line = new Line();
            line.root = new GameObject("Line", typeof(RectTransform));
            line.root.transform.SetParent(parent, false);
            LayoutElement le = line.root.AddComponent<LayoutElement>();
            le.preferredHeight = theme.scoreboardRowHeight;
            le.minHeight = theme.scoreboardRowHeight;

            GameObject stripGo = new GameObject("Self Strip", typeof(RectTransform));
            stripGo.transform.SetParent(line.root.transform, false);
            RectTransform stripRt = stripGo.GetComponent<RectTransform>();
            stripRt.anchorMin = Vector2.zero;
            stripRt.anchorMax = Vector2.one;
            stripRt.offsetMin = stripRt.offsetMax = Vector2.zero;
            line.selfStrip = stripGo.AddComponent<Image>();
            line.selfStrip.color = theme.scoreboardSelfRowColor;
            line.selfStrip.raycastTarget = false;
            line.selfStrip.enabled = false;

            for (int c = 0; c < ColumnCount; c++)
            {
                TextMeshProUGUI cell = AddCell(line.root.transform, c == 0 ? TextAlignmentOptions.Left : TextAlignmentOptions.Center);
                RectTransform rt = cell.rectTransform;
                rt.anchorMin = new Vector2(ColumnEdge(c), 0f);
                rt.anchorMax = new Vector2(ColumnEdge(c + 1), 1f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                line.cells[c] = cell;
            }
            return line;
        }

        /// <summary>Column c starts at edge c and ends at edge c+1 (UiTheme.scoreboardColumnEdges); a theme with
        /// too few edges falls back to an even split so the board still draws.</summary>
        private float ColumnEdge(int index)
        {
            float[] edges = theme.scoreboardColumnEdges;
            if (edges != null && edges.Length > ColumnCount)
                return edges[index];
            return index / (float)ColumnCount;
        }

        /// <summary>Same recipe as PlayerHud.AddLabel: one shared outlined material for every text on this board.</summary>
        private TextMeshProUGUI AddCell(Transform parent, TextAlignmentOptions alignment)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            if (theme.font != null)
                tmp.font = theme.font;
            tmp.text = string.Empty;
            tmp.fontSize = theme.scoreboardTextSize;
            tmp.color = theme.textColor;
            tmp.alignment = alignment;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;

            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(textMaterial);
            }
            tmp.fontSharedMaterial = textMaterial;
            return tmp;
        }
    }
}
