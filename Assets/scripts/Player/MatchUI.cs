using System.Collections;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.UI;

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

    [Header("Capital under attack (Tudor, 2026-09-16)")]
    [SerializeField, Tooltip("Colours/font/text the respawn panel's under-attack note is styled from - the " +
             "same theme asset PlayerHud reads for the HUD.")]
    private UiTheme theme;

    private PhotonView photonView;
    private Rigidbody rigidbody;
    private PlayerMotor playerMotor;

    // Built lazily on the first SetRespawnNote call - see BuildRespawnNoteLabel. respawnNoteGo is the
    // note's OWN root (backing strip + text child) - what SetActive actually toggles, the same "the
    // root, not a child" rule PlayerHud.ShowToast follows for its own toast (toggling the text alone
    // would leave a blank backing strip floating on the panel whenever the note has nothing to say).
    private GameObject respawnNoteGo;
    private TextMeshProUGUI respawnNoteText;

    /// <summary>True while this player is stuck on the waiting panel. PlayerLifecycle polls this as
    /// its "am I waiting for my capital back" flag: the panel the player can actually see is the
    /// single source of truth for that, rather than a second bool that could disagree with it.</summary>
    public bool IsWaitingForRespawn => waitingPanel != null && waitingPanel.activeSelf;

    /// <summary>True once this player has been shown a match result panel (win or lose). Added for
    /// LoadoutScreen (Task 9a review): FreezeForRestOfMatch only stops movement, and
    /// PlayerInputRouter's ShopSuppressed deliberately does not gate on the match being over (it
    /// only checks alive/typing), so without this a still-living player could open the loadout
    /// screen and keep re-picking a loadout after the result is already decided. Same
    /// panel-is-the-source-of-truth reasoning as IsWaitingForRespawn above.
    ///
    /// ONE-WAY LATCH (Task 9b quality review): nothing ever sets youWonPanel/youLostPanel back to
    /// inactive, so once true this stays true for the rest of the match - fine today because there
    /// is no rematch/new-match-in-place flow, a match ending is the last thing that happens on this
    /// player object before the scene changes or the room closes. Phase 2's match loop (if it adds a
    /// rematch or a return-to-lobby-without-reloading path) will need to reset these panels, and this
    /// property, explicitly when that happens - it will not do so on its own.</summary>
    public bool MatchOver => (youWonPanel != null && youWonPanel.activeSelf) || (youLostPanel != null && youLostPanel.activeSelf);

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        rigidbody = GetComponent<Rigidbody>();
        playerMotor = GetComponent<PlayerMotor>();

        // A missing panel (or theme - B3 review, 2026-09-16: joins the same check rather than its own
        // separate log line, since a missing theme is just as silent - BuildRespawnNoteLabel falls back
        // to plain white/no backing) is silent at runtime - every call below is null-guarded so one
        // broken Inspector reference cannot throw mid-match - so say so once at spawn instead. Three UI
        // buttons sat broken for the life of this project because nothing ever complained.
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

    /// <summary>Tudor, 2026-09-16: the "your capital is under attack - you will respawn at your Tier 2 zone"
    /// line PlayerLifecycle polls onto the respawn panel while a player waits to come back into the match. Built
    /// lazily under respawnPanel, below its existing "Respawning! Please Wait!" label, the first time this is
    /// called - hides itself (SetActive on respawnNoteGo, the note's own root, not just an empty string or the
    /// text's own GameObject) whenever text is empty, same "the root, not a child" rule PlayerHud.ShowToast
    /// follows for its own toast.</summary>
    public void SetRespawnNote(string text)
    {
        if (respawnPanel == null)
            return; // Awake already logged the missing-panel error; nothing to attach the note to.

        if (respawnNoteGo == null)
            BuildRespawnNoteLabel();

        respawnNoteText.text = text ?? "";
        respawnNoteGo.SetActive(!string.IsNullOrEmpty(text));
    }

    /// <summary>Same small recipe PlayerHud.AddLabel/ApplyOutline uses (font/colour from UiTheme, one outline
    /// material) plus a dark backing strip behind the text - the readability trick PlayerHud's own Panel Colour
    /// gives every HUD group, applied here because plain white text with just a thin outline read too faint
    /// against the respawn panel's own pale salmon wash (B3 review, 2026-09-16, 616x576 capture). Kept private
    /// and duplicated here rather than shared, the same call PlayerHud's own class comment makes for itself: the
    /// two components have no other coupling, so a shared utility class would exist only for this one method.</summary>
    private void BuildRespawnNoteLabel()
    {
        respawnNoteGo = new GameObject("Under Attack Note", typeof(RectTransform));
        respawnNoteGo.transform.SetParent(respawnPanel.transform, false);

        RectTransform rootRt = respawnNoteGo.GetComponent<RectTransform>();
        // respawnPanel's own existing content ("Respawning! Please Wait!") sits at anchoredPosition
        // (0, 150) - this sits below it rather than overlapping, still well inside the panel's own
        // -80/-80 stretch margin at the game's tested 616x576 Game view.
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
        // Its own dedicated size/colour (B3 review), not Body Text Size/Text Colour: those are tuned for
        // the HUD's own dark Panel Colour backing, and this note needed to be noticeably bigger to read
        // clearly at a glance on the respawn panel.
        respawnNoteText.fontSize = theme != null ? theme.capitalUnderAttackNoteFontSize : 30f;
        respawnNoteText.color = theme != null ? theme.capitalUnderAttackNoteColor : Color.white;
        respawnNoteText.alignment = TextAlignmentOptions.Center;
        respawnNoteText.enableWordWrapping = true;
        respawnNoteText.raycastTarget = false;

        if (theme != null)
        {
            // Font must be assigned before fontSharedMaterial is touched - see PlayerHud.AddLabel's
            // own comment for why the order matters (assigning .font switches fontSharedMaterial to
            // that font asset's own default, which is exactly the template this clones from).
            Material outlineMaterial = new Material(respawnNoteText.fontSharedMaterial);
            outlineMaterial.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, theme.textOutlineWidth);
            outlineMaterial.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, theme.textOutlineColor);
            respawnNoteText.fontSharedMaterial = outlineMaterial;
        }

        respawnNoteGo.SetActive(false); // SetRespawnNote shows/hides it from here on.
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

        // Playtest extras P5 (2026-09-26): this client's own match log, zipped and shown alongside
        // its own result panel - see MatchLogZip's own class comment for why calling this again on
        // quit (GameQuit.Quit) is still safe.
        Overpower.Telemetry.MatchLogZip.Instance?.ZipNow();
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

    // ---- RPCs (retired, Task 2.7) -----------------------------------------------------------
    // Used to be sent by the master client's elimination bookkeeping in PlayerLifecycle, targeted at
    // one player's own PhotonView. Elimination and the match result are decided from replicated Room
    // Properties now (MatchDirector), which every client - including a late joiner - already reads,
    // so none of these four are called any more; each forwards to (or was replaced by) a plain local
    // method MatchDirector calls directly. Inside a body that still runs, "local" means the RECEIVER:
    // every PhotonNetwork.LocalPlayer read below is the player being told, not whoever sent it.
    //
    // None of these four names may be renamed or removed. PUN sends an index into the RpcList in
    // PhotonServerSettings.asset, which is a committed list of method NAMES, so a rename or removal
    // mis-dispatches every RPC listed after it on any client that already shipped.

    /// Kept only for the committed RpcList (Task 2.7 retired its only caller, PlayerLifecycle.
    /// RPC_HandleDeathMaster's old winner announcement) - MatchDirector now writes mWin and every
    /// client reacts through ShowMatchResult instead.
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

    /// <summary>Shows this player their "waiting for a teammate to retake the capital" panel,
    /// locally (Task 2.7 review). PlayerLifecycle calls this directly the instant a death becomes a
    /// last-stand death - no RPC needed, the same local-call pattern ShowYouLost uses. The old RPC
    /// below is kept only for the committed RpcList and now just forwards here.</summary>
    public void ShowWaitingPanel()
    {
        Debug.Log("[MatchUI] Show Waiting Panel Entered");

        // Never over the top of "you lost": being eliminated outranks waiting for a respawn that
        // is no longer coming.
        if (waitingPanel != null && !waitingPanel.activeSelf && (youLostPanel == null || !youLostPanel.activeSelf))
        {
            waitingPanel.SetActive(true);
            Debug.Log("[MatchUI] (local) Showing Waiting panel.");
        }
    }

    /// Kept only for the committed RpcList (Task 2.7 retired its only caller, PlayerLifecycle.
    /// RPC_HandleDeathMaster) - an older client could still send it, so the body stays, just
    /// forwarding to the local method above instead of duplicating it.
    [PunRPC]
    void RPC_ShowWaitingPanel(int teamID)
    {
        if ((int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey] != teamID) return;
        ShowWaitingPanel();
    }

    /// <summary>Shows this player their team-eliminated panel, locally. Task 2.7: MatchDirector calls
    /// this directly, on each newly-eliminated team's own client, once elimination is decided from
    /// replicated state - no RPC needed any more, since the state (mElim) already replicated itself.
    /// The old RPC below is kept only for the committed RpcList and now just forwards here.</summary>
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
    }

    /// Kept only for the committed RpcList (Task 2.7 retired its caller, PlayerLifecycle.
    /// RPC_HandleDeathMaster) - an older client could still send it, so the body stays, just
    /// forwarding to the local method above instead of duplicating it.
    [PunRPC]
    void RPC_ShowYouLostPanel(int teamID)
    {
        if ((int)PhotonNetwork.LocalPlayer.CustomProperties[PlayerTeam.TeamKey] != teamID) return;
        ShowYouLost();
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
