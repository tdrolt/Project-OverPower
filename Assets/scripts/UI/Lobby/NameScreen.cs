using Overpower.Data;
using Overpower.Lobby;
using Overpower.Net;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The first screen of the game and the owner of the lobby screens (lobby Task 9; it replaces JoinGameUI): a centred name screen (PROJECT over
    /// OVERPOWER, the name box, Find a lobby, and Rejoin your match while a match still holds this player's place), then the lobby list and the
    /// create screen, all built in code from UiTheme (LobbyUiKit). Find a lobby needs a valid name (PlayerNameRules with the LobbyConfig
    /// lengths); the typed name is what Photon calls the player for the session. After a match (or a spectator's Leave) RoomManager sets
    /// LobbyReturn and the rebuilt scene opens straight on the list. Joining or creating a room hides the screens; the lobby room screen (Task 10) takes over there. The scene's old name panel and its tips are switched off at start (they stay in the scene, hidden).
    /// </summary>
    public sealed class NameScreen : MonoBehaviourPunCallbacks
    {
        /// <summary>Which of the screens is up.</summary>
        public enum Screen { Name, List, Create, InRoom }

        [SerializeField] private RoomManager roomManager;

        [Tooltip("Read for every colour, size, font and text of the lobby screens, and for the Rejoin your match wording.")]
        [SerializeField] private UiTheme theme;

        [Tooltip("The chat object the scene keeps switched off until a room is joined.")]
        [SerializeField] private GameObject chatmanager;

        [Tooltip("The scene's old name panel (name box, Join button, tips). It is switched off when the game starts; the new screens replace it.")]
        [FormerlySerializedAs("joinUIPanel")]
        [SerializeField] private GameObject legacyPanel;

        private LobbyUiKit kit;
        private Canvas canvas;
        private GameObject nameRoot;
        private TMP_InputField nameInput;
        private TextMeshProUGUI hintLabel;
        private LobbyButton findButton;
        private LobbyButton rejoinButton;
        private CanvasGroup rejoinGroup;
        private LobbyListPanel list;
        private CreateLobbyPanel create;
        private LobbyRoomPanel room;
        private ModeInfoPanel modeInfo;
        private HowToPlayPanel howToPlay;
        private WarmupBar warmupBar;

        private Screen current = Screen.Name;
        private bool enterListWhenConnected;
        private bool backToListWhenLeft;
        private string messageForList;
        private float nextOfferCheckAt;
        private const float OfferCheckSeconds = 1f;

        /// <summary>The screen that is up.</summary>
        public Screen Current => current;
        public TMP_InputField NameInput => nameInput;
        public LobbyButton FindButton => findButton;
        public LobbyButton RejoinButton => rejoinButton;
        public LobbyListPanel List => list;
        public CreateLobbyPanel CreatePanel => create;
        public LobbyRoomPanel Room => room;
        public ModeInfoPanel ModeInfo => modeInfo;
        public HowToPlayPanel HowToPlay => howToPlay;
        public WarmupBar Warmup => warmupBar;

        /// <summary>The name in the box (what Find a lobby would use).</summary>
        public string TypedName => nameInput != null ? nameInput.text : "";

        /// <summary>The hint line under the name box as drawn.</summary>
        public string HintText => hintLabel != null ? hintLabel.text : "";

        private int MinLength => roomManager != null && roomManager.LobbyCfg != null ? roomManager.LobbyCfg.NameMinLength : LobbyConfig.DefaultNameMinLength;
        private int MaxLength => roomManager != null && roomManager.LobbyCfg != null ? roomManager.LobbyCfg.NameMaxLength : LobbyConfig.DefaultNameMaxLength;

        private void Start()
        {
            if (theme == null || roomManager == null)
            {
                Debug.LogError("[NAME SCREEN] the NameScreen needs the RoomManager and the UiTheme assigned - no lobby screens are built");
                return;
            }
            if (legacyPanel != null) legacyPanel.SetActive(false);

            kit = new LobbyUiKit(theme);
            canvas = kit.CreateCanvas(transform, "Lobby Screens Canvas");
            BuildNameScreen(canvas.transform);
            list = LobbyListPanel.Create(canvas.transform, kit, roomManager);
            create = CreateLobbyPanel.Create(canvas.transform, kit, roomManager);
            room = LobbyRoomPanel.Create(canvas.transform, kit, roomManager);
            warmupBar = WarmupBar.Create(canvas.transform, kit);
            modeInfo = ModeInfoPanel.Create(canvas.transform, kit);
            howToPlay = HowToPlayPanel.Create(canvas.transform, kit);

            list.CreateClicked += OpenCreate;
            list.JoinRequested += OnJoinRequested;
            list.HowToPlayClicked += howToPlay.Show;
            room.HowToPlayRequested += howToPlay.Show;
            room.ModeInfoRequested += modeInfo.Show;
            create.Cancelled += OpenList;
            roomManager.Lobbies.JoinFailed += OnJoinFailed;
            roomManager.Lobbies.CreateFailed += OnCreateFailed;
            if (roomManager.Rejoin != null) roomManager.Rejoin.ReturnToNameScreen += ShowNameScreenAgain;

            // The name kept for the session: Photon's NickName survives the scene rebuild after a match.
            if (!string.IsNullOrEmpty(PhotonNetwork.NickName))
                nameInput.text = PhotonNetwork.NickName;
            ApplyNameValidity();

            // Lobby Task 8: coming back from a match (or a spectator's Leave) skips the name step.
            if (LobbyReturn.Consume())
            {
                Debug.Log("[LOBBY] back on the list");
                OpenList();
                RequestList();
            }
            else
            {
                ShowOnly(Screen.Name);
            }
        }

        private void OnDestroy()
        {
            if (roomManager != null)
            {
                if (roomManager.Lobbies != null)
                {
                    roomManager.Lobbies.JoinFailed -= OnJoinFailed;
                    roomManager.Lobbies.CreateFailed -= OnCreateFailed;
                }
                if (roomManager.Rejoin != null) roomManager.Rejoin.ReturnToNameScreen -= ShowNameScreenAgain;
            }
            kit?.Dispose();
        }

        // ---- the name screen ----

        private void BuildNameScreen(Transform parent)
        {
            Image backdrop = kit.Backdrop(parent, "Name Screen", theme.lobbyDarkColor);
            nameRoot = backdrop.gameObject;

            VerticalLayoutGroup column = LobbyUiKit.VGroup(nameRoot.transform, "Column", theme.nameScreenGap, TextAnchor.UpperCenter);
            RectTransform columnRect = (RectTransform)column.transform;
            columnRect.anchorMin = columnRect.anchorMax = columnRect.pivot = new Vector2(0.5f, 0.5f);
            columnRect.sizeDelta = new Vector2(theme.nameScreenWidth, 0f);
            column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            VerticalLayoutGroup title = LobbyUiKit.VGroup(column.transform, "Title", theme.nameScreenTitleGap, TextAnchor.UpperCenter);
            kit.Text(title.transform, "Caption", theme.nameScreenKickerText, kit.Bold, theme.nameScreenKickerSize, theme.lobbyCyanColor,
                TextAlignmentOptions.Midline, theme.lobbyKickerSpacing);
            TextMeshProUGUI big = kit.OutlinedTitle(title.transform, "OVERPOWER", theme.nameScreenTitleText, theme.nameScreenTitleSize);
            big.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Size(big.gameObject, -1f, theme.nameScreenTitleSize * 0.95f);

            VerticalLayoutGroup nameBlock = LobbyUiKit.VGroup(column.transform, "Name", theme.nameScreenInnerGap);
            kit.Text(nameBlock.transform, "Label", theme.nameScreenLabelText, kit.Bold, theme.nameScreenLabelSize, theme.lobbyMutedColor,
                TextAlignmentOptions.Midline, theme.lobbyLabelSpacing);
            nameInput = kit.InputBox(nameBlock.transform, "Name box", "", theme.nameScreenFieldHeight, theme.nameScreenFieldTextSize,
                theme.nameScreenFieldPadding, TextAlignmentOptions.Midline, MaxLength, true, out _);
            nameInput.onValueChanged.AddListener(_ => OnNameChanged());
            hintLabel = kit.Text(nameBlock.transform, "Hint", NameHint(), kit.Body, theme.nameScreenHintSize, theme.lobbyDimColor, TextAlignmentOptions.Midline);

            findButton = kit.MakeButton(column.transform, "Find a lobby", theme.nameScreenFindText, kit.Display, theme.nameScreenFindTextSize,
                theme.lobbyOffWhiteColor, theme.lobbyPurpleColor, theme.lobbyCornerRadius, theme.lobbyPurpleColor, 0f, theme.lobbyButtonSpacing);
            LobbyUiKit.Size(findButton.Root, -1f, theme.nameScreenFindHeight);
            findButton.Button.onClick.AddListener(OnFindClicked);

            rejoinButton = kit.MakeButton(column.transform, "Rejoin your match", theme.rejoinMatchButton, kit.Bold, theme.nameScreenRejoinTextSize,
                theme.lobbyOffWhiteColor, theme.lobbyDarkColor, theme.lobbyCornerRadius, theme.lobbyCyanColor, theme.lobbyBorderWidth);
            LobbyUiKit.Size(rejoinButton.Root, -1f, theme.nameScreenRejoinHeight);
            rejoinButton.Button.onClick.AddListener(OnRejoinClicked);
            // Kept in the layout but invisible while no match holds a place, so the column does not jump when the offer appears.
            rejoinGroup = rejoinButton.Root.AddComponent<CanvasGroup>();
            SetRejoinOffered(false);
        }

        private string NameHint() => string.Format(theme.nameScreenHintFormat, MinLength, MaxLength);

        private void OnNameChanged()
        {
            hintLabel.text = NameHint();
            hintLabel.color = theme.lobbyDimColor;
            ApplyNameValidity();
        }

        private bool NameIsValid => PlayerNameRules.IsValid(nameInput.text, MinLength, MaxLength);

        private void ApplyNameValidity() => findButton.SetEnabled(NameIsValid);

        /// <summary>Types a name into the box (what a driver does instead of the keyboard). The box's own limit applies.</summary>
        public void TypeName(string text) => nameInput.text = text;

        /// <summary>Presses Find a lobby (what a click does).</summary>
        public void PressFind() => findButton.Press();

        private void OnFindClicked()
        {
            if (!NameIsValid) return;
            PhotonNetwork.NickName = nameInput.text;
            Debug.Log($"[LOBBY] Find a lobby: playing as {PhotonNetwork.NickName}");
            OpenList();
            RequestList();
        }

        /// <summary>Joins Photon's lobby for the list as soon as the connection is ready (checked every frame in Update). A join or a create that
        /// was refused leaves Photon's lobby, and Photon only sends list changes while in it, so those ask for the list again too.</summary>
        private void RequestList()
        {
            enterListWhenConnected = true;
            ConnectIfDropped();
        }

        /// <summary>A client that dropped off Photon (or never connected) connects again; nothing else will, and the list would wait for ever.
        /// Asked once, by RequestList (every way onto the list goes through it); a press made while a disconnect is still finishing is not retried: the
        /// disconnect's OnDisconnected gives the name screen back, and the next Find a lobby connects.</summary>
        private void ConnectIfDropped()
        {
            if (LobbyScreenRules.MustConnectForList(PhotonNetwork.NetworkClientState))
            {
                Debug.Log("[LOBBY] not connected - connecting again for the list");
                PhotonNetwork.ConnectUsingSettings();
            }
        }

        // ---- moving between the screens ----

        private void ShowOnly(Screen screen)
        {
            current = screen;
            nameRoot.SetActive(screen == Screen.Name);
            if (screen != Screen.List) list.Hide();
            if (screen != Screen.Create) create.Hide();
            if (screen == Screen.InRoom) room.Show(); else room.Hide();
            modeInfo.Hide();
            howToPlay.Hide();
            if (screen == Screen.List) list.Show(PhotonNetwork.NickName);
            if (screen == Screen.Name) nextOfferCheckAt = 0f;
        }

        private void OpenList() => ShowOnly(Screen.List);

        private void OpenCreate()
        {
            ShowOnly(Screen.Create);
            create.Show(PhotonNetwork.NickName);
        }

        private void OnJoinRequested(string roomName)
        {
            Debug.Log($"[LOBBY] join pressed: {roomName}");
            roomManager.Lobbies.Join(roomName);
        }

        private void OnJoinFailed(string text)
        {
            if (current == Screen.List) RequestList();
            if (current == Screen.InRoom)
            {
                // a join that went through and was then undone (a running lobby with no seat): the list comes back with the reason once the room is left
                backToListWhenLeft = true;
                messageForList = text;
            }
            else if (current == Screen.List)
            {
                list.ShowMessage(text);
            }
        }

        private void OnCreateFailed(string message)
        {
            if (current != Screen.Create) OpenCreate();
            create.ShowFailure(theme.createFailedText);
            RequestList();
        }

        /// <summary>The player gave up a rejoin (Leave, or OK on "the match is gone"): the name screen comes back for a normal start. The panel was
        /// hidden when they joined the match that has since dropped them.</summary>
        private void ShowNameScreenAgain()
        {
            enterListWhenConnected = false;
            backToListWhenLeft = false;
            messageForList = null;
            if (chatmanager != null) chatmanager.SetActive(false);
            ShowOnly(Screen.Name);
            ApplyNameValidity();
        }

        // ---- Rejoin your match ----

        private void Update()
        {
            if (enterListWhenConnected && roomManager != null && roomManager.Lobbies != null && PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom)
            {
                enterListWhenConnected = false;
                roomManager.Lobbies.EnterList();
            }
            if (rejoinButton == null || Time.unscaledTime < nextOfferCheckAt) return;
            nextOfferCheckAt = Time.unscaledTime + OfferCheckSeconds;
            bool offered = current == Screen.Name && roomManager != null && roomManager.Rejoin != null && roomManager.Rejoin.RejoinOffered();
            SetRejoinOffered(offered);
        }

        private void SetRejoinOffered(bool offered)
        {
            rejoinGroup.alpha = offered ? 1f : 0f;
            rejoinGroup.blocksRaycasts = offered;
            rejoinGroup.interactable = offered;
        }

        private void OnRejoinClicked()
        {
            if (roomManager == null || roomManager.Rejoin == null) return;
            SetRejoinOffered(false);
            roomManager.Rejoin.RejoinFromNameScreen();
        }

        // ---- Photon ----

        public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
        {
            enterListWhenConnected = false;
            if (nameInput == null) return;
            // Give the screen back rather than stranding the player on a list that can no longer change.
            if (current == Screen.List || current == Screen.Create)
            {
                ShowOnly(Screen.Name);
                hintLabel.text = theme.lobbyConnectionLostText;
                hintLabel.color = theme.lobbyErrorColor;
            }
            ApplyNameValidity();
        }

        public override void OnJoinedRoom()
        {
            if (nameInput == null) return;
            ShowOnly(Screen.InRoom);
            if (chatmanager != null) chatmanager.SetActive(true);
        }

        public override void OnLeftRoom()
        {
            if (!backToListWhenLeft || nameInput == null) return;
            backToListWhenLeft = false;
            if (chatmanager != null) chatmanager.SetActive(false);
            OpenList();
            if (messageForList != null) list.ShowMessage(messageForList);
            messageForList = null;
            RequestList();
        }
    }
}
