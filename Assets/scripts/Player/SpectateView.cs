using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;
using Overpower.Net;

/// <summary>
/// The Spectate / Next button on a knocked-out player's "You lost" panel: the camera follows a living
/// player of the team that knocked this player out; each press moves to the next (SpectateRules.NextTarget).
/// Watching only, nothing here touches input: a knocked-out player is dead and frozen, so the HUD actions,
/// shop and shooting are already closed (alive gate, LoadoutScreen's MatchOver check).
/// Built by MatchUI (owner only) as a copy of the panel's own button; the panel's existing button is
/// BackToNameScreenRules' business. When the match is over the button goes and the camera returns to this body.
/// "The team that knocked you out" is not recorded by the room: it is the team behind this player's last lethal
/// hit (PlayerLifecycle.LastKillerTeam), else the team holding their base (SpectateRules.KnockerTeam).
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

    public int CurrentActor => currentActor;
    public bool IsSpectating => currentActor != SpectateRules.None;
    /// <summary>Tests and captures read it.</summary>
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

    /// <summary>While watching, the full-screen lose panel would hide the game, so it shrinks to a bottom strip
    /// (title and backdrop hidden) and comes back exactly as it was when watching stops.</summary>
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
                Advance(); // the followed player died or left; stays put when nobody is left
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

    /// <summary>Public for the Play Mode checks.</summary>
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
            return; // nobody watchable: keep target and camera, the refresh tries again (SpectateRules.PickOrKeep)

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

    private void CollectLiving()
    {
        living.Clear();
        MatchDirector director = MatchDirector.Instance;
        foreach (Photon.Realtime.Player p in PhotonNetwork.PlayerList)
        {
            if (p.IsLocal || !Teams.TryGetPlayingTeam(p, out int team)) // a seat spectator has no body to watch
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
