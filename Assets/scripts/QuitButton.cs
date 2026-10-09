using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;
using TMPro;

/// <summary>
/// Quits the game when its Button is clicked. Put this on a Button; it wires itself, so there is no inspector
/// reference to forget (the win, lose and waiting panel buttons it replaces sat for months with **no onClick
/// handler at all**: they looked like working buttons and did nothing).
///
/// There is no main menu scene to return to, so Quit is the honest action. The sequence lives in GameQuit.Quit()
/// (also used by the Escape pop-up's Yes); Quit() stays public because the win/lose/waiting panel buttons are
/// wired to it by name.
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

    /// <summary>Once the MATCH is over (the room's phase) this leads back to the lobby list for a fresh start; on the
    /// waiting panel, or on a knocked-out player's lose panel while the match still runs, it closes the game (D22).</summary>
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

    public static string LabelFor(bool matchOver, MatchPhase phase, string lobbyListLabel, string quitLabel) =>
        BackToNameScreenRules.ButtonLabel(matchOver ? BackToNameScreenRules.ButtonAction(phase) : ResultButtonAction.CloseGame, lobbyListLabel, quitLabel);

    private static ResultButtonAction CurrentAction()
    {
        MatchDirector director = MatchDirector.Instance;
        return BackToNameScreenRules.ButtonAction(director != null ? director.Phase : MatchPhase.Warmup);
    }

    /// <summary>The label follows what the button will do; the wording comes from UiTheme.</summary>
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
