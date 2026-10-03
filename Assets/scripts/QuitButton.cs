using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;
using TMPro;

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

    private TMP_Text label;
    private RoomManager manager;

    /// <summary>Task 9f (Tudor D22): once the MATCH is over (the room's phase) this leads back to the lobby list for a fresh start; on the
    /// waiting panel, or on a knocked-out player's lose panel while the match still runs, it closes the game.</summary>
    public void Quit()
    {
        if (CurrentAction() == ResultButtonAction.BackToLobbyList)
        {
            if (manager == null)
                manager = FindFirstObjectByType<RoomManager>();
            if (manager != null)
            {
                manager.ReturnToLobbyList();
                return;
            }
        }
        GameQuit.Quit();
    }

    /// <summary>The label the button shows: the rule's label for the action it will take - "Back to the lobby list" once the match is over (and this
    /// is the match-over panel), else "Quit".</summary>
    public static string LabelFor(bool matchOver, MatchPhase phase, string lobbyListLabel, string quitLabel) =>
        BackToNameScreenRules.ButtonLabel(matchOver ? BackToNameScreenRules.ButtonAction(phase) : ResultButtonAction.CloseGame, lobbyListLabel, quitLabel);

    private static ResultButtonAction CurrentAction()
    {
        MatchDirector director = MatchDirector.Instance;
        return BackToNameScreenRules.ButtonAction(director != null ? director.Phase : MatchPhase.Warmup);
    }

    /// <summary>The label follows what the button will do (UiTheme texts): "Back to the lobby list" once the match is over, else "Quit".</summary>
    void Update()
    {
        MatchUI ui = GetComponentInParent<MatchUI>();
        if (ui == null || ui.Theme == null)
            return;
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
        if (label == null)
            return;
        MatchDirector director = MatchDirector.Instance;
        string wanted = LabelFor(ui.MatchOver, director != null ? director.Phase : MatchPhase.Warmup,
            ui.Theme.resultButtonLobbyList, ui.Theme.resultButtonQuit);
        if (label.text != wanted)
            label.text = wanted;
    }
}
