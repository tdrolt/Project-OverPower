using System;
using System.Collections.Generic;
using System.Text;
using Overpower.Data;
using Overpower.Lobby;
using Overpower.Match;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The lobby room (board 3A, lobby Task 10): the lobby's name, the mode as a cyan button that opens the game mode info, the host, one column per
    /// team of the mode with a button per seat (an empty seat is dashed, a taken one shows the name, yours is outlined, the host is marked), a row of
    /// spectator seats, the No role list with Leave my seat, and along the bottom Leave lobby, How to play and (host only) Start game. It draws what
    /// LobbySeats holds and sends what the player presses (TryTake, LeaveSeat, StartGame): it never decides who sits where. It is shown while this
    /// client is in a room whose stage (lS) is still the lobby, and hides itself the moment the stage moves on (everyone spawns or spectates).
    /// Built in code from UiTheme (LobbyUiKit); the seat buttons are made once per lobby, from the room's GameModeDefinition, and updated in place.
    /// </summary>
    public sealed class LobbyRoomPanel : MonoBehaviour
    {
        /// <summary>What a seat button shows.</summary>
        public enum SeatState { Empty, Taken, Mine }

        private sealed class SeatView
        {
            public string Key;
            public bool Spectator;
            public LobbyButton Button;
            public Image Dash;
            public TextMeshProUGUI Label;
            public SeatState State = (SeatState)(-1);
            public string Text;
            public float TextSize;
            public Color Border;
            public bool Bordered;
        }

        private const float RedrawSeconds = 0.25f;

        /// <summary>How tall a line of the display font (Oswald) is, in text sizes. A text box shorter than that drops its line altogether (the
        /// ellipsis overflow cuts what does not fit), so every heading box is made this tall.</summary>
        private const float DisplayLine = 1.5f;

        private LobbyUiKit kit;
        private UiTheme theme;
        private RoomManager roomManager;

        private GameObject root;
        private RectTransform content;
        private GameObject built;                       // the screen's parts that depend on the room's mode
        private GameModeDefinition builtFor;
        private TextMeshProUGUI titleLabel, hostLabel, noRoleHeading, noRoleNote, startHint;
        private LobbyButton modeButton, leaveSeatButton, leaveLobbyButton, howToButton, startButton;
        private Transform noRoleList;
        private readonly List<TextMeshProUGUI> noRoleNames = new List<TextMeshProUGUI>();
        private readonly Dictionary<string, SeatView> seatViews = new Dictionary<string, SeatView>();
        private readonly List<string> seatOrder = new List<string>();
        private readonly StringBuilder scratch = new StringBuilder();

        private bool wanted;
        private bool dirty;
        private float nextRedrawAt;
        private LobbySeats subscribedSeats;

        /// <summary>The mode button was pressed (the game mode info page opens; the NameScreen shows it).</summary>
        public event Action<GameModeDefinition> ModeInfoRequested;

        /// <summary>How to play was pressed.</summary>
        public event Action HowToPlayRequested;

        /// <summary>Whether the screen is up on the screen: asked for by the NameScreen, in a room, its mode read and the lobby stage still 0.</summary>
        public bool IsShowing => root != null && root.activeSelf;

        public string TitleText => titleLabel != null ? titleLabel.text : "";
        public string ModeButtonText => modeButton != null ? modeButton.Label.text : "";
        public string HostLineText => hostLabel != null ? hostLabel.text : "";
        public LobbyButton ModeButton => modeButton;
        public LobbyButton LeaveSeatButton => leaveSeatButton;
        public LobbyButton LeaveLobbyButton => leaveLobbyButton;
        public LobbyButton HowToPlayButton => howToButton;
        public LobbyButton StartButton => startButton;

        /// <summary>Start game as drawn: shown to the master only; greyed when a team would stay empty.</summary>
        public bool StartVisible => IsShowing && startButton != null && startButton.Root.activeSelf;
        public bool StartInteractable => StartVisible && startButton.Button.interactable;

        /// <summary>The line beside Start game (what it does, or why it is greyed), or for everyone else "Waiting for ... to start the game".</summary>
        public string StartLineText => startHint != null ? startHint.text : "";

        /// <summary>The seat keys in the order they are drawn (team seats column by column, then the spectator seats).</summary>
        public IReadOnlyList<string> SeatKeys => seatOrder;

        /// <summary>The text of one seat button as drawn ("Empty seat" or the player's name); null for a key with no button.</summary>
        public string SeatLabel(string seatKey) => seatViews.TryGetValue(seatKey, out SeatView view) ? view.Label.text : null;

        /// <summary>What one seat button shows (Empty / Taken / Mine); null for a key with no button.</summary>
        public SeatState? SeatStateOf(string seatKey) => seatViews.TryGetValue(seatKey, out SeatView view) ? view.State : (SeatState?)null;

        /// <summary>The names in the No role box as drawn, top to bottom.</summary>
        public IReadOnlyList<string> NoRoleNames
        {
            get
            {
                var names = new List<string>();
                foreach (TextMeshProUGUI label in noRoleNames)
                    if (label.gameObject.activeSelf) names.Add(label.text);
                return names;
            }
        }

        /// <summary>The No role heading as drawn ("No role (2)").</summary>
        public string NoRoleHeadingText => noRoleHeading != null ? noRoleHeading.text : "";

        public static LobbyRoomPanel Create(Transform canvas, LobbyUiKit kit, RoomManager manager)
        {
            var go = new GameObject("Lobby Room", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            LobbyUiKit.Stretch((RectTransform)go.transform);
            LobbyRoomPanel panel = go.AddComponent<LobbyRoomPanel>();
            panel.kit = kit;
            panel.theme = kit.Theme;
            panel.roomManager = manager;
            panel.BuildFrame();
            return panel;
        }

        /// <summary>Asks for the screen (the NameScreen does this on joining a room). It appears once the room's mode is read and the stage is the lobby.</summary>
        public void Show()
        {
            wanted = true;
            dirty = true;
            nextRedrawAt = 0f;
        }

        public void Hide()
        {
            wanted = false;
            if (root != null) root.SetActive(false);
        }

        /// <summary>Takes a seat through its own button (what a click does). False when there is no such seat button.</summary>
        public bool PressSeat(string seatKey)
        {
            if (!seatViews.TryGetValue(seatKey, out SeatView view) || !view.Button.Button.interactable) return false;
            view.Button.Press();
            return true;
        }

        private void OnDestroy()
        {
            if (subscribedSeats != null) subscribedSeats.SeatsChanged -= MarkDirty;
        }

        private void MarkDirty() => dirty = true;

        // ---- the frame (built once) ----

        private void BuildFrame()
        {
            Image backdrop = kit.Backdrop(transform, "Lobby Room Screen", theme.lobbyDarkColor);
            root = backdrop.gameObject;
            content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
            content.SetParent(root.transform, false);
            LobbyUiKit.Stretch(content);
            root.SetActive(false);
        }

        // ---- the parts that depend on the room's mode (built when the mode is first seen) ----

        private void BuildFor(GameModeDefinition mode)
        {
            if (built != null) Destroy(built);
            seatViews.Clear();
            seatOrder.Clear();
            noRoleNames.Clear();
            builtFor = mode;

            VerticalLayoutGroup column = LobbyUiKit.VGroup(content, "Layout", theme.lobbyRoomGap, TextAnchor.UpperLeft,
                LobbyUiKit.Pad(theme.lobbyRoomPadding.x, theme.lobbyRoomPadding.x, theme.lobbyRoomPadding.y, theme.lobbyRoomPadding.y));
            built = column.gameObject;
            LobbyUiKit.Stretch((RectTransform)column.transform);

            BuildHeader(column.transform);
            BuildBody(column.transform, mode);
            BuildFooter(column.transform);
        }

        private void BuildHeader(Transform parent)
        {
            VerticalLayoutGroup header = LobbyUiKit.VGroup(parent, "Header", 3f);
            titleLabel = kit.Text(header.transform, "Lobby name", "", kit.Display, theme.lobbyRoomTitleSize, theme.lobbyOffWhiteColor, TextAlignmentOptions.MidlineLeft);
            LobbyUiKit.Size(titleLabel.gameObject, -1f, theme.lobbyRoomTitleSize * DisplayLine);

            HorizontalLayoutGroup row = LobbyUiKit.HGroup(header.transform, "Mode row", 15f, TextAnchor.MiddleLeft);
            modeButton = kit.MakeButton(row.transform, "Mode", "", kit.Bold, theme.lobbyRoomModeTextSize, theme.lobbyCyanColor, theme.lobbyDarkColor,
                theme.lobbyCornerRadius, theme.lobbyCyanColor, theme.lobbyRoomMineBorderWidth);
            LobbyUiKit.Size(modeButton.Root, -1f, theme.lobbyRoomModeHeight);
            modeButton.Button.onClick.AddListener(() => ModeInfoRequested?.Invoke(builtFor));
            BuildInfoIcon(modeButton);
            hostLabel = kit.Text(row.transform, "Host", "", kit.Body, theme.lobbyRoomHostLineSize, theme.lobbyMutedColor, TextAlignmentOptions.MidlineLeft);
            LobbyUiKit.Size(hostLabel.gameObject, -1f, theme.lobbyRoomModeHeight, 1f);
        }

        /// <summary>The circled i at the right end of the mode button. The fonts have no circled-i letter, so it is drawn: a round outline with an i in it.</summary>
        private void BuildInfoIcon(LobbyButton button)
        {
            float size = theme.lobbyRoomInfoIconSize;
            LobbyBox icon = kit.Box(button.Fill.transform, "Info icon", theme.lobbyDarkColor, size * 0.5f, theme.lobbyCyanColor, theme.lobbyBorderWidth);
            icon.Outer.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            icon.Outer.anchorMin = icon.Outer.anchorMax = icon.Outer.pivot = new Vector2(1f, 0.5f);
            icon.Outer.sizeDelta = new Vector2(size, size);
            icon.Outer.anchoredPosition = new Vector2(-theme.lobbyRoomModePadding, 0f);
            icon.Outer.GetComponent<Image>().raycastTarget = false;
            if (icon.Fill != null) icon.Fill.raycastTarget = false;
            TextMeshProUGUI i = kit.Text(icon.Inner, "i", theme.lobbyRoomInfoIconText, kit.Bold, size * 0.62f, theme.lobbyCyanColor, TextAlignmentOptions.Midline);
            LobbyUiKit.Stretch((RectTransform)i.transform);

            button.Label.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform labelRect = (RectTransform)button.Label.transform;
            labelRect.offsetMin = new Vector2(theme.lobbyRoomModePadding, 0f);
            labelRect.offsetMax = new Vector2(-(theme.lobbyRoomModePadding + size + theme.lobbyRoomModePadding * 0.4f), 0f);
        }

        /// <summary>Widens the mode button to its text, the icon and the padding (the text arrives after the button is built).</summary>
        private void FitModeButton()
        {
            float text = modeButton.Label.GetPreferredValues(modeButton.Label.text).x;
            float width = theme.lobbyRoomModePadding + text + theme.lobbyRoomModePadding * 0.4f + theme.lobbyRoomInfoIconSize + theme.lobbyRoomModePadding;
            LobbyUiKit.Size(modeButton.Root, width + theme.lobbyRoomMineBorderWidth * 2f + 4f); // the outline takes its width off both sides of the label's room
        }

        private void BuildBody(Transform parent, GameModeDefinition mode)
        {
            HorizontalLayoutGroup body = LobbyUiKit.HGroup(parent, "Body", theme.lobbyRoomGap, TextAnchor.UpperLeft, null, true);
            LobbyUiKit.Size(body.gameObject, -1f, 0f, 1f, 1f);

            VerticalLayoutGroup left = LobbyUiKit.VGroup(body.transform, "Seats", theme.lobbyRoomTeamGap, TextAnchor.UpperLeft);
            left.childForceExpandHeight = false;
            LobbyUiKit.Size(left.gameObject, 0f, -1f, 1f, 1f);

            HorizontalLayoutGroup teams = LobbyUiKit.HGroup(left.transform, "Teams", theme.lobbyRoomTeamGap, TextAnchor.UpperLeft, null, true);
            teams.childForceExpandWidth = true;
            LobbyUiKit.Size(teams.gameObject, -1f, -1f, -1f, 0f); // the row is as tall as its tallest column, not stretched to fill the screen
            foreach (int team in mode.Teams)
                BuildTeamColumn(teams.transform, mode, team);

            if (mode.SpectatorSeats > 0)
                BuildSpectators(left.transform, mode);

            BuildNoRole(body.transform);
        }

        private void BuildTeamColumn(Transform parent, GameModeDefinition mode, int team)
        {
            LobbyBox box = kit.Box(parent, "Team " + team, theme.lobbyPanelColor, theme.lobbyRoomBoxRadius, Color.clear, 0f);
            LobbyUiKit.Size(box.Outer.gameObject, 0f, -1f, 1f);
            VerticalLayoutGroup group = box.Inner.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = theme.lobbyRoomSeatGap;
            group.padding = LobbyUiKit.Pad(theme.lobbyRoomBoxPadding, theme.lobbyRoomBoxPadding, theme.lobbyRoomBoxPadding + theme.lobbyRoomStripeHeight, theme.lobbyRoomBoxPadding);
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            BuildStripe(box.Outer, TeamColour(team));

            TextMeshProUGUI name = kit.Text(box.Inner, "Name", LobbyRoomRules.TeamName(theme.scoreboardTeamNames, team), kit.Display, theme.lobbyRoomTeamNameSize,
                theme.lobbyOffWhiteColor, TextAlignmentOptions.MidlineLeft);
            LobbyUiKit.Size(name.gameObject, -1f, theme.lobbyRoomTeamNameSize * DisplayLine);
            for (int i = 0; i < mode.SeatsPerTeam; i++)
                BuildSeat(box.Inner, LobbySeatRules.SeatKey(team, i), false, theme.lobbyRoomSeatHeight, theme.lobbyRoomSeatTextSize);
        }

        /// <summary>The coloured edge across the top of a team column: the top of a rounded box in the team's colour, cut off after the stripe's height
        /// (a mask over the top strip), so it follows the column's rounded corners.</summary>
        private void BuildStripe(RectTransform column, Color colour)
        {
            var mask = new GameObject("Stripe", typeof(RectTransform), typeof(RectMask2D));
            mask.transform.SetParent(column, false);
            mask.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform maskRect = (RectTransform)mask.transform;
            maskRect.anchorMin = new Vector2(0f, 1f);
            maskRect.anchorMax = new Vector2(1f, 1f);
            maskRect.pivot = new Vector2(0.5f, 1f);
            maskRect.sizeDelta = new Vector2(0f, theme.lobbyRoomStripeHeight);
            maskRect.anchoredPosition = Vector2.zero;

            var fill = new GameObject("Colour", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(mask.transform, false);
            RectTransform fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 1f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.pivot = new Vector2(0.5f, 1f);
            fillRect.sizeDelta = new Vector2(0f, theme.lobbyRoomBoxRadius * 3f);
            fillRect.anchoredPosition = Vector2.zero;
            Image image = fill.GetComponent<Image>();
            LobbyUiKit.Round(image, theme.lobbyRoomBoxRadius);
            image.color = colour;
            image.raycastTarget = false;
        }

        private Color TeamColour(int team)
        {
            Color[] colours = theme.lobbyRoomTeamColors;
            return colours != null && team >= 0 && team < colours.Length ? colours[team] : theme.lobbyMutedColor;
        }

        private void BuildSpectators(Transform parent, GameModeDefinition mode)
        {
            LobbyBox box = kit.Box(parent, "Spectators", theme.lobbyPanelColor, theme.lobbyRoomBoxRadius, Color.clear, 0f);
            LobbyUiKit.Size(box.Outer.gameObject, -1f, -1f, -1f, 0f);
            VerticalLayoutGroup group = box.Inner.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = theme.lobbyRoomSeatGap;
            group.padding = LobbyUiKit.Pad(theme.lobbyRoomBoxPadding, theme.lobbyRoomBoxPadding, theme.lobbyRoomBoxPadding, theme.lobbyRoomBoxPadding);
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            TextMeshProUGUI heading = kit.Text(box.Inner, "Heading", theme.lobbyRoomSpectatorsText, kit.Display, theme.lobbyRoomSpectatorTitleSize,
                theme.lobbyRoomHeadingColor, TextAlignmentOptions.MidlineLeft);
            LobbyUiKit.Size(heading.gameObject, -1f, theme.lobbyRoomSpectatorTitleSize * DisplayLine);

            HorizontalLayoutGroup row = LobbyUiKit.HGroup(box.Inner, "Seats", theme.lobbyRoomSpectatorGap, TextAnchor.UpperLeft, null, true);
            row.childForceExpandWidth = true;
            for (int i = 0; i < mode.SpectatorSeats; i++)
                BuildSeat(row.transform, LobbySeatRules.SpectatorSeatKey(i), true, theme.lobbyRoomSpectatorSeatHeight, theme.lobbyRoomSpectatorTextSize);
        }

        /// <summary>One seat button. It has two looks in one object: a filled box with an outline (taken, or yours) and a dashed outline (empty).</summary>
        private void BuildSeat(Transform parent, string key, bool spectator, float height, float textSize)
        {
            LobbyButton button = kit.MakeButton(parent, "Seat " + key, theme.lobbyRoomEmptySeatText, kit.Body, textSize, theme.lobbyDimColor, theme.lobbyRoomSeatFill,
                theme.lobbyCornerRadius, theme.lobbyRoomSeatFill, 0.05f); // a hair of outline only so the box has an outline layer to recolour
            LobbyUiKit.Size(button.Root, spectator ? 0f : -1f, height, spectator ? 1f : -1f);
            button.Button.onClick.AddListener(() => roomManager.Seats.TryTake(key));

            var dash = new GameObject("Dashed", typeof(RectTransform), typeof(Image));
            dash.transform.SetParent(button.Root.transform, false);
            dash.transform.SetSiblingIndex(0);
            LobbyUiKit.Stretch((RectTransform)dash.transform);
            Image dashImage = dash.GetComponent<Image>();
            LobbyUiKit.Dash(dashImage, theme.lobbyCornerRadius);
            dashImage.color = theme.lobbyBorderColor;
            dashImage.raycastTarget = false;

            // The label sits in the box's padding, left aligned.
            button.Label.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform labelRect = (RectTransform)button.Label.transform;
            labelRect.offsetMin = new Vector2(theme.lobbyRoomSeatPadding, 0f);
            labelRect.offsetMax = new Vector2(-theme.lobbyRoomSeatPadding, 0f);

            var view = new SeatView { Key = key, Spectator = spectator, Button = button, Dash = dashImage, Label = (TextMeshProUGUI)button.Label, TextSize = textSize };
            seatViews[key] = view;
            seatOrder.Add(key);
        }

        private void BuildNoRole(Transform parent)
        {
            LobbyBox box = kit.Box(parent, "No role", theme.lobbyRoomSideFill, theme.lobbyRoomBoxRadius, theme.lobbyRoomSideBorder, theme.lobbyBorderWidth);
            LobbyUiKit.Size(box.Outer.gameObject, theme.lobbyRoomSideWidth, -1f);
            VerticalLayoutGroup group = box.Inner.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = theme.lobbyRoomSeatGap;
            group.padding = LobbyUiKit.Pad(theme.lobbyRoomBoxPadding, theme.lobbyRoomBoxPadding, theme.lobbyRoomBoxPadding, theme.lobbyRoomBoxPadding);
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            noRoleHeading = kit.Text(box.Inner, "Heading", "", kit.Display, theme.lobbyRoomSideTitleSize, theme.lobbyOffWhiteColor, TextAlignmentOptions.MidlineLeft);
            LobbyUiKit.Size(noRoleHeading.gameObject, -1f, theme.lobbyRoomSideTitleSize * DisplayLine);

            VerticalLayoutGroup names = LobbyUiKit.VGroup(box.Inner, "Names", theme.lobbyRoomSeatGap);
            noRoleList = names.transform;

            LobbyUiKit.Spacer(box.Inner);
            LobbyUiKit.Size(box.Inner.GetChild(box.Inner.childCount - 1).gameObject, 0f, 0f, 0f, 1f);

            noRoleNote = kit.Text(box.Inner, "Note", theme.lobbyRoomNoRoleNote, kit.Body, theme.lobbyRoomSideNoteSize, theme.lobbyDimColor, TextAlignmentOptions.TopLeft, 0f, true);

            leaveSeatButton = kit.MakeButton(box.Inner, "Leave my seat", theme.lobbyRoomLeaveSeatText, kit.Body, theme.lobbyRoomSideButtonTextSize, theme.lobbyOffWhiteColor,
                theme.lobbyRoomSideFill, theme.lobbyCornerRadius, theme.lobbyBorderColor, theme.lobbyBorderWidth);
            LobbyUiKit.Size(leaveSeatButton.Root, -1f, theme.lobbyRoomSideButtonHeight);
            leaveSeatButton.Button.onClick.AddListener(() => roomManager.Seats.LeaveSeat());
        }

        private void BuildFooter(Transform parent)
        {
            HorizontalLayoutGroup footer = LobbyUiKit.HGroup(parent, "Footer", theme.lobbyRoomBottomGap, TextAnchor.MiddleLeft);
            footer.gameObject.AddComponent<LayoutElement>().minHeight = theme.lobbyRoomStartHeight; // as tall for everyone as for the host, so the screen does not shift

            leaveLobbyButton = kit.MakeButton(footer.transform, "Leave lobby", theme.lobbyRoomLeaveLobbyText, kit.Body, theme.lobbyRoomBottomTextSize, theme.lobbyOffWhiteColor,
                theme.lobbyDarkColor, theme.lobbyCornerRadius, theme.lobbyBorderColor, theme.lobbyBorderWidth);
            LobbyUiKit.Size(leaveLobbyButton.Root, -1f, theme.lobbyRoomBottomHeight);
            LobbyUiKit.WidenToLabel(leaveLobbyButton, theme.lobbyRoomBottomPadding);
            leaveLobbyButton.Button.onClick.AddListener(OnLeaveLobby);

            howToButton = kit.MakeButton(footer.transform, "How to play", theme.lobbyListHowToText, kit.Bold, theme.lobbyRoomBottomTextSize, theme.lobbyDarkTextColor,
                theme.lobbyCyanColor, theme.lobbyCornerRadius, theme.lobbyCyanColor, 0f);
            LobbyUiKit.Size(howToButton.Root, -1f, theme.lobbyRoomBottomHeight);
            LobbyUiKit.WidenToLabel(howToButton, theme.lobbyRoomBottomPadding);
            howToButton.Button.onClick.AddListener(() => HowToPlayRequested?.Invoke());

            LobbyUiKit.Spacer(footer.transform);
            var gap = new GameObject("Gap", typeof(RectTransform));
            gap.transform.SetParent(footer.transform, false);
            LobbyUiKit.Size(gap, Mathf.Max(0f, theme.lobbyRoomStartGap - theme.lobbyRoomBottomGap), 0f, 0f, 0f);

            startHint = kit.Text(footer.transform, "Start line", "", kit.Body, theme.lobbyRoomStartHintSize, theme.lobbyMutedColor, TextAlignmentOptions.MidlineRight);
            startButton = kit.MakeButton(footer.transform, "Start game", theme.lobbyRoomStartText, kit.Display, theme.lobbyRoomStartTextSize, theme.lobbyOffWhiteColor,
                theme.lobbyPurpleColor, theme.lobbyCornerRadius, theme.lobbyPurpleColor, 0f, theme.lobbyButtonSpacing);
            LobbyUiKit.Size(startButton.Root, -1f, theme.lobbyRoomStartHeight);
            LobbyUiKit.WidenToLabel(startButton, theme.lobbyRoomStartPadding);
            startButton.Button.onClick.AddListener(() => roomManager.GameStart.StartGame());
        }

        private void OnLeaveLobby()
        {
            Debug.Log("[LOBBY] Leave lobby pressed - back to the list");
            roomManager.ReturnToLobbyList();
        }

        // ---- keeping it current ----

        private void Update()
        {
            if (roomManager == null || roomManager.Seats == null) return;
            if (subscribedSeats != roomManager.Seats)
            {
                if (subscribedSeats != null) subscribedSeats.SeatsChanged -= MarkDirty;
                subscribedSeats = roomManager.Seats;
                subscribedSeats.SeatsChanged += MarkDirty;
            }

            LobbySeats seats = roomManager.Seats;
            bool show = wanted && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && seats.HasLayout && seats.Mode != null
                && MatchDirector.LobbyStageNow == LobbySeatRules.LobbyBeforeStart;
            if (show != root.activeSelf)
            {
                root.SetActive(show);
                dirty = true;
            }
            if (!show) return;

            if (builtFor != seats.Mode || built == null)
            {
                BuildFor(seats.Mode);
                dirty = true;
            }
            if (!dirty && Time.unscaledTime < nextRedrawAt) return;
            dirty = false;
            nextRedrawAt = Time.unscaledTime + RedrawSeconds;
            Redraw(seats);
        }

        private void Redraw(LobbySeats seats)
        {
            Room room = PhotonNetwork.CurrentRoom;
            Player master = PhotonNetwork.MasterClient;
            int me = PhotonNetwork.LocalPlayer.ActorNumber;
            string hostName = master != null ? master.NickName : "";
            bool iAmHost = PhotonNetwork.IsMasterClient;

            titleLabel.text = RoomDisplayName(room);
            string modeText = string.Format(theme.lobbyRoomModeFormat, builtFor.DisplayName);
            if (modeButton.Label.text != modeText) modeButton.Label.text = modeText;
            FitModeButton(); // every redraw: the text's width is only known once its glyphs are drawn
            hostLabel.text = string.Format(theme.lobbyRoomHostFormat, "<b><color=#" + ColorUtility.ToHtmlStringRGB(theme.lobbyOffWhiteColor) + ">" + hostName + "</color></b>");

            foreach (SeatView view in seatViews.Values)
            {
                if (seats.Seats.TryGetValue(view.Key, out int actor) && actor > 0)
                {
                    bool mine = actor == me;
                    Player holder = room.GetPlayer(actor);
                    string text = LobbyRoomRules.SeatText(holder != null ? holder.NickName : null, mine, master != null && actor == master.ActorNumber,
                        theme.lobbyRoomYouSuffix, theme.lobbyRoomHostSuffix);
                    ApplySeat(view, mine ? SeatState.Mine : SeatState.Taken, text);
                }
                else
                {
                    ApplySeat(view, SeatState.Empty, theme.lobbyRoomEmptySeatText);
                }
            }

            IReadOnlyList<int> noRole = seats.NoRoleActors;
            noRoleHeading.text = string.Format(theme.lobbyRoomNoRoleFormat, noRole.Count);
            for (int i = 0; i < noRole.Count; i++)
            {
                if (i >= noRoleNames.Count)
                {
                    TextMeshProUGUI label = kit.Text(noRoleList, "Name " + i, "", kit.Body, theme.lobbyRoomSideNameSize, theme.lobbyOffWhiteColor, TextAlignmentOptions.MidlineLeft);
                    LobbyUiKit.Size(label.gameObject, -1f, theme.lobbyRoomSideNameSize * 1.4f);
                    noRoleNames.Add(label);
                }
                Player p = room.GetPlayer(noRole[i]);
                string name = p != null ? p.NickName : "?";
                if (noRole[i] == me) name += theme.lobbyRoomYouSuffix;
                noRoleNames[i].text = name;
                noRoleNames[i].gameObject.SetActive(true);
            }
            for (int i = noRole.Count; i < noRoleNames.Count; i++) noRoleNames[i].gameObject.SetActive(false);

            leaveSeatButton.SetEnabled(seats.MySeat != null);

            // The bottom right: the host's Start game with what it does (or why it is greyed); everyone else reads who they wait for.
            startButton.Root.SetActive(iAmHost);
            if (iAmHost)
            {
                int? blocked = LobbySeatRules.StartBlockReason(seats.Layout, seats.Seats, seats.NoRoleActors);
                startButton.SetEnabled(blocked == null);
                startHint.text = blocked == null
                    ? theme.lobbyRoomStartHintText
                    : string.Format(theme.lobbyRoomStartBlockedFormat, LobbyRoomRules.TeamName(theme.scoreboardTeamNames, blocked.Value));
                startHint.color = blocked == null ? theme.lobbyMutedColor : theme.lobbyErrorColor;
            }
            else
            {
                startHint.text = string.Format(theme.lobbyRoomWaitingFormat, hostName);
                startHint.color = theme.lobbyMutedColor;
            }
        }

        private static string RoomDisplayName(Room room)
        {
            if (room == null) return "";
            return room.CustomProperties.TryGetValue(LobbyKeys.Name, out object raw) && raw is string name && name.Length > 0 ? name : room.Name;
        }

        private void ApplySeat(SeatView view, SeatState state, string text)
        {
            if (view.State != state)
            {
                view.State = state;
                bool empty = state == SeatState.Empty;
                view.Dash.gameObject.SetActive(empty);
                float width = state == SeatState.Mine ? theme.lobbyRoomMineBorderWidth : 0f;
                // the outline of the box: its fill is inset by the width; an empty seat has no box, only the dashes
                view.Button.Border.color = empty ? Color.clear : (state == SeatState.Mine ? theme.lobbyOffWhiteColor : theme.lobbyRoomSeatFill);
                view.Button.Fill.color = empty ? Color.clear : theme.lobbyRoomSeatFill;
                RectTransform fill = view.Button.Fill.rectTransform;
                fill.offsetMin = new Vector2(width, width);
                fill.offsetMax = new Vector2(-width, -width);
                view.Label.color = empty ? theme.lobbyDimColor : theme.lobbyOffWhiteColor;
                view.Label.font = state == SeatState.Mine ? kit.Bold : kit.Body;
                view.Label.fontSize = empty ? (view.Spectator ? theme.lobbyRoomSpectatorTextSize : theme.lobbyRoomSeatEmptyTextSize) : view.TextSize;
            }
            if (view.Text != text)
            {
                view.Text = text;
                view.Label.text = text;
            }
        }
    }
}
