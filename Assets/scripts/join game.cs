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

    void OnNameChanged(string playerName)
    {
        bool isValid = Regex.IsMatch(playerName, @"^[a-zA-Z0-9]{4,}$");
        joinButton.interactable = isValid;
    }

    void OnJoinClicked()
    {
        PhotonNetwork.NickName = nameInput.text;
        roomManager.JoinGame();
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
