using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;
using Photon.Pun;
using TMPro;


public class JoinGameUI : MonoBehaviourPunCallbacks
{
    public TMP_InputField nameInput;
    public Button joinButton;
    public GameObject joinUIPanel; // Parent UI panel to hide
    public GameObject chatmanager;
    public GameObject chattext;
    public RoomManager roomManager;

    void Start()
    {
        joinButton.interactable = false;
        joinButton.onClick.AddListener(OnJoinClicked);
        nameInput.onValueChanged.AddListener(OnNameChanged);
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
