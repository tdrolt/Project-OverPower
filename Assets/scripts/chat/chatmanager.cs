using System.Collections;
using UnityEngine;
using Photon.Chat;
using ExitGames.Client.Photon;
using Photon.Pun;
using TMPro;

public class PhotonChat : MonoBehaviour, IChatClientListener
{
    ChatClient chatClient;
    bool isConnected;
    string privateReceiver = "";

    [SerializeField] GameObject chatPanel;
    [SerializeField] GameObject text;
    [SerializeField] TMP_InputField chatField;
    [SerializeField] TextMeshProUGUI chatDisplay;

    void Start()
    {
        ConnectToChat();
    }

   private void ConnectToChat()
{
    isConnected = true;
    chatClient = new ChatClient(this);
    
    // Use the player's Photon nickname as the username for the chat
    chatClient.Connect(PhotonNetwork.PhotonServerSettings.AppSettings.AppIdChat, PhotonNetwork.AppVersion, 
        new AuthenticationValues(PhotonNetwork.LocalPlayer.NickName));
    
    Debug.Log("Connecting to chat...");
}


    public void DebugReturn(DebugLevel level, string message)
    {
        Debug.Log(message);
    }

    public void OnChatStateChange(ChatState state)
    {
        Debug.Log("Chat state changed to: " + state);
    }

    public void OnConnected()
    {
        Debug.Log("Connected to chat");
        SubToChatOnConnect();
    }

    public void OnDisconnected()
    {
        Debug.Log("Disconnected from chat");
    }

    public void OnGetMessages(string channelName, string[] senders, object[] messages)
    {
        for (int i = 0; i < senders.Length; i++)
        {
            string msg = string.Format("{0}: {1}", senders[i], messages[i]);
            chatDisplay.text += "\n" + msg; // Display the received public message
        }
    }

    public void OnPrivateMessage(string sender, object message, string channelName)
    {
        string privateMsg = string.Format("{0} (private to {1}): {2}", sender, privateReceiver, message);
        chatDisplay.text += "\n" + privateMsg; // Display the private message
    }

    public void OnStatusUpdate(string user, int status, bool gotMessage, object message)
    {
        Debug.Log($"{user} is now {status}");
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
        Debug.Log($"{user} has subscribed to {channel}");
    }

    public void OnUserUnsubscribed(string channel, string user)
    {
        Debug.Log($"{user} has unsubscribed from {channel}");
    }

    private void SubToChatOnConnect()
    {
        chatClient.Subscribe(new string[] { "RegionChannel" });
    }

    public void SubmitPublicChatOnClick()
    {
        if (!string.IsNullOrEmpty(chatField.text))
        {
            chatClient.PublishMessage("RegionChannel", chatField.text); // Send to public chat
            chatField.text = ""; // Clear input field after sending
        }
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
    if (isConnected)
    {
        chatClient.Service();
    }

    // Toggle chat panel visibility on Enter key press
    if (Input.GetKeyDown(KeyCode.Return))
    {
        // If the chat is closed, open it and focus on the input field
        if (!chatPanel.activeSelf)
        {
            text.SetActive(false);
            chatPanel.SetActive(true);
            chatField.Select();  // Focus on the chat input field
            chatField.ActivateInputField();  // Make sure the input field is active
        }
        else
        {
            // If the chat is already open, send the message
            SubmitPublicChatOnClick();
        }
    }

    // Close the chat panel on Escape key press
    if (Input.GetKeyDown(KeyCode.Escape))
    {
        if (chatPanel.activeSelf)
        {
            chatPanel.SetActive(false);
            text.SetActive(true);  // Show the text when chat is closed
            chatField.DeactivateInputField();  // Deactivate the input field when closing the chat
        }
    }

    // Submit chat on Enter key press if the chat panel is active
    if (Input.GetKeyDown(KeyCode.Return) && chatPanel.activeSelf)
    {
        if (!string.IsNullOrEmpty(chatField.text))
        {
            SubmitPublicChatOnClick();
            chatField.text = "";  // Clear input field after sending
        }
    }
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
