using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro; // or UnityEngine.UI depending on your setup

public class TeamSyncHandler : MonoBehaviour, IInRoomCallbacks
{
    private PhotonView photonView;
    private PlayerTeam playerTeam;
    private Multiplayer multiplayer;

    void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerTeam = GetComponent<PlayerTeam>();
        multiplayer = GetComponent<Multiplayer>();
    }

    void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        // If *someone else's* teamID changed, we may need to update our perception of them
        if (!photonView.IsMine && changedProps.ContainsKey("teamID"))
        {
            UpdateNameTagColor();
        }
    }

    void UpdateNameTagColor()
    {
        if (multiplayer == null || multiplayer.playerNameText == null)
            return;

        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("teamID", out object localTeamObj) &&
            playerTeam != null)
        {
            int localTeamID = (int)localTeamObj;
            if (playerTeam.teamID == localTeamID)
                multiplayer.playerNameText.color = Color.green;
            else
                multiplayer.playerNameText.color = Color.red;
        }
    }

    // Required interface methods (unused)
    public void OnPlayerEnteredRoom(Player newPlayer) { }
    public void OnPlayerLeftRoom(Player otherPlayer) { }
    public void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged) { }
    public void OnMasterClientSwitched(Player newMasterClient) { }
}
