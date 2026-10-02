using System;
using System.Collections.Generic;
using Overpower.Data;
using Overpower.Lobby;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The lobby list (board 1A, lobby Task 9): a table of the open lobbies from the LobbyDirectory, a Create lobby button, a How to play
    /// button and a line that says the list updates by itself. Rows are kept per lobby and updated in place (redrawn when the directory says the
    /// list changed and every LobbyConfig list-redraw interval, so a status or a player count follows the room without a row being rebuilt under
    /// the mouse). Join / Spectate join the lobby by room name (a spectator seat is given by the late-join placement once inside); a full lobby's
    /// button is greyed. Draws and reports; the directory does the joining. Built in code from UiTheme (LobbyUiKit).
    /// </summary>
    public sealed class LobbyListPanel : MonoBehaviour
    {
        /// <summary>What one row shows, as plain text (what checks record).</summary>
        public readonly struct RowSummary
        {
            public readonly string RoomName, Name, Mode, Players, Status, Host, Action;
            public readonly bool ActionEnabled;

            public RowSummary(string roomName, string name, string mode, string players, string status, string host, string action, bool actionEnabled)
            {
                RoomName = roomName; Name = name; Mode = mode; Players = players; Status = status; Host = host; Action = action; ActionEnabled = actionEnabled;
            }

            public override string ToString() => $"{Name} | {Mode} | {Players} | {Status} | {Host} | {Action}{(ActionEnabled ? "" : " (greyed)")}";
        }

        private sealed class RowView
        {
            public GameObject Root;
            public Image Background;
            public TextMeshProUGUI Name, Mode, Players, Status, Host;
            public LobbyButton Action;
            public JoinAction ActionKind;
            public string RoomName;
        }

        private LobbyUiKit kit;
        private UiTheme theme;
        private RoomManager roomManager;
        private LobbyDirectory directory;
        private GameModeCatalogue catalogue;
        private LobbyConfig config;

        private GameObject root;
        private TextMeshProUGUI playerLabel;
        private Transform rowParent;
        private TextMeshProUGUI emptyLabel;
        private TextMeshProUGUI footerLabel;
        private LobbyButton createButton;
        private LobbyButton howToButton;
        private readonly Dictionary<string, RowView> rows = new Dictionary<string, RowView>();
        private readonly List<string> stale = new List<string>();
        private readonly List<RowSummary> summaries = new List<RowSummary>();

        private bool dirty;
        private float nextRedrawAt;
        private float messageUntil;

        /// <summary>The Create lobby button was pressed.</summary>
        public event Action CreateClicked;

        /// <summary>The How to play button was pressed (the panel itself is lobby Task 12).</summary>
        public event Action HowToPlayClicked;

        /// <summary>A Join / Spectate button was pressed for this room.</summary>
        public event Action<string> JoinRequested;

        public bool IsShowing => root != null && root.activeSelf;

        /// <summary>The rows as drawn, top to bottom.</summary>
        public IReadOnlyList<RowSummary> Rows => summaries;

        /// <summary>The line at the bottom right: the hint, or the short message about a lobby that could not be joined.</summary>
        public string FooterText => footerLabel != null ? footerLabel.text : "";

        /// <summary>The line shown in place of the table (empty or connecting); empty when rows are shown.</summary>
        public string EmptyText => emptyLabel != null && emptyLabel.gameObject.activeSelf ? emptyLabel.text : "";

        public static LobbyListPanel Create(Transform canvas, LobbyUiKit kit, RoomManager manager)
        {
            var go = new GameObject("Lobby List", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            LobbyListPanel panel = go.AddComponent<LobbyListPanel>();
            panel.Build(kit, manager);
            return panel;
        }

        public void Show(string playerName)
        {
            if (playerLabel != null) playerLabel.text = playerName ?? "";
            root.SetActive(true);
            dirty = true;
            nextRedrawAt = 0f;
            messageUntil = 0f;
            footerLabel.text = theme.lobbyListHintText;
            footerLabel.color = theme.lobbyDimColor;
            Redraw();
        }

        public void Hide() => root.SetActive(false);

        /// <summary>Presses the row's Join / Spectate button (what a click does). False when there is no such row or its button is greyed.</summary>
        public bool PressAction(string roomName)
        {
            if (!rows.TryGetValue(roomName, out RowView row) || !row.Action.Button.interactable) return false;
            row.Action.Press();
            return true;
        }

        /// <summary>Presses Create lobby.</summary>
        public void PressCreate() => createButton.Press();

        /// <summary>Presses How to play.</summary>
        public void PressHowToPlay() => howToButton.Press();

        /// <summary>A short message in place of the hint (a join that was refused), for the theme's message time.</summary>
        public void ShowMessage(string text)
        {
            footerLabel.text = text;
            footerLabel.color = theme.lobbyErrorColor;
            messageUntil = Time.unscaledTime + theme.lobbyMessageSeconds;
        }

        private void OnDestroy()
        {
            if (directory != null) directory.ListChanged -= MarkDirty;
        }

        private void MarkDirty() => dirty = true;

        private void Update()
        {
            if (!IsShowing) return;
            if (messageUntil > 0f && Time.unscaledTime >= messageUntil)
            {
                messageUntil = 0f;
                footerLabel.text = theme.lobbyListHintText;
                footerLabel.color = theme.lobbyDimColor;
            }
            float interval = config != null ? config.ListRedrawSeconds : LobbyConfig.DefaultListRedrawSeconds;
            if (dirty || Time.unscaledTime >= nextRedrawAt)
            {
                Redraw();
                nextRedrawAt = Time.unscaledTime + interval;
            }
        }

        // ---- drawing the rows ----

        private void Redraw()
        {
            dirty = false;
            summaries.Clear();
            IReadOnlyList<LobbyEntry> entries = directory != null ? directory.Entries : null;
            int count = entries != null ? entries.Count : 0;

            stale.Clear();
            foreach (string key in rows.Keys) stale.Add(key);
            for (int i = 0; i < count; i++)
            {
                LobbyEntry entry = entries[i];
                stale.Remove(entry.RoomName);
                if (!rows.TryGetValue(entry.RoomName, out RowView row))
                {
                    row = BuildRow(entry.RoomName);
                    rows[entry.RoomName] = row;
                }
                Fill(row, entry);
                row.Root.transform.SetSiblingIndex(i);
            }
            foreach (string key in stale)
            {
                Destroy(rows[key].Root);
                rows.Remove(key);
            }

            bool any = count > 0;
            emptyLabel.gameObject.SetActive(!any);
            if (!any)
                emptyLabel.text = PhotonNetwork.InLobby ? theme.lobbyListEmptyText : theme.lobbyListConnectingText;
        }

        private void Fill(RowView row, LobbyEntry entry)
        {
            GameModeDefinition mode = catalogue != null && entry.ModeId >= 0 ? catalogue.ById(entry.ModeId) : null;
            string modeText = mode != null ? mode.DisplayName : theme.lobbyListUnknownModeText;
            JoinAction action = LobbyListCache.ActionOf(entry);
            bool full = action == JoinAction.Full;
            string status = LobbyListRules.StatusText(entry.StageValue);

            LobbyScreenRules.PlayersPartsOf(entry, out string main, out string spectators);
            string playersText = spectators.Length == 0
                ? main
                : $"{main} <size={theme.lobbyListRowSpecSize}><color=#{ColorUtility.ToHtmlStringRGB(theme.lobbyDimColor)}>{spectators}</color></size>";

            Color text = full ? theme.lobbyDimColor : theme.lobbyOffWhiteColor;
            row.Background.color = full ? theme.lobbyRowFullColor : theme.lobbyPanelColor;
            row.Name.text = entry.DisplayName;
            row.Name.color = text;
            row.Mode.text = modeText;
            row.Mode.color = text;
            row.Players.text = playersText;
            row.Players.color = text;
            row.Status.text = status;
            row.Status.color = full ? theme.lobbyDimColor : StatusColour(entry.StageValue);
            row.Host.text = entry.HostName;
            row.Host.color = text;

            string actionText = action == JoinAction.Join ? theme.lobbyListJoinText : action == JoinAction.Spectate ? theme.lobbyListSpectateText : theme.lobbyListFullText;
            row.Action.Label.text = actionText;
            row.Action.SetEnabled(!full);
            row.ActionKind = action;

            summaries.Add(new RowSummary(entry.RoomName, entry.DisplayName, modeText, spectators.Length == 0 ? main : main + " " + spectators, status,
                entry.HostName, actionText, !full));
        }

        private Color StatusColour(LobbyStage stage)
        {
            if (stage == LobbyStage.InMatch) return theme.lobbyYellowColor;
            if (stage == LobbyStage.Warmup) return theme.lobbyMutedColor;
            return theme.lobbyCyanColor;
        }

        // ---- building ----

        private void Build(LobbyUiKit uiKit, RoomManager manager)
        {
            kit = uiKit;
            theme = kit.Theme;
            roomManager = manager;
            directory = manager != null ? manager.Lobbies : null;
            catalogue = manager != null ? manager.ModeCatalogue : null;
            config = manager != null ? manager.LobbyCfg : null;
            if (directory != null) directory.ListChanged += MarkDirty;

            root = gameObject;
            RectTransform rootRect = (RectTransform)transform;
            LobbyUiKit.Stretch(rootRect);
            Image background = root.AddComponent<Image>();
            background.color = theme.lobbyDarkColor;

            VerticalLayoutGroup page = root.AddComponent<VerticalLayoutGroup>();
            page.padding = LobbyUiKit.Pad(theme.lobbyListPadding.x, theme.lobbyListPadding.x, theme.lobbyListPadding.y, theme.lobbyListPadding.y);
            page.spacing = theme.lobbyListGap;
            page.childAlignment = TextAnchor.UpperLeft;
            page.childControlWidth = page.childControlHeight = true;
            page.childForceExpandWidth = true;
            page.childForceExpandHeight = false;

            BuildHeader(page.transform);
            BuildColumnHeads(page.transform);
            BuildBody(page.transform);
            BuildFooter(page.transform);
            root.SetActive(false);
        }

        private void BuildHeader(Transform parent)
        {
            HorizontalLayoutGroup header = LobbyUiKit.HGroup(parent, "Header", theme.lobbyListHeaderGap, TextAnchor.LowerLeft);
            header.childForceExpandWidth = false;

            VerticalLayoutGroup left = LobbyUiKit.VGroup(header.transform, "Title", theme.lobbyListTitleGap, TextAnchor.LowerLeft, null, false);
            kit.Text(left.transform, "Caption", theme.lobbyListKickerText, kit.Bold, theme.lobbyListKickerSize, theme.lobbyCyanColor,
                TextAlignmentOptions.MidlineLeft, theme.lobbyListKickerSpacing);
            kit.Text(left.transform, "Heading", theme.lobbyListTitleText, kit.Display, theme.lobbyListTitleSize, theme.lobbyOffWhiteColor,
                TextAlignmentOptions.MidlineLeft);
            LobbyUiKit.Spacer(header.transform);

            HorizontalLayoutGroup right = LobbyUiKit.HGroup(header.transform, "Player and Create", theme.lobbyListPlayerGap, TextAnchor.MiddleRight);
            HorizontalLayoutGroup who = LobbyUiKit.HGroup(right.transform, "Playing as", theme.lobbyListPlayingAsGap, TextAnchor.MiddleRight);
            kit.Text(who.transform, "Playing as", theme.lobbyListPlayingAsText, kit.Body, theme.lobbyListPlayingAsSize, theme.lobbyMutedColor,
                TextAlignmentOptions.MidlineRight);
            playerLabel = kit.Text(who.transform, "Player", "", kit.Bold, theme.lobbyListPlayingAsSize, theme.lobbyOffWhiteColor,
                TextAlignmentOptions.MidlineLeft);
            createButton = kit.MakeButton(right.transform, "Create lobby", theme.lobbyListCreateText, kit.Display, theme.lobbyListCreateTextSize,
                theme.lobbyOffWhiteColor, theme.lobbyPurpleColor, theme.lobbyCornerRadius, theme.lobbyPurpleColor, 0f, theme.lobbyButtonSpacing);
            createButton.Button.onClick.AddListener(() => CreateClicked?.Invoke());
            LobbyUiKit.Size(createButton.Root, -1f, theme.lobbyListCreateHeight);
            LobbyUiKit.WidenToLabel(createButton, theme.lobbyListCreatePadding);
        }

        private void BuildColumnHeads(Transform parent)
        {
            HorizontalLayoutGroup heads = LobbyUiKit.HGroup(parent, "Column Heads", theme.lobbyListColumnGap, TextAnchor.MiddleLeft,
                LobbyUiKit.Pad(theme.lobbyListRowPadding, theme.lobbyListRowPadding, 0f, 0f));
            string[] names = theme.lobbyListColumnTexts;
            for (int i = 0; i < 5; i++)
            {
                TextMeshProUGUI head = kit.Text(heads.transform, "Head " + i, names != null && i < names.Length ? names[i] : "", kit.Bold,
                    theme.lobbyListHeadSize, theme.lobbyDimColor, TextAlignmentOptions.MidlineLeft, theme.lobbyLabelSpacing);
                SizeColumn(head.gameObject, i);
            }
            var action = new GameObject("Action", typeof(RectTransform));
            action.transform.SetParent(heads.transform, false);
            LobbyUiKit.Size(action, theme.lobbyListActionWidth, 0f, 0f);
        }

        private void SizeColumn(GameObject cell, int index)
        {
            float[] weights = theme.lobbyListColumnWeights;
            float weight = weights != null && index < weights.Length ? weights[index] : 1f;
            LayoutElement element = LobbyUiKit.Size(cell, 0f, -1f, weight);
            element.minWidth = 0f;
        }

        private void BuildBody(Transform parent)
        {
            var viewport = new GameObject("Table", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(parent, false);
            LobbyUiKit.Size(viewport, -1f, 0f, 1f, 1f);
            RectTransform viewportRect = (RectTransform)viewport.transform;

            var content = new GameObject("Rows", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);
            VerticalLayoutGroup rowsGroup = content.AddComponent<VerticalLayoutGroup>();
            rowsGroup.spacing = theme.lobbyListRowGap;
            rowsGroup.childControlWidth = rowsGroup.childControlHeight = true;
            rowsGroup.childForceExpandWidth = true;
            rowsGroup.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            rowParent = content.transform;

            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = theme.lobbyListScrollSensitivity;

            emptyLabel = kit.Text(viewport.transform, "Empty", theme.lobbyListConnectingText, kit.Body, theme.lobbyListRowTextSize, theme.lobbyDimColor,
                TextAlignmentOptions.Top);
            RectTransform emptyRect = (RectTransform)emptyLabel.transform;
            emptyRect.anchorMin = new Vector2(0f, 1f);
            emptyRect.anchorMax = new Vector2(1f, 1f);
            emptyRect.pivot = new Vector2(0.5f, 1f);
            emptyRect.anchoredPosition = new Vector2(0f, -theme.lobbyListRowHeight * 0.5f);
            emptyRect.sizeDelta = new Vector2(0f, theme.lobbyListRowHeight);
        }

        private void BuildFooter(Transform parent)
        {
            HorizontalLayoutGroup footer = LobbyUiKit.HGroup(parent, "Footer", theme.lobbyListFooterGap, TextAnchor.MiddleLeft);
            howToButton = kit.MakeButton(footer.transform, "How to play", theme.lobbyListHowToText, kit.Bold, theme.lobbyListHowToTextSize,
                theme.lobbyDarkTextColor, theme.lobbyCyanColor, theme.lobbyCornerRadius, theme.lobbyCyanColor, 0f);
            howToButton.Button.onClick.AddListener(() =>
            {
                Debug.Log("[LOBBY] How to play pressed (the panel itself is lobby Task 12)");
                HowToPlayClicked?.Invoke();
            });
            LobbyUiKit.Size(howToButton.Root, -1f, theme.lobbyListHowToHeight);
            LobbyUiKit.WidenToLabel(howToButton, theme.lobbyListHowToPadding);
            LobbyUiKit.Spacer(footer.transform);
            footerLabel = kit.Text(footer.transform, "Hint", theme.lobbyListHintText, kit.Body, theme.lobbyListHintSize, theme.lobbyDimColor,
                TextAlignmentOptions.MidlineRight);
        }

        private RowView BuildRow(string roomName)
        {
            var go = new GameObject("Row " + roomName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(rowParent, false);
            var view = new RowView { Root = go, RoomName = roomName };
            view.Background = go.GetComponent<Image>();
            LobbyUiKit.Round(view.Background, theme.lobbyCornerRadius);
            LobbyUiKit.Size(go, -1f, theme.lobbyListRowHeight);

            HorizontalLayoutGroup group = go.AddComponent<HorizontalLayoutGroup>();
            group.spacing = theme.lobbyListColumnGap;
            group.padding = LobbyUiKit.Pad(theme.lobbyListRowPadding, theme.lobbyListRowPadding, 0f, 0f);
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;

            float size = theme.lobbyListRowTextSize;
            view.Name = kit.Text(go.transform, "Name", "", kit.Bold, size, theme.lobbyOffWhiteColor, richText: false); // typed by a player
            view.Mode = kit.Text(go.transform, "Mode", "", kit.Body, size, theme.lobbyOffWhiteColor);
            view.Players = kit.Text(go.transform, "Players", "", kit.Body, size, theme.lobbyOffWhiteColor);
            view.Status = kit.Text(go.transform, "Status", "", kit.Body, size, theme.lobbyCyanColor);
            view.Host = kit.Text(go.transform, "Host", "", kit.Body, size, theme.lobbyOffWhiteColor, richText: false); // typed by a player
            SizeColumn(view.Name.gameObject, 0);
            SizeColumn(view.Mode.gameObject, 1);
            SizeColumn(view.Players.gameObject, 2);
            SizeColumn(view.Status.gameObject, 3);
            SizeColumn(view.Host.gameObject, 4);

            view.Action = kit.MakeButton(go.transform, "Action", "Join", kit.Bold, theme.lobbyListRowButtonTextSize, theme.lobbyDarkTextColor,
                theme.lobbyOffWhiteColor, theme.lobbyCornerRadius, theme.lobbyOffWhiteColor, 0f);
            LobbyUiKit.Size(view.Action.Root, theme.lobbyListActionWidth, theme.lobbyListRowButtonHeight, 0f);
            string captured = roomName;
            view.Action.Button.onClick.AddListener(() => JoinRequested?.Invoke(captured));
            return view;
        }

    }
}
