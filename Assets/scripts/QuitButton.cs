using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quits the game when its Button is clicked.
///
/// Put this on a Button. It wires itself, so there is no inspector reference to forget -- which
/// matters here because the three buttons this replaces sat on the win, lose and waiting panels
/// for months with **no onClick handler at all**. They looked like working buttons and did
/// nothing.
///
/// These were labelled "Main Menu", but there is no main menu scene to return to. Quitting is the
/// honest version of what that button can currently do; a real menu is future work.
/// </summary>
[RequireComponent(typeof(Button))]
public class QuitButton : MonoBehaviour
{
    void Start()
    {
        GetComponent<Button>().onClick.AddListener(Quit);
    }

    public void Quit()
    {
        // Leave the room first so the other players see us go immediately, instead of a ghost
        // player standing in the arena until Photon's timeout expires.
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
