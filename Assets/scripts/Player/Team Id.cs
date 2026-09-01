using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class PlayerTeam : MonoBehaviourPun
{
    public int teamID = 0;

    [PunRPC]
    public void RPC_SetTeamID(int newTeamID)
    {
        teamID = newTeamID;
        Debug.Log($"[RPC_SetTeamID] TeamID updated via RPC: {teamID}");
    }

}