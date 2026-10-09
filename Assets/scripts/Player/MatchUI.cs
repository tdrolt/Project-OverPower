using System.Collections;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.UI;

/// <summary>
/// The four full-screen panels that tell one player where they stand: waiting to be revived,
/// respawning, you won, you lost. They live on the player prefab, not in the scene, because every
/// message is addressed to one specific player; a scene-level HUD would have to work out who it was for.
///
/// This component only shows and hides panels. It never decides that someone died, how long a respawn
/// takes, or who won: PlayerLifecycle and MatchDirector own that and call in here.
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

    [Header("Capital under attack (Tudor, 2026-09-16)")]
    [SerializeField, Tooltip("Colours/font/text the respawn panel's under-attack note is styled from - the " +
             "same theme asset PlayerHud reads for the HUD.")]
    private UiTheme theme;

    private PhotonView photonView;
    private Rigidbody rigidbody;
    private PlayerMotor playerMotor;

    // Built lazily on the first SetRespawnNote call. respawnNoteGo is the note's OWN root (backing strip +
    // text child) and what SetActive toggles, the same "the root, not a child" rule as PlayerHud.ShowToast:
    // toggling the text alone would leave a blank backing strip on the panel.
    private GameObject respawnNoteGo;
    private TextMeshProUGUI respawnNoteText;

    /// <summary>True while this player is stuck on the waiting panel. PlayerLifecycle polls this as its
    /// "waiting for my capital back" flag: the visible panel is the single source of truth, not a second
    /// bool that could disagree.</summary>
    public bool IsWaitingForRespawn => waitingPanel != null && waitingPanel.activeSelf;

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

    private const string WaitingTitleObjectName = "Waiting";

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        rigidbody = GetComponent<Rigidbody>();
        playerMotor = GetComponent<PlayerMotor>();

        // A missing panel or theme is silent at runtime (every call is null-guarded so one broken
        // Inspector reference cannot throw mid-match, and BuildRespawnNoteLabel falls back to plain
        // white), so say so once at spawn.
        // The waiting text lives on the UiTheme (one home for texts), set here, not baked into the prefab.
        if (waitingPanel != null && theme != null)
        {
            // The title is the child named "Waiting"; the QUIT button's TMP text is a child too, so a
            // bare GetComponentInChildren would be order-dependent.
            TMP_Text waitingText = null;
            foreach (TMP_Text candidate in waitingPanel.GetComponentsInChildren<TMP_Text>(true))
                if (candidate.gameObject.name == WaitingTitleObjectName) { waitingText = candidate; break; }
            if (waitingText != null)
                waitingText.text = theme.waitingPanelText;
            else
                Debug.LogError($"[MatchUI] {name}: the waiting panel has no text object named \"{WaitingTitleObjectName}\" - its title stays as the prefab has it.");
        }

        if (waitingPanel == null || youWonPanel == null || respawnPanel == null || youLostPanel == null || theme == null)
            Debug.LogError($"[MatchUI] {name}: one or more match panels, or the UiTheme, are not " +
                            "assigned - this player will not be told when they win, lose or respawn, " +
                            "and the capital-under-attack respawn note will render unstyled.");
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

    /// <summary>The "your capital is under attack - you will respawn at your Tier 2 zone" line PlayerLifecycle
    /// polls onto the respawn panel. Built lazily under respawnPanel, below its "Respawning! Please Wait!"
    /// label; hides itself (SetActive on respawnNoteGo, the note's root) whenever text is empty.</summary>
    public void SetRespawnNote(string text)
    {
        if (respawnPanel == null)
            return; // Awake already logged it

        if (respawnNoteGo == null)
            BuildRespawnNoteLabel();

        respawnNoteText.text = text ?? "";
        respawnNoteGo.SetActive(!string.IsNullOrEmpty(text));
    }

    /// <summary>PlayerHud.AddLabel/ApplyOutline's recipe (font/colour from UiTheme, one outline material) plus
    /// a dark backing strip, because plain outlined white text read too faint against the respawn panel's pale
    /// salmon wash. Duplicated rather than shared: the two components have no other coupling.</summary>
    private void BuildRespawnNoteLabel()
    {
        respawnNoteGo = new GameObject("Under Attack Note", typeof(RectTransform));
        respawnNoteGo.transform.SetParent(respawnPanel.transform, false);

        RectTransform rootRt = respawnNoteGo.GetComponent<RectTransform>();
        // The panel's "Respawning! Please Wait!" sits at anchoredPosition (0, 150); this sits below it,
        // inside the panel's -80/-80 stretch margin at the tested 616x576 Game view.
        rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = new Vector2(0f, 60f);
        rootRt.sizeDelta = new Vector2(500f, 90f);

        Image backing = respawnNoteGo.AddComponent<Image>();
        backing.color = theme != null ? theme.capitalUnderAttackNoteBackingColor : new Color(0f, 0f, 0f, 0.6f);
        backing.raycastTarget = false;

        GameObject textGo = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
        textGo.name = "Text";
        textGo.transform.SetParent(respawnNoteGo.transform, false);
        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(10f, 6f);
        textRt.offsetMax = new Vector2(-10f, -6f);

        respawnNoteText = textGo.GetComponent<TextMeshProUGUI>();
        if (theme != null && theme.font != null)
            respawnNoteText.font = theme.font;
        // Its own size/colour, not Body Text Size/Text Colour: those are tuned for the HUD's dark Panel
        // Colour backing, and this note must read at a glance on the respawn panel.
        respawnNoteText.fontSize = theme != null ? theme.capitalUnderAttackNoteFontSize : 30f;
        respawnNoteText.color = theme != null ? theme.capitalUnderAttackNoteColor : Color.white;
        respawnNoteText.alignment = TextAlignmentOptions.Center;
        respawnNoteText.enableWordWrapping = true;
        respawnNoteText.raycastTarget = false;

        if (theme != null)
        {
            // Font before fontSharedMaterial: assigning .font switches fontSharedMaterial to that font
            // asset's default, which is the template cloned here (as PlayerHud.AddLabel).
            Material outlineMaterial = new Material(respawnNoteText.fontSharedMaterial);
            outlineMaterial.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, theme.textOutlineWidth);
            outlineMaterial.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, theme.textOutlineColor);
            respawnNoteText.fontSharedMaterial = outlineMaterial;
        }

        respawnNoteGo.SetActive(false); // SetRespawnNote shows/hides it from here on
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

    /// <summary>Shows the "waiting for a teammate to retake the capital" panel, locally. PlayerLifecycle
    /// calls it the instant a death becomes a last-stand death. The RPC below only forwards here.</summary>
    public void ShowWaitingPanel()
    {
        Debug.Log("[MatchUI] Show Waiting Panel Entered");

        // Never over "you lost" (elimination outranks waiting for a respawn that is not coming) nor
        // "you won": MatchOver covers both.
        if (waitingPanel != null && !waitingPanel.activeSelf && !MatchOver)
        {
            waitingPanel.SetActive(true);
            Debug.Log("[MatchUI] (local) Showing Waiting panel.");
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
        Debug.Log("[MatchUI] ShowYouLost running");

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
            Debug.Log("[MatchUI] youLostPanel.SetActive(true) called");
        }

        Debug.Log($"waitingPanel: {(waitingPanel == null ? "null" : waitingPanel.name)}, activeInHierarchy: {waitingPanel?.activeInHierarchy}");
        Debug.Log($"youLostPanel: {(youLostPanel == null ? "null" : youLostPanel.name)}, activeInHierarchy: {youLostPanel?.activeInHierarchy}");
    }
}
