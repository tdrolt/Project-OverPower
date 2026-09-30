using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;
using Overpower.Net;

/// <summary>
/// Task 9g (Tudor D28): the Spectate / Next button on a knocked-out player's "You lost" panel. Pressed, the camera snaps to a
/// living player of the team that knocked this player out and follows them; pressed again it moves to the next living player of
/// that team (SpectateRules.NextTarget - when that team has no one alive, any living player of a team still in). Watching only:
/// nothing here touches input. A knocked-out player is dead and frozen, so the HUD actions, the shop and shooting are already
/// closed to them (alive gate, LoadoutScreen's MatchOver check); this component adds no way back in.
///
/// Built by MatchUI when the lose panel is first shown to its own player (owner only), as a copy of the panel's own button so it
/// looks the same; the panel's existing button stays what it was (BackToNameScreenRules decides that one). When the match is over
/// the button goes and the camera returns to this player's own body: the normal result screen takes over.
///
/// "The team that knocked you out": the room does not record it, so it is the team behind this player's own last lethal hit
/// (PlayerLifecycle.LastKillerTeam), else the team holding their base (SpectateRules.KnockerTeam). If the followed player dies or leaves, the view moves on by itself.
/// </summary>
public sealed class SpectateView : MonoBehaviour
{
    private const float RefreshSeconds = 0.25f;

    private MatchUI ui;
    private GameObject panel;
    private Button button;
    private TMP_Text label;
    private CameraTracking cam;
    private int currentActor = SpectateRules.None;
    private float nextRefresh;
    private RectTransform panelRect, quitRect, spectateRect;
    private Image panelImage;
    private GameObject titleObject;
    private bool compact;
    // The full panel's own layout, kept so it comes back exactly.
    private Vector2 savedAnchorMin, savedAnchorMax, savedPivot, savedPos, savedSize, savedQuitPos, savedSpectatePos;
    private readonly List<SpectateCandidate> living = new List<SpectateCandidate>();

    /// <summary>The actor number being watched, or SpectateRules.None.</summary>
    public int CurrentActor => currentActor;
    public bool IsSpectating => currentActor != SpectateRules.None;
    /// <summary>The button, once built (tests and captures read it).</summary>
    public Button Button => button;

    public void Init(MatchUI matchUi, GameObject losePanel)
    {
        ui = matchUi;
        panel = losePanel;
        QuitButton quit = panel != null ? panel.GetComponentInChildren<QuitButton>(true) : null;
        if (quit == null)
        {
            Debug.LogError("[SpectateView] the lose panel has no button to copy - no Spectate button.");
            enabled = false;
            return;
        }

        GameObject copy = Instantiate(quit.gameObject, quit.transform.parent);
        copy.name = "Spectate Button";
        DestroyImmediate(copy.GetComponent<QuitButton>()); // the copy must not quit the game
        RectTransform rect = copy.GetComponent<RectTransform>();
        quitRect = quit.GetComponent<RectTransform>();
        rect.anchoredPosition = quitRect.anchoredPosition + new Vector2(0f, quitRect.sizeDelta.y + 10f);
        button = copy.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Advance);
        label = copy.GetComponentInChildren<TMP_Text>(true);
        copy.SetActive(false);

        panelRect = panel.GetComponent<RectTransform>();
        panelImage = panel.GetComponent<Image>();
        spectateRect = rect;
        Transform title = panel.transform.Find("You lose");
        titleObject = title != null ? title.gameObject : null;
    }

    /// <summary>While watching, the full-screen lose panel hides the game: it shrinks to a strip at the bottom holding Next and Quit
    /// (big title and grey backdrop hidden), and comes back exactly as it was when watching stops or the match ends.</summary>
    private void SetCompact(bool on)
    {
        if (on == compact || panelRect == null || ui == null || ui.Theme == null)
            return;
        compact = on;
        if (on)
        {
            savedAnchorMin = panelRect.anchorMin; savedAnchorMax = panelRect.anchorMax; savedPivot = panelRect.pivot;
            savedPos = panelRect.anchoredPosition; savedSize = panelRect.sizeDelta;
            savedQuitPos = quitRect.anchoredPosition; savedSpectatePos = spectateRect.anchoredPosition;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, ui.Theme.spectateStripBottom);
            panelRect.sizeDelta = ui.Theme.spectateStripSize;
            quitRect.anchoredPosition = new Vector2(ui.Theme.spectateButtonOffset, 0f);
            spectateRect.anchoredPosition = new Vector2(-ui.Theme.spectateButtonOffset, 0f);
        }
        else
        {
            panelRect.anchorMin = savedAnchorMin; panelRect.anchorMax = savedAnchorMax; panelRect.pivot = savedPivot;
            panelRect.anchoredPosition = savedPos; panelRect.sizeDelta = savedSize;
            quitRect.anchoredPosition = savedQuitPos; spectateRect.anchoredPosition = savedSpectatePos;
        }
        if (panelImage != null)
            panelImage.enabled = !on;
        if (titleObject != null)
            titleObject.SetActive(!on);
    }

    private void Update()
    {
        if (button == null || panel == null)
            return;

        MatchDirector director = MatchDirector.Instance;
        MatchPhase phase = director != null ? director.Phase : MatchPhase.Warmup;
        bool visible = SpectateRules.ButtonVisible(panel.activeSelf, phase);
        if (button.gameObject.activeSelf != visible)
            button.gameObject.SetActive(visible);
        if (!visible)
        {
            if (IsSpectating)
                StopSpectating();
            return;
        }

        UiThemeLabels();
        SetCompact(IsSpectating);

        if (IsSpectating && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (!StillWatchable(currentActor))
                Advance(); // the followed player died or left: on to the next one (or stays put when nobody is left, until someone is alive again)
        }
    }

    private void UiThemeLabels()
    {
        if (label == null || ui == null || ui.Theme == null)
            return;
        string wanted = IsSpectating ? ui.Theme.spectateNextButton : ui.Theme.spectateButton;
        if (label.text != wanted)
            label.text = wanted;
    }

    private void OnDisable() => StopSpectating();

    /// <summary>First press starts watching; each next press moves on. Public for the Play Mode checks.</summary>
    public void Advance()
    {
        if (cam == null)
            cam = CameraTracking.Instance;
        if (cam == null)
            return;

        CollectLiving();
        int actor = SpectateRules.NextTarget(living, PreferredTeam(), currentActor);
        PhotonView view = actor != SpectateRules.None ? PlayerLookup.GetPhotonViewFor(actor) : null;
        if (view == null)
            return; // nobody watchable now: keep the target and the camera; the refresh tries again (SpectateRules.PickOrKeep)

        currentActor = SpectateRules.PickOrKeep(actor, currentActor);
        cam.yawSource = transform; // the arena keeps this player's own angle whichever team is watched
        cam.target = view.transform;
        Debug.Log($"[SPECTATE] watching actor {actor}");
    }

    private void StopSpectating()
    {
        if (cam == null)
            cam = CameraTracking.Instance;
        if (cam != null && IsSpectating)
        {
            cam.target = transform;
            cam.yawSource = null;
        }
        currentActor = SpectateRules.None;
        SetCompact(false);
    }

    private bool StillWatchable(int actor)
    {
        CollectLiving();
        for (int i = 0; i < living.Count; i++)
            if (living[i].Actor == actor)
                return true;
        return false;
    }

    private int PreferredTeam()
    {
        if (!Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam))
            return SpectateRules.None;
        PlayerLifecycle lifecycle = GetComponent<PlayerLifecycle>();
        int killer = lifecycle != null ? lifecycle.LastKillerTeam : -1;
        int baseOwner = -1;
        BuildingManager buildings = BuildingManager.Instance;
        if (buildings != null && buildings.Map != null && buildings.Current != null)
        {
            int capital = buildings.Map.CapitalOf(myTeam);
            if (capital >= 0)
                baseOwner = buildings.Current.OwnerOf(capital);
        }
        return SpectateRules.KnockerTeam(killer, baseOwner, myTeam);
    }

    /// <summary>Everyone alive and present, with a body, on a team still in the match - not this player.</summary>
    private void CollectLiving()
    {
        living.Clear();
        MatchDirector director = MatchDirector.Instance;
        foreach (Photon.Realtime.Player p in PhotonNetwork.PlayerList)
        {
            if (p.IsLocal || !Teams.TryGetTeam(p, out int team))
                continue;
            if (director != null && director.IsEliminated(team))
                continue;

            bool waiting = p.CustomProperties.TryGetValue(PlayerLifecycle.LastStandKey, out object lastStand) && lastStand is bool w && w;
            bool? alive = p.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool a ? a : (bool?)null;
            if (waiting || !PresenceRules.CountsAsAlive(p.IsInactive, alive))
                continue;

            PhotonView view = PlayerLookup.GetPhotonViewFor(p.ActorNumber);
            if (view == null)
                continue;
            living.Add(new SpectateCandidate(p.ActorNumber, team));
        }
    }
}
