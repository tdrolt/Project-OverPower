using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Chat;
using ExitGames.Client.Photon;
using Photon.Pun;
using TMPro;
using Overpower.Chat;
using Overpower.Lobby;
using Overpower.Net;
using Overpower.UI;

public class PhotonChat : MonoBehaviour, IChatClientListener
{
    ChatClient chatClient;
    bool isConnected;
    string privateReceiver = "";

    [SerializeField] GameObject chatPanel;
    // 2026-09-27 bug fix: this used to be wired (in "chat manager.prefab") to the SAME GameObject as
    // chatField's own TMP_InputField text component ("chat input/Text Area/Text") - a scene wiring
    // mistake, not a deliberate double-use. Update() below calls text.SetActive(false) the instant
    // chat opens, which disabled the exact object TMP_InputField renders typed characters into: you
    // could open chat, type, and never see a single character, because the one thing on screen that
    // would have shown it had just been turned off. There is no other "closed-chat hint" object in
    // the prefab to point this at, so the reference is now null in the prefab and every use below is
    // null-guarded - safe today, and safe again if a real hint object is wired in later.
    [SerializeField] GameObject text;
    [SerializeField] TMP_InputField chatField;
    [SerializeField] TextMeshProUGUI chatDisplay;

    /// <summary>Playtest extras P6 (2026-09-26): whether the chat panel is open, kept in step with
    /// chatPanel.activeSelf below (both places that change it also set this) rather than read live
    /// off the GameObject, so QuitConfirmPanel can check it with no scene reference of its own - same
    /// static-bool pattern as LoadoutScreen.IsOpen. There is only ever one PhotonChat in the scene.</summary>
    public static bool IsOpen { get; private set; }

    // ---- lobby Task 11 (D10): one channel per lobby ----
    // The chat manager object is switched on by the name screen when a room is joined and off when it is left (NameScreen.OnJoinedRoom /
    // OnLeftRoom), so the chat connects when it is enabled and disconnects when it is disabled. While connected, Update keeps the subscription
    // equal to the channel of the room this client is in (ChatChannelRule): nothing on the lobby list, the room's own channel inside a lobby.
    // The chat user id is the install's stable player id (PlayerIdentity.UserId), never the nickname; who sent a line, their team and whether
    // they spectate travel inside the message (ChatMessage), and the id is never shown or logged in full.

    string subscribedChannel;       // the channel this client asked for (null: none)
    float nextConnectAt;
    bool lookApplied;
    bool connectPending;            // set when enabled; the connection is made once the previous one has closed (ChatClientReaper)
    UiTheme theme;
    Canvas chatCanvas;
    RoomManager roomManager;
    NameScreen nameScreen;
    int lastLayout = -1;            // 0 = arena corner, 1 = lobby room corner
    readonly List<string> lines = new List<string>();
    readonly List<string> plainLines = new List<string>();

    const float ReconnectSeconds = 3f;
    TextMeshProUGUI notSentHint;    // built on the first refused line
    float notSentHintUntil;

    /// <summary>The channel this client is subscribed to (or has just asked for); null on the list.</summary>
    public string SubscribedChannel => subscribedChannel;

    /// <summary>True once the chat server connection is up and a channel is asked for.</summary>
    public bool IsListening => chatClient != null && chatClient.CanChat && subscribedChannel != null;

    /// <summary>The lines now on screen as plain text ("Name: text", "[SPEC] Name: text"), oldest first.</summary>
    public IReadOnlyList<string> PlainLines => plainLines;

    public string ChatStateName => chatClient == null ? "none" : chatClient.State.ToString();

    void OnEnable()
    {
        if (roomManager == null) roomManager = FindFirstObjectByType<RoomManager>();
        if (nameScreen == null) nameScreen = FindFirstObjectByType<NameScreen>();
        theme = roomManager != null ? roomManager.Theme : null;
        ApplyLook();
        connectPending = true;
    }

    void OnDisable()
    {
        if (notSentHint != null) notSentHint.gameObject.SetActive(false);
        SetOpen(false);
        connectPending = false;
        ChatClientReaper.Close(chatClient);
        chatClient = null;
        isConnected = false;
        subscribedChannel = null;
        ClearLines();
    }

    /// <summary>Task 9f: the scene is rebuilt when a player goes back to the name screen. The old chat connection and the open flag must
    /// not outlive it (the next join connects again under the new name).</summary>
    void OnDestroy()
    {
        IsOpen = false;
        ChatClientReaper.Close(chatClient);
        chatClient = null;
    }

    private void ConnectToChat()
    {
        ChatClientReaper.Close(chatClient);
        isConnected = true;
        chatClient = new ChatClient(this);
        subscribedChannel = null;

        // Lobby Task 11: the chat user id is the stable player id, not the nickname (two players may share a name; the name travels in the message).
        chatClient.Connect(PhotonNetwork.PhotonServerSettings.AppSettings.AppIdChat, PhotonNetwork.AppVersion,
            new AuthenticationValues(PlayerIdentity.UserId));

        Debug.Log("Connecting to chat (user " + ChatLine.ShortId(PlayerIdentity.UserId) + "...)");
    }

    /// <summary>Keeps the subscription equal to the room's channel and reconnects a dropped chat connection. Runs every frame; cheap.</summary>
    private void ReconcileChannel()
    {
        if (chatClient == null) return;
        if (chatClient.State == ChatState.Disconnected || chatClient.State == ChatState.Uninitialized)
        {
            subscribedChannel = null;
            if (Time.unscaledTime >= nextConnectAt)
            {
                nextConnectAt = Time.unscaledTime + ReconnectSeconds;
                ConnectToChat();
            }
            return;
        }
        if (!chatClient.CanChat) return;

        string wanted = ChatChannelRule.ChannelFor(PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : null);
        if (wanted == subscribedChannel) return;

        if (subscribedChannel != null)
            chatClient.Unsubscribe(new[] { subscribedChannel });
        subscribedChannel = null;
        ClearLines();
        if (wanted != null)
        {
            subscribedChannel = wanted;
            chatClient.Subscribe(new[] { wanted });
        }
    }

    private void ClearLines()
    {
        lines.Clear();
        plainLines.Clear();
        if (chatDisplay != null) chatDisplay.text = "";
    }

    /// <summary>Opens or closes the chat panel (Enter and Escape call this; so can a test driver).</summary>
    public void SetOpen(bool open)
    {
        if (chatPanel == null) return;
        if (open)
        {
            if (chatPanel.activeSelf) return;
            if (text != null) text.SetActive(false);
            chatPanel.SetActive(true);
            IsOpen = true;
            chatField.Select();            // focus on the chat input field
            chatField.ActivateInputField();
        }
        else
        {
            if (chatPanel.activeSelf)
            {
                chatPanel.SetActive(false);
                if (text != null) text.SetActive(true);  // show the hint when chat is closed
                chatField.DeactivateInputField();
            }
            IsOpen = false;
        }
    }

    /// <summary>Publishes a line to the current lobby's channel as this player (name, team, spectator seat). Nothing is sent outside a room.
    /// Returns true only when the line really went out; a refused line is neither logged nor cleared (ChatSendRule), so a retry is logged once.</summary>
    public bool Send(string typed) => SendWithOutcome(typed) == ChatSendOutcome.Sent;

    /// <summary>Send, and say what happened (Ignored / Refused / Sent): the typing box's Enter handler acts on this outcome through
    /// ChatSendRule, so the helper the tests cover is the code that decides.</summary>
    public ChatSendOutcome SendWithOutcome(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return ChatSendOutcome.Ignored;
        bool canPublish = chatClient != null && chatClient.CanChat && subscribedChannel != null
            && subscribedChannel == ChatChannelRule.ChannelFor(PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : null);
        bool published = false;
        if (canPublish)
        {
            ResolveSender(out int team, out bool spectator);
            var message = new ChatMessage(PhotonNetwork.NickName, team, spectator, typed);
            published = chatClient.PublishMessage(subscribedChannel, message.Encode());
        }
        ChatSendOutcome outcome = ChatSendRule.Outcome(typed, canPublish, published);
        // Playtest extras P3 (2026-09-26): the SENDER's own text, never OnGetMessages' receive callback. Logged only once the line really went
        // out (Dominion Task 1 Part 0): a line refused during a chat reconnect stays in the box and would otherwise be logged on every retry.
        if (ChatSendRule.IsLogged(outcome))
            Overpower.Telemetry.MatchTelemetry.Instance?.LogChat(typed);
        return outcome;
    }

    /// <summary>The team and spectator flag this player chats under: from the match (player properties) once the game has started, from the
    /// seat held in the lobby room before that. A player with no seat has no team (-1).</summary>
    private void ResolveSender(out int team, out bool spectator)
    {
        var me = PhotonNetwork.LocalPlayer;
        spectator = Teams.IsSpectator(me);
        team = -1;
        if (spectator) return;
        if (Teams.TryGetPlayingTeam(me, out int playing))
        {
            team = playing;
            return;
        }
        string seat = roomManager != null && roomManager.Seats != null ? roomManager.Seats.MySeat : null;
        if (LobbySeatRules.IsSpectatorSeat(seat)) spectator = true;
        else if (LobbySeatRules.TryTeamOfSeat(seat, out int seatTeam)) team = seatTeam;
    }

    private string ColourHexFor(ChatMessage message)
    {
        if (theme == null) return "FFFFFF";
        Color colour = message.Spectator ? theme.lobbyMutedColor : theme.ShotColorFor(message.Team);
        return ColorUtility.ToHtmlStringRGB(new Color(colour.r, colour.g, colour.b, 1f));
    }

    private string SpectatorTag => theme != null ? theme.chatSpectatorTag : "[SPEC]";

    /// <summary>Restyles the prefab's chat panel from the UiTheme "Chat" fields (board 7A): a see-through dark panel bottom left, bigger text in
    /// Public Sans, no Send button (Enter sends), a typing box with the placeholder. Done in code so the numbers live in one place (UiTheme).</summary>
    private void ApplyLook()
    {
        if (lookApplied || theme == null || chatPanel == null || chatField == null || chatDisplay == null) return;
        lookApplied = true;

        Canvas canvas = chatPanel.GetComponentInParent<Canvas>();
        chatCanvas = canvas;
        if (canvas != null)
        {
            canvas.sortingOrder = ChatPanelRule.SortingOrder(false); // PlacePanel raises it while the lobby room shows
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.referenceResolution = theme.referenceResolution;
                scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            }
        }

        var panel = (RectTransform)chatPanel.transform;
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
        PlacePanel(false);
        Image panelImage = chatPanel.GetComponent<Image>();
        LobbyUiKit.Round(panelImage, theme.chatPanelRadius);
        panelImage.color = new Color(theme.chatPanelColor.r, theme.chatPanelColor.g, theme.chatPanelColor.b, theme.chatPanelAlpha);
        panelImage.raycastTarget = false;

        // "Chat room" holds the lines, the typing box and the old Send button.
        var room = (RectTransform)chatField.transform.parent;
        LobbyUiKit.Stretch(room);
        room.offsetMin = new Vector2(theme.chatPanelPadding, theme.chatPanelPadding);
        room.offsetMax = new Vector2(-theme.chatPanelPadding, -theme.chatPanelPadding);

        Transform send = room.Find("send button");
        if (send != null) send.gameObject.SetActive(false);

        var field = (RectTransform)chatField.transform;
        field.anchorMin = new Vector2(0f, 0f);
        field.anchorMax = new Vector2(1f, 0f);
        field.pivot = new Vector2(0.5f, 0f);
        field.anchoredPosition = Vector2.zero;
        field.sizeDelta = new Vector2(0f, theme.chatInputHeight);
        Image fieldImage = chatField.GetComponent<Image>();
        if (fieldImage != null)
        {
            float radius = theme.chatPanelRadius * theme.chatInputCornerFactor;
            LobbyUiKit.Round(fieldImage, radius);
            fieldImage.color = theme.lobbyBorderColor;
            var inner = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            inner.transform.SetParent(field, false);
            inner.transform.SetAsFirstSibling();
            var innerRect = (RectTransform)inner.transform;
            LobbyUiKit.Stretch(innerRect);
            innerRect.offsetMin = new Vector2(theme.chatInputBorderWidth, theme.chatInputBorderWidth);
            innerRect.offsetMax = new Vector2(-theme.chatInputBorderWidth, -theme.chatInputBorderWidth);
            Image innerImage = inner.GetComponent<Image>();
            LobbyUiKit.Round(innerImage, radius - theme.chatInputBorderWidth);
            innerImage.color = theme.lobbyPanelColor;
            innerImage.raycastTarget = false;
            chatField.targetGraphic = fieldImage;
        }
        TMP_FontAsset body = theme.lobbyBodyFont;
        if (chatField.textViewport != null)
        {
            chatField.textViewport.offsetMin = new Vector2(theme.chatInputTextInset, 0f);
            chatField.textViewport.offsetMax = new Vector2(-theme.chatInputTextInset, 0f);
        }
        StyleInputText(chatField.textComponent, body, theme.lobbyOffWhiteColor, theme.chatInputTextSize);
        TMP_Text hint = chatField.placeholder as TMP_Text;
        StyleInputText(hint, body, theme.lobbyDimColor, theme.chatInputTextSize);
        if (hint != null) hint.text = theme.chatInputPlaceholder;
        if (body != null) chatField.fontAsset = body;
        chatField.pointSize = theme.chatInputTextSize;
        chatField.characterLimit = ChatMessage.MaxTextLength;
        chatField.customCaretColor = true;
        chatField.caretColor = theme.lobbyOffWhiteColor;
        chatField.transition = Selectable.Transition.None;
        chatField.text = ""; // the prefab was saved with test text in the box

        // The lines: a clipped area above the typing box; the text is bottom-aligned so the newest line sits just above the box.
        var scroll = (RectTransform)chatDisplay.transform.parent;
        LobbyUiKit.Stretch(scroll);
        scroll.offsetMin = new Vector2(0f, theme.chatInputHeight + theme.chatPanelGap);
        ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
        if (scrollRect != null) scrollRect.enabled = false;
        Mask mask = scroll.GetComponent<Mask>();
        if (mask != null) Destroy(mask);
        Image scrollImage = scroll.GetComponent<Image>();
        if (scrollImage != null) { scrollImage.color = Color.clear; scrollImage.raycastTarget = false; }
        if (scroll.GetComponent<RectMask2D>() == null) scroll.gameObject.AddComponent<RectMask2D>();

        var display = (RectTransform)chatDisplay.transform;
        LobbyUiKit.Stretch(display);
        display.pivot = new Vector2(0.5f, 0.5f);
        if (body != null) chatDisplay.font = body;
        chatDisplay.fontSize = theme.chatTextSize;
        chatDisplay.enableAutoSizing = false;
        chatDisplay.color = theme.lobbyOffWhiteColor;
        chatDisplay.alignment = TextAlignmentOptions.BottomLeft;
        chatDisplay.enableWordWrapping = true;
        chatDisplay.overflowMode = TextOverflowModes.Overflow;
        chatDisplay.richText = true;
        chatDisplay.lineSpacing = theme.chatLineSpacing;
        chatDisplay.raycastTarget = false;
    }

    /// <summary>Puts the panel in the arena corner, or - while the lobby room screen is up - right of that screen's bottom buttons and under its
    /// spectator row (UiTheme chatLobby*).</summary>
    private void PlacePanel(bool lobbyRoom)
    {
        if (theme == null || chatPanel == null) return;
        lastLayout = lobbyRoom ? 1 : 0;
        if (chatCanvas != null) chatCanvas.sortingOrder = ChatPanelRule.SortingOrder(lobbyRoom); // over the lobby screens only while the lobby room shows
        var panel = (RectTransform)chatPanel.transform;
        panel.anchoredPosition = lobbyRoom ? theme.chatLobbyMargin : theme.chatPanelMargin;
        panel.sizeDelta = lobbyRoom ? theme.chatLobbySize : theme.chatPanelSize;
    }

    private static void StyleInputText(TMP_Text label, TMP_FontAsset font, Color colour, float size)
    {
        if (label == null) return;
        if (font != null) label.font = font;
        label.color = colour;
        label.fontStyle = FontStyles.Normal;
        label.fontSize = size;
        label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
    }

    public void DebugReturn(DebugLevel level, string message)
    {
        // The Photon Chat library writes the user id (the install's player id) into some of its messages: only its first 8 characters may reach a log.
        string id = PlayerIdentity.UserId;
        if (!string.IsNullOrEmpty(id) && message != null && message.Contains(id))
            message = message.Replace(id, ChatLine.ShortId(id) + "...");
        Debug.Log(message);
    }

    public void OnChatStateChange(ChatState state)
    {
        Debug.Log("Chat state changed to: " + state);
    }

    public void OnConnected()
    {
        Debug.Log("Connected to chat");
        // The channel is subscribed by ReconcileChannel (Update), from the room this client is in.
    }

    public void OnDisconnected()
    {
        Debug.Log("Disconnected from chat");
        subscribedChannel = null;
    }

    public void OnGetMessages(string channelName, string[] senders, object[] messages)
    {
        // A late message from a channel this client has left (or is not subscribed to) is dropped.
        if (!ChatChannelRule.Accepts(subscribedChannel, channelName)) return;

        bool added = false;
        for (int i = 0; i < messages.Length; i++)
        {
            if (!ChatMessage.TryDecode(messages[i], out ChatMessage message)) continue; // anything malformed is ignored
            lines.Add(ChatLine.Format(message, SpectatorTag, ColourHexFor(message)));
            plainLines.Add(ChatLine.Speaker(message, SpectatorTag) + ": " + message.Text);
            added = true;
        }
        if (!added) return;
        int keep = theme != null ? theme.chatMaxLines : UiTheme.DefaultChatMaxLines;
        ChatLine.Trim(lines, keep);
        ChatLine.Trim(plainLines, keep);
        chatDisplay.text = string.Join("\n", lines); // display the received public message
    }

    public void OnPrivateMessage(string sender, object message, string channelName)
    {
        string privateMsg = string.Format("{0} (private to {1}): {2}", sender, privateReceiver, message);
        chatDisplay.text += "\n" + privateMsg; // Display the private message
    }

    public void OnStatusUpdate(string user, int status, bool gotMessage, object message)
    {
        Debug.Log($"{ChatLine.ShortId(user)} is now {status}");
    }

    public void OnSubscribed(string[] channels, bool[] results)
    {
        Debug.Log("Subscribed to channels: " + string.Join(", ", channels));
    }

    public void OnUnsubscribed(string[] channels)
    {
        Debug.Log("Unsubscribed from channels: " + string.Join(", ", channels));
    }

    public void OnUserSubscribed(string channel, string user)
    {
        Debug.Log($"{ChatLine.ShortId(user)} has subscribed to {channel}");
    }

    public void OnUserUnsubscribed(string channel, string user)
    {
        Debug.Log($"{ChatLine.ShortId(user)} has unsubscribed from {channel}");
    }

    public void SubmitPublicChatOnClick()
    {
        if (!string.IsNullOrEmpty(chatField.text))
        {
            // To the current lobby's channel only. The field clears only when the line really went out (Lobby Task 15b): a line typed
            // during a chat reconnect (Send returns false while the client cannot chat) stays in the field to be sent again.
            ChatSendOutcome outcome = SendWithOutcome(chatField.text);
            if (ChatSendRule.ClearsTheBox(outcome)) chatField.text = "";
            if (ChatSendRule.ShowsHint(outcome)) ShowNotSentHint();
            else if (outcome == ChatSendOutcome.Sent) HideNotSentHint();
        }
    }

    /// <summary>The short "not sent, chat reconnecting" hint above the typing box (UiTheme chatNotSent*); hides itself after a few seconds.</summary>
    public bool NotSentHintShowing => notSentHint != null && notSentHint.gameObject.activeSelf;
    public string NotSentHintText => notSentHint != null ? notSentHint.text : "";

    private void HideNotSentHint()
    {
        if (notSentHint != null) notSentHint.gameObject.SetActive(false);
    }

    private void ShowNotSentHint()
    {
        if (theme == null || chatPanel == null || chatField == null) return;
        if (notSentHint == null)
        {
            var go = new GameObject("Not sent hint", typeof(RectTransform));
            go.transform.SetParent(chatPanel.transform, false);
            notSentHint = go.AddComponent<TextMeshProUGUI>();
            if (theme.lobbyBodyFont != null) notSentHint.font = theme.lobbyBodyFont;
            notSentHint.fontSize = theme.chatNotSentHintSize;
            notSentHint.color = theme.lobbyErrorColor;
            notSentHint.alignment = TextAlignmentOptions.BottomRight;
            notSentHint.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            // Stretch between the panel's padding on both sides (offsetMin/offsetMax), height from the text size.
            rt.offsetMin = new Vector2(theme.chatPanelPadding, rt.offsetMin.y);
            rt.offsetMax = new Vector2(-theme.chatPanelPadding, rt.offsetMax.y);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, theme.chatNotSentHintSize * theme.chatNotSentHintLineHeight);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, theme.chatPanelPadding + theme.chatInputHeight + theme.chatNotSentHintGap);
        }
        notSentHint.text = theme.chatNotSentHint;
        notSentHint.gameObject.SetActive(true);
        notSentHintUntil = Time.unscaledTime + theme.chatNotSentHintSeconds;
    }

    public void SubmitPrivateChatOnClick()
    {
        if (!string.IsNullOrEmpty(privateReceiver) && !string.IsNullOrEmpty(chatField.text))
        {
            chatClient.SendPrivateMessage(privateReceiver, chatField.text); // Send private message
            chatField.text = ""; // Clear input field after sending
        }
        else
        {
            Debug.LogError("Private receiver not set or message is empty.");
        }
    }

    void Update()
    {
        if (notSentHint != null && notSentHint.gameObject.activeSelf && Time.unscaledTime >= notSentHintUntil)
            notSentHint.gameObject.SetActive(false);
        if (connectPending && !ChatClientReaper.Busy)
        {
            connectPending = false;
            ConnectToChat();
        }
        if (isConnected)
        {
            chatClient?.Service();
            ReconcileChannel();
        }

        bool inLobbyRoom = nameScreen != null && nameScreen.Room != null && nameScreen.Room.IsShowing;
        if (lookApplied && lastLayout != (inLobbyRoom ? 1 : 0)) PlacePanel(inLobbyRoom);

        // The chat closes itself when the room is left (not when only the chat subscription drops: a short chat-server reconnect keeps it open, with what was typed) or How to play / the mode info page opens over it.
        if (ChatPanelRule.MustClose(chatPanel.activeSelf, PhotonNetwork.InRoom, LobbyOverlayPanel.AnyPageOpen))
            SetOpen(false);

        // Toggle chat panel visibility on Enter key press
        if (Input.GetKeyDown(KeyCode.Return))
        {
            // If the chat is closed, open it and focus on the input field (only while it has a channel: on the name, list and Create
            // screens Enter does nothing); if it is open, send the message
            if (!chatPanel.activeSelf)
            {
                if (ChatPanelRule.MayOpen(subscribedChannel)) SetOpen(true);
            }
            else
                SubmitPublicChatOnClick();
        }

        // Close the chat panel on Escape key press
        if (Input.GetKeyDown(KeyCode.Escape) && chatPanel.activeSelf)
            SetOpen(false);
    }

    public void TypeChatOnValueChange(string valueIn)
    {
        // This function can be used if you need to do something with the input change
    }

    public void ReceiverOnValueChange(string valueIn)
    {
        privateReceiver = valueIn; // Update the private receiver
    }
}
