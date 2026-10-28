using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;
using TMPro;

/// <summary>
/// The win, lose, waiting and death-card button: Leave match while the match runs, Back to the lobby list once it is over; both
/// go through RoomManager.ReturnToLobbyList (BackToNameScreenRules). It wires itself in Start (AddListener), so there is no
/// inspector reference to forget: those buttons once sat for months with no onClick handler at all. GameQuit.Quit is only the
/// fallback when no RoomManager is found.
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

    public void Quit()
    {
        if (BackToNameScreenRules.LeadsToLobbyList(CurrentAction()))
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

    public static string LabelFor(bool matchOver, MatchPhase phase, string lobbyListLabel, string leaveLabel) =>
        BackToNameScreenRules.ButtonLabel(matchOver ? BackToNameScreenRules.ButtonAction(phase) : ResultButtonAction.LeaveMatch, lobbyListLabel, leaveLabel);

    private static ResultButtonAction CurrentAction()
    {
        MatchDirector director = MatchDirector.Instance;
        return BackToNameScreenRules.ButtonAction(director != null ? director.Phase : MatchPhase.Warmup);
    }

    /// <summary>The label follows what the button will do; the wording comes from UiTheme.</summary>
    void Update()
    {
        MatchUi ui = GetComponentInParent<MatchUi>();
        if (ui == null || ui.Theme == null)
            return;
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
        if (label == null)
            return;
        MatchDirector director = MatchDirector.Instance;
        string wanted = LabelFor(ui.MatchOver, director != null ? director.Phase : MatchPhase.Warmup,
            ui.Theme.resultButtonLobbyList, ui.Theme.resultButtonLeaveMatch);
        if (label.text != wanted)
            label.text = wanted;
    }
}
