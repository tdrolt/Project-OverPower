using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;
using Photon.Pun;
using TMPro;
using Overpower.Lobby;
using Overpower.Net;
using Overpower.UI;


public class JoinGameUI : MonoBehaviourPunCallbacks
{
    public TMP_InputField nameInput;
    public Button joinButton;
    public GameObject joinUIPanel; // Parent UI panel to hide
    public GameObject chatmanager;
    public GameObject chattext;
    public RoomManager roomManager;

    [Tooltip("Read for the tips shown under the name box (Name screen tips section). Empty = no tips.")]
    [SerializeField] private UiTheme theme;

    /// <summary>Task 9e: "Rejoin your match", built in code beside the name box, shown only while a match this install
    /// dropped out of is still holding its place (RejoinController.RejoinOffered).</summary>
    private Button rejoinButton;
    private float nextOfferCheckAt;
    private const float OfferCheckSeconds = 1f;

    void Start()
    {
        joinButton.interactable = false;
        joinButton.onClick.AddListener(OnJoinClicked);
        nameInput.onValueChanged.AddListener(OnNameChanged);
        NameScreenTips.Build((RectTransform)joinButton.transform, theme, nameInput.textComponent.font);
        BuildRejoinButton();
        // Task 9f: back on the name screen after a match the name is already there (PhotonNetwork.NickName survives the scene rebuild).
        if (!string.IsNullOrEmpty(PhotonNetwork.NickName))
            nameInput.text = PhotonNetwork.NickName;
        if (roomManager != null && roomManager.Rejoin != null)
            roomManager.Rejoin.ReturnToNameScreen += ShowNameScreenAgain;

        // Lobby Task 8: coming back from a match (or a spectator's Leave) skips the name step and goes to the lobby list. The list screen
        // is lobby Task 9; until then the name panel is simply hidden and the list is loaded (logged below).
        openListWhenConnected = LobbyReturn.Consume();
        if (openListWhenConnected)
        {
            if (joinUIPanel != null) joinUIPanel.SetActive(false);
            if (roomManager != null && roomManager.Lobbies != null)
                roomManager.Lobbies.ListChanged += LogBackOnTheList;
            if (PhotonNetwork.IsConnectedAndReady)
                EnterTheList();
        }
    }

    private bool openListWhenConnected;

    private void EnterTheList()
    {
        openListWhenConnected = false;
        if (roomManager != null && roomManager.Lobbies != null)
            roomManager.Lobbies.EnterList();
    }

    private void LogBackOnTheList()
    {
        if (roomManager == null || roomManager.Lobbies == null) return;
        roomManager.Lobbies.ListChanged -= LogBackOnTheList;
        Debug.Log($"[LOBBY] back on the list ({roomManager.Lobbies.Entries.Count} lobbies)");
    }

    void OnDestroy()
    {
        if (roomManager != null && roomManager.Rejoin != null)
            roomManager.Rejoin.ReturnToNameScreen -= ShowNameScreenAgain;
        if (roomManager != null && roomManager.Lobbies != null)
            roomManager.Lobbies.ListChanged -= LogBackOnTheList;
    }

    void Update()
    {
        if (rejoinButton == null || Time.unscaledTime < nextOfferCheckAt)
            return;
        nextOfferCheckAt = Time.unscaledTime + OfferCheckSeconds;

        bool offered = joinUIPanel != null && joinUIPanel.activeInHierarchy
                       && roomManager != null && roomManager.Rejoin != null && roomManager.Rejoin.RejoinOffered();
        if (rejoinButton.gameObject.activeSelf != offered)
            rejoinButton.gameObject.SetActive(offered);
    }

    /// <summary>A copy of the Join button (same look), moved above the name box and relabelled from UiTheme.</summary>
    void BuildRejoinButton()
    {
        if (theme == null || joinButton == null || nameInput == null)
            return;

        GameObject copy = Instantiate(joinButton.gameObject, joinButton.transform.parent);
        copy.name = "Rejoin your match";
        rejoinButton = copy.GetComponent<Button>();
        rejoinButton.onClick = new Button.ButtonClickedEvent(); // the copy must not inherit the Join button's own wiring
        rejoinButton.onClick.AddListener(OnRejoinClicked);
        rejoinButton.interactable = true;
        rejoinButton.GetComponent<Image>().color = theme.rejoinButtonColor;

        var rect = (RectTransform)copy.transform;
        var nameRect = (RectTransform)nameInput.transform;
        rect.sizeDelta = theme.rejoinMatchButtonSize;
        rect.anchoredPosition = new Vector2(0f,
            nameRect.anchoredPosition.y + nameRect.rect.height * nameRect.pivot.y + theme.rejoinButtonGapAboveNameBox
            + theme.rejoinMatchButtonSize.y * rect.pivot.y);

        TMP_Text label = copy.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = theme.rejoinMatchButton;
        copy.SetActive(false);
    }

    void OnRejoinClicked()
    {
        if (roomManager == null || roomManager.Rejoin == null)
            return;
        rejoinButton.gameObject.SetActive(false);
        roomManager.Rejoin.RejoinFromNameScreen();
    }

    /// <summary>Task 9e: the player gave up a rejoin (Leave, or OK on "the match is gone"): the name screen comes back for a
    /// normal join. The panel was hidden when they joined the match that has since dropped them.</summary>
    void ShowNameScreenAgain()
    {
        joinRequestedEarly = false;
        if (chatmanager != null) chatmanager.SetActive(false);
        if (chattext != null) chattext.SetActive(false);
        if (joinUIPanel != null) joinUIPanel.SetActive(true);
        joinButton.interactable = Regex.IsMatch(nameInput.text, NamePattern);
        nextOfferCheckAt = 0f;
    }

    /// At least four alphanumeric characters. Named so the disconnect path can re-check it
    /// instead of keeping a second copy of the pattern.
    private const string NamePattern = @"^[a-zA-Z0-9]{4,}$";

    void OnNameChanged(string playerName)
    {
        joinButton.interactable = Regex.IsMatch(playerName, NamePattern);
    }

    /// True when Join was pressed before Photon finished connecting, so the click can be
    /// replayed the moment it is safe to act on.
    private bool joinRequestedEarly = false;

    void OnJoinClicked()
    {
        // Task 9e-2: the saved match is kept here - if the server refuses this join because the old place is still held, the answer
        // is to rejoin it (RejoinController.TryRejoinHeldPlace); a successful new join saves its own match over it.
        PhotonNetwork.NickName = nameInput.text;
        joinButton.interactable = false;   // one join per click; re-enabled only if we never connect

        // JoinLobby() requires being on the Master Server, and the button used to become
        // clickable purely because the typed name was valid -- nothing waited for the connection.
        // That is a race, and Multiplayer Play Mode loses it reliably: the main editor client is
        // the window you are already looking at when play starts, so you type and click within a
        // second, before OnConnectedToMaster has fired. The call was dropped and nothing retried
        // it, so that client sat on the name screen forever while the virtual players -- which you
        // reach seconds later, long after they have connected -- worked fine.
        if (PhotonNetwork.IsConnectedAndReady)
        {
            roomManager.JoinGame();
        }
        else
        {
            joinRequestedEarly = true;
        }
    }

    public override void OnConnectedToMaster()
    {
        if (openListWhenConnected)
            EnterTheList();
        if (!joinRequestedEarly)
            return;

        joinRequestedEarly = false;
        roomManager.JoinGame();
    }

    public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
    {
        // Give the button back rather than stranding the player on a dead screen.
        joinRequestedEarly = false;
        if (joinButton != null)
            joinButton.interactable = Regex.IsMatch(nameInput.text, NamePattern);
    }

    public override void OnJoinedRoom()
    {
        if (joinUIPanel != null)
        {
            joinUIPanel.SetActive(false); // 🔥 Hide the UI
            chatmanager.SetActive(true);
            chattext.SetActive(true);
        }
    }
}
