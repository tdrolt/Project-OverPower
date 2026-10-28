using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.UI;

/// <summary>
/// What tells one player where they stand: waiting to be revived, respawning, you won, you lost. It lives on the
/// player prefab, not in the scene, because every message is addressed to one specific player; a scene-level HUD
/// would have to work out who it was for. Respawning and waiting are the death card (DeathCard, built on first use,
/// under the shop); the prefab's respawn and waiting panels stay inactive and are kept only because their objects
/// are referenced. YOU WIN / YOU LOSE are the prefab's panels, restyled to the card look at Awake (ResultPanelSkin).
///
/// This component only shows and hides. It never decides that someone died, how long a respawn
/// takes, or who won: PlayerLifecycle and MatchDirector own that and call in here.
///
/// Deliberately NOT IPunObservable - see PlayerNetSync.cs for why there can only be one.
/// </summary>
public class MatchUi : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField, Tooltip("Stays inactive: the death card's waiting card shows this state (dead, the team's capital lost).")]
    private GameObject waitingPanel;

    [SerializeField, Tooltip("Shown for the rest of the match when this player's team wins.")]
    private GameObject youWonPanel;

    [SerializeField, Tooltip("Stays inactive: the death card's RESPAWN IN countdown shows this state (an ordinary death).")]
    private GameObject respawnPanel;

    [SerializeField, Tooltip("Shown for the rest of the match when this player's team is eliminated.")]
    private GameObject youLostPanel;

    [Header("Capital under attack")]
    [SerializeField, Tooltip("Colours, fonts and texts of the death card, its capital-under-attack note and the " +
             "YOU WIN / YOU LOSE cards - the same theme asset PlayerHud reads for the HUD.")]
    private UiTheme theme;

    private PhotonView photonView;
    private Rigidbody rigidbody;
    private PlayerMotor playerMotor;

    private DeathCard deathCard;
    private bool waiting;

    /// <summary>True while this player is on the waiting card. PlayerLifecycle polls this as its "waiting for my
    /// capital back" flag: the one bool the card is shown from, so the two cannot disagree.</summary>
    public bool IsWaitingForRespawn => waiting;

    /// <summary>The death card, or null before this player's first death. Checks and captures read it.</summary>
    public DeathCard DeathCard => deathCard;

    /// <summary>True once this player has been shown a match result panel (win or lose). LoadoutScreen
    /// reads it: FreezeForRestOfMatch only stops movement and PlayerInputRouter's ShopSuppressed does not
    /// gate on the match being over, so without this a living player could keep re-picking a loadout after
    /// the result is decided.
    ///
    /// ONE-WAY LATCH: nothing sets the panels back to inactive, so once true it stays true for the rest of
    /// the match. A rematch or return-to-lobby-without-reloading path must reset these panels, and this
    /// property, explicitly.</summary>
    public bool MatchOver => dominionResultShown || (youWonPanel != null && youWonPanel.activeSelf) || (youLostPanel != null && youLostPanel.activeSelf);

    // In a Dominion match the result is DominionHud's result card, not the YOU WIN / YOU LOSE panels;
    // this latch stands in for them.
    private bool dominionResultShown;

    public UiTheme Theme => theme;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        rigidbody = GetComponent<Rigidbody>();
        playerMotor = GetComponent<PlayerMotor>();

        ResultPanelSkin.Apply(youWonPanel, theme, 1);
        ResultPanelSkin.Apply(youLostPanel, theme, 2); // Quit, and SpectateView's Spectate over it

        // A missing panel or theme is silent at runtime (every call is null-guarded so one broken
        // Inspector reference cannot throw mid-match), so say so once at spawn.
        if (waitingPanel == null || youWonPanel == null || respawnPanel == null || youLostPanel == null || theme == null)
            Debug.LogError($"[MatchUi] {name}: one or more match panels, or the UiTheme, are not " +
                            "assigned - this player will not be told when they win or lose, " +
                            "and the death card will draw in the default look.");
    }

    /// <summary>The death card, built the first time something is shown on it; never on a remote body.</summary>
    private DeathCard EnsureCard()
    {
        if (deathCard != null || (photonView != null && !photonView.IsMine))
            return deathCard;
        deathCard = gameObject.AddComponent<DeathCard>();
        deathCard.Init(theme);
        return deathCard;
    }

    /// <summary>Called by PlayerLifecycle during the respawn countdown: the death card's RESPAWN IN n.</summary>
    public void SetRespawnPanelVisible(bool visible)
    {
        if (visible)
            EnsureCard();
        deathCard?.SetRespawn(visible);
    }

    /// <summary>Called by PlayerLifecycle once this player is on their way back into the match.</summary>
    public void HideWaitingPanel()
    {
        waiting = false;
        deathCard?.SetWaiting(false);
    }

    /// <summary>The "your capital is under attack - you will respawn at your Tier 2 zone" line PlayerLifecycle
    /// polls onto the death card, under the countdown; empty text hides it.</summary>
    public void SetRespawnNote(string text)
    {
        if (deathCard == null && string.IsNullOrEmpty(text))
            return;
        EnsureCard()?.SetNote(text);
    }

    /// Shows the end-of-match result to this client, win or lose. The territory win condition needs
    /// it because the elimination path only ever announced the winner.
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

        // Dominion: the result card with the points-per-round table replaces the win / lose panels.
        if (Overpower.Dominion.DominionMode.IsActive() && Overpower.UI.DominionHud.Instance != null
            && Overpower.UI.DominionHud.Instance.ShowResult(winningTeam, BackToTheLobbyList))
        {
            dominionResultShown = true;
            FreezeForRestOfMatch();
            Overpower.Telemetry.MatchLogZip.Instance?.ZipNow();
            return;
        }

        if (myTeam == winningTeam)
            youWonPanel?.SetActive(true);
        else
            youLostPanel?.SetActive(true);

        FreezeForRestOfMatch();

        // This client's own match log, zipped alongside its result panel; calling it again on quit
        // (GameQuit.Quit) is safe, see MatchLogZip.
        Overpower.Telemetry.MatchLogZip.Instance?.ZipNow();
    }

    /// <summary>The Dominion result card's button: the same place the win / lose panels' button leads.</summary>
    private static void BackToTheLobbyList() => Object.FindFirstObjectByType<RoomManager>()?.ReturnToLobbyList();

    /// Stops the player moving once the match is decided, with a zero multiplier: PlayerMotor's speed
    /// is a product of keyed multipliers with no settable field.
    ///
    /// Deliberately no matching RemoveSpeedMultiplier: nothing should un-freeze a match-over player.
    /// The key is this component, so a respawn finishing late cannot lift this freeze with its own.
    private void FreezeForRestOfMatch()
    {
        if (rigidbody == null)
            return;

        rigidbody.linearVelocity = Vector3.zero;
        playerMotor.AddSpeedMultiplier(this, 0f);
    }

    // ---- RPCs (retired) -----------------------------------------------------------
    // Elimination and the match result are decided from replicated Room Properties (MatchDirector),
    // which every client including a late joiner reads, so none of these four is called any more; each
    // forwards to a plain local method MatchDirector calls directly. Inside a body that still runs,
    // "local" means the RECEIVER: every PhotonNetwork.LocalPlayer read is the player being told.
    //
    // None of these four names may be renamed or removed. PUN sends an index into the RpcList in
    // PhotonServerSettings.asset, a committed list of method NAMES, so a rename or removal
    // mis-dispatches every RPC listed after it on any client that already shipped.

    /// Kept only for the committed RpcList: MatchDirector writes mWin and every client reacts through
    /// ShowMatchResult instead.
    [PunRPC]
    public void RPC_ShowYouWonPanel(int teamID)
    {
        int myTeam = (int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey];

        Debug.Log("[MatchUi] Show Winning Panel Entered");

        if (myTeam == teamID)
        {
            youWonPanel.SetActive(true);
        }
    }

    /// Has no caller today - kept because the name is in the committed RpcList and an older client
    /// could still send it; it forwards to the local method, like the RPCs below. Delete the entry from
    /// PhotonServerSettings and this method together.
    [PunRPC]
    void RPC_HideWaitingPanelAll(int teamID)
    {
        if ((int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey] != teamID) return;
        HideWaitingPanel();
    }

    /// <summary>Shows the "waiting for a teammate to retake the capital" panel, locally. PlayerLifecycle
    /// calls it the instant a death becomes a last-stand death. The RPC below only forwards here.</summary>
    public void ShowWaitingPanel()
    {
        Debug.Log("[MatchUi] Show Waiting Panel Entered");

        // Never over "you lost" (elimination outranks waiting for a respawn that is not coming) nor
        // "you won": MatchOver covers both.
        if (!waiting && !MatchOver)
        {
            waiting = true;
            EnsureCard()?.SetWaiting(true);
            Debug.Log("[MatchUi] (local) Showing the waiting card.");
        }
    }

    /// Kept only for the committed RpcList: an older client could still send it, so the body stays,
    /// forwarding to the local method above.
    [PunRPC]
    void RPC_ShowWaitingPanel(int teamID)
    {
        if ((int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey] != teamID) return;
        ShowWaitingPanel();
    }

    /// <summary>Shows the team-eliminated panel, locally. MatchDirector calls it on each newly-eliminated
    /// team's own client once elimination is decided from replicated state (mElim). The RPC below only
    /// forwards here.</summary>
    public void ShowYouLost()
    {
        Debug.Log("[MatchUi] ShowYouLost running");

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");

        HideWaitingPanel();

        if (youLostPanel != null && !IsWaitingForRespawn)
        {
            youLostPanel.SetActive(true);
            Debug.Log("[MatchUi] youLostPanel.SetActive(true) called");
        }

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");

        // StartCoroutine(DelayedShowLose());

        FreezeForRestOfMatch();
        EnsureSpectateView();
    }

    /// <summary>The Spectate button on this player's own lose panel, built the first time it is shown.</summary>
    private void EnsureSpectateView()
    {
        if (youLostPanel == null || photonView == null || !photonView.IsMine || GetComponent<SpectateView>() != null)
            return;
        gameObject.AddComponent<SpectateView>().Init(this, youLostPanel);
    }

    /// Kept only for the committed RpcList: an older client could still send it, so the body stays,
    /// forwarding to the local method above.
    [PunRPC]
    void RPC_ShowYouLostPanel(int teamID)
    {
        if ((int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey] != teamID) return;
        ShowYouLost();
    }

    /// Unreachable: its only call site is the commented-out line above. The lose panel sometimes lost a
    /// race with the waiting panel being hidden in the same frame; a short delay was the workaround.
    /// Kept in case "you lost" ever fails to appear again; delete both it and the commented call if
    /// it is not needed.
    IEnumerator DelayedShowLose()
    {
        yield return new WaitForSeconds(0.1f);
        if (youLostPanel != null && !waitingPanel.activeSelf)
        {
            youLostPanel.SetActive(true);
            Debug.Log("[MatchUi] youLostPanel.SetActive(true) called");
        }

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");
    }
}
