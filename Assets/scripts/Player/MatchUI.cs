using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// The four full-screen panels that tell one player where they stand in the match: waiting to be
/// revived, respawning, you won, you lost. Split out of Multiplayer.cs (Task 0.11b) alongside
/// PlayerLifecycle (death and respawn) and PlayerNameTag (the floating name).
///
/// The panels live on the player prefab rather than in the scene because every message here is
/// addressed to one specific player - "your team is out", "you are waiting" - and the master client
/// delivers them by calling an RPC on that player's own PhotonView. A scene-level HUD would have to
/// work out who the message was for; this does not.
///
/// This component only shows and hides panels. It never decides that someone died, how long a
/// respawn takes, or who won: PlayerLifecycle owns that and calls in here, and the master client's
/// elimination bookkeeping reaches the panels through the RPCs below.
///
/// Deliberately NOT IPunObservable - see PlayerNetSync.cs for why there can only be one.
/// </summary>
public class MatchUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField, Tooltip("Shown while this player is dead and their team has lost its capital, " +
             "so they are waiting for a teammate to recapture it before they can respawn.")]
    private GameObject waitingPanel;

    [SerializeField, Tooltip("Shown for the rest of the match when this player's team wins.")]
    private GameObject youWonPanel;

    [SerializeField, Tooltip("Shown during the respawn countdown after an ordinary death.")]
    private GameObject respawnPanel;

    [SerializeField, Tooltip("Shown for the rest of the match when this player's team is eliminated.")]
    private GameObject youLostPanel;

    private PhotonView photonView;
    private Rigidbody rigidbody;
    private PlayerMotor playerMotor;

    /// <summary>True while this player is stuck on the waiting panel. PlayerLifecycle polls this as
    /// its "am I waiting for my capital back" flag: the panel the player can actually see is the
    /// single source of truth for that, rather than a second bool that could disagree with it.</summary>
    public bool IsWaitingForRespawn => waitingPanel != null && waitingPanel.activeSelf;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        rigidbody = GetComponent<Rigidbody>();
        playerMotor = GetComponent<PlayerMotor>();

        // A missing panel is silent at runtime - every call below is null-guarded so one broken
        // Inspector reference cannot throw mid-match - so say so once at spawn instead. Three UI
        // buttons sat broken for the life of this project because nothing ever complained.
        if (waitingPanel == null || youWonPanel == null || respawnPanel == null || youLostPanel == null)
            Debug.LogError($"[MatchUI] {name}: one or more match panels are not assigned - " +
                            "this player will not be told when they win, lose or respawn.");
    }

    /// <summary>Called by PlayerLifecycle during the respawn countdown.</summary>
    public void SetRespawnPanelVisible(bool visible)
    {
        if (respawnPanel != null)
            respawnPanel.SetActive(visible);
    }

    /// <summary>Called by PlayerLifecycle once this player is on their way back into the match.</summary>
    public void HideWaitingPanel()
    {
        if (waitingPanel != null)
            waitingPanel.SetActive(false);
    }

    /// Shows the end-of-match result to this client, win or lose. Used by the territory win
    /// condition, which needs to tell losers as well -- the elimination path only ever announced
    /// the winner, so everyone else was left with no screen at all.
    public void ShowMatchResult(int winningTeam)
    {
        if (!photonView.IsMine)
            return;

        int myTeam = PlayerTeam.NoTeam;
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(PlayerTeam.TeamKey, out object raw)
            && raw is int value)
        {
            myTeam = value;
        }

        HideWaitingPanel();
        SetRespawnPanelVisible(false);

        if (myTeam == winningTeam)
            youWonPanel?.SetActive(true);
        else
            youLostPanel?.SetActive(true);

        FreezeForRestOfMatch();
    }

    /// Stops the player moving once the match is decided. Adds a zero multiplier rather than
    /// writing a speed value: PlayerMotor's speed is a product of keyed multipliers and has no
    /// settable field, and the previous version of this code assigned a raw speed field that
    /// PlayerMotor had stopped reading - a dead write that let a match-over player keep sliding
    /// around (Task 0.11a defect 1).
    ///
    /// There is deliberately no matching RemoveSpeedMultiplier: the match is over for this player
    /// and nothing should un-freeze them. The key is this component, so a respawn finishing late
    /// cannot lift this freeze when it lifts its own.
    private void FreezeForRestOfMatch()
    {
        if (rigidbody == null)
            return;

        rigidbody.linearVelocity = Vector3.zero;
        playerMotor.AddSpeedMultiplier(this, 0f);
    }

    // ---- RPCs -----------------------------------------------------------------------------
    // Sent by the master client's elimination bookkeeping in PlayerLifecycle, and targeted at one
    // player's own PhotonView. Inside these bodies "local" means the RECEIVER: every
    // PhotonNetwork.LocalPlayer read below is the player being told, not the master client that
    // sent it. The teamID the sender meant therefore has to travel as a parameter.
    //
    // None of these four names may be renamed. PUN sends an index into the RpcList in
    // PhotonServerSettings.asset, which is a committed list of method NAMES, so a rename
    // mis-dispatches on every client that already shipped. Moving them between files is safe.

    [PunRPC]
    public void RPC_ShowYouWonPanel(int teamID)
    {
        int myTeam = (int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey];

        Debug.Log("[MatchUI] Show Winning Panel Entered");

        if (myTeam == teamID)
        {
            youWonPanel.SetActive(true);
        }
    }

    /// Has no caller today - kept because the name is in the committed RpcList and an older client
    /// could still send it. Delete the entry from PhotonServerSettings and this method together.
    [PunRPC]
    void RPC_HideWaitingPanelAll(int teamID)
    {
        int localTeamID = (int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey];
        if (teamID != localTeamID) return;

        Debug.Log("[MatchUI] Hiding Waiting Panel Entered");

        if (waitingPanel != null && waitingPanel.activeSelf)
        {
            waitingPanel.SetActive(false);
            Debug.Log($"[MatchUI] (RPC) Hiding Waiting for Team {teamID}.");
        }
    }

    [PunRPC]
    void RPC_ShowWaitingPanel(int teamID)
    {
        int localTeamID = (int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey];
        if (teamID != localTeamID) return;

        Debug.Log("[MatchUI] Show Waiting Panel Entered");

        // Never over the top of "you lost": being eliminated outranks waiting for a respawn that
        // is no longer coming.
        if (waitingPanel != null && !waitingPanel.activeSelf && !youLostPanel.activeSelf)
        {
            waitingPanel.SetActive(true);
            Debug.Log($"[MatchUI] (RPC) Showing Waiting panel for player on Team {teamID}.");
        }
    }

    [PunRPC]
    void RPC_ShowYouLostPanel(int teamID)
    {
        if ((int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey] != teamID) return;

        Debug.Log("[MatchUI] RPC_ShowYouLostPanel running");

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");

        if (waitingPanel != null)
        {
            waitingPanel.SetActive(false);
            Debug.Log("[MatchUI] waitingPanel.SetActive(false) called");
        }

        if (youLostPanel != null && !waitingPanel.activeSelf)
        {
            youLostPanel.SetActive(true);
            Debug.Log("[MatchUI] youLostPanel.SetActive(true) called");
        }

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");

        // StartCoroutine(DelayedShowLose());

        FreezeForRestOfMatch();
    }

    /// Currently unreachable: its only call site is the commented-out line above. It exists because
    /// the panel above sometimes lost a race with the waiting panel being hidden in the same frame,
    /// and a frame's delay was the workaround. Kept rather than deleted so that history is visible
    /// if "you lost" ever fails to appear again - but if it has not been needed by the next
    /// playtest, delete both it and the commented call.
    IEnumerator DelayedShowLose()
    {
        yield return new WaitForSeconds(0.1f);
        if (youLostPanel != null && !waitingPanel.activeSelf)
        {
            youLostPanel.SetActive(true);
            Debug.Log("[MatchUI] youLostPanel.SetActive(true) called");
        }

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");
    }
}
