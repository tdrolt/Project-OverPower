using Photon.Pun;
using UnityEngine;

/// <summary>
/// The one real "close the game" path, shared by QuitButton.Quit and the Escape pop-up's Yes (QuitConfirmPanel).
///
/// Order matters: it zips THIS client's own match log BEFORE disconnecting or quitting, instead of relying on
/// MatchTelemetry's BeforeClose/OnApplicationQuit sequence still having time to zip. MatchLogZip's zip step calls
/// MatchTelemetry.FlushNow(), which writes the buffered lines synchronously, so the zip is guaranteed to contain them.
///
/// Always calls ZipNow(), never a "skip if already zipped" variant: Disconnect() below can raise
/// MatchTelemetry.OnLeftRoom (clearing CurrentFolder) before this client's own OnApplicationQuit re-zip runs.
/// MatchLogZipRule.ZipFileName names the zip after the match folder, which never changes for the match, so a second
/// ZipNow() overwrites the same file (see MatchLogZip: ZipNow, HandleBeforeClose, MatchLogZipRule.ResolveZipFolder).
/// </summary>
public static class GameQuit
{
    public static void Quit()
    {
        Overpower.Telemetry.MatchLogZip.Instance?.ZipNow();

        // Quitting through the menu gives the seat up at once (D-b): LeaveRoom(false) is a real leave, not the
        // "inactive, may come back" a bare Disconnect leaves for the rejoin window (that is for crashes and lost
        // connections), and it forgets the saved match so the next start offers no rejoin. Leave first and flush it, then disconnect.
        Overpower.Net.PlayerIdentity.ClearLastMatch();
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom(becomeInactive: false);
            PhotonNetwork.SendAllOutgoingCommands();
        }
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
