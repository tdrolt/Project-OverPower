using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;

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
///
/// Playtest extras P6 (2026-09-26): the actual quit sequence now lives in the shared GameQuit.Quit()
/// (also used by the Escape pop-up's Yes, QuitConfirmPanel), so both zip this client's own match log
/// first - see GameQuit's own class comment for the order. Kept as a public Quit() method (not
/// renamed/removed) since the win/lose/waiting panel buttons are already wired to it by name.
/// </summary>
[RequireComponent(typeof(Button))]
public class QuitButton : MonoBehaviour
{
    void Start()
    {
        GetComponent<Button>().onClick.AddListener(Quit);
    }

    /// <summary>Task 9f (Tudor D22): once the match is over this leads back to the name screen for a fresh start; on the mid-match
    /// waiting panel it still closes the game.</summary>
    public void Quit()
    {
        MatchUI ui = GetComponentInParent<MatchUI>();
        bool matchOver = ui != null && ui.MatchOver;
        RoomManager manager = matchOver ? FindFirstObjectByType<RoomManager>() : null;
        if (BackToNameScreenRules.ButtonAction(matchOver) == ResultButtonAction.BackToNameScreen && manager != null)
            manager.ReturnToNameScreen();
        else
            GameQuit.Quit();
    }
}
