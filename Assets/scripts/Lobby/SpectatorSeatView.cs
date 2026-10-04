using System.Collections.Generic;
using Overpower.Arena;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;
using Overpower.Vision;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Overpower.Lobby
{
    /// <summary>
    /// What a player on a spectator seat sees (lobby Task 6, spec section 7): no body, no gold, no shop, no fog. The scene's follow camera
    /// (CameraTracking) is reused as the rig: it follows a player's body, or the whole map framed on the arena outline. Q / E go to the
    /// previous / next player (actor order, wrapping), Space toggles the whole map, the mouse wheel zooms as it always did (CameraTracking
    /// reads it). The keys are read here through the Input System and call the public methods (Previous, Next, ToggleWholeMap), which
    /// are what checks call. The bar at the bottom (SpectatorBar) shows who is watched and the keys, with Leave.
    ///
    /// Sits on the RoomManager's GameObject (added by RoomManager.Awake, so the scene file does not change) and does nothing until
    /// LobbyStart calls Begin on the spec = true edge; End (leaving the room) gives the camera back.
    /// </summary>
    public sealed class SpectatorSeatView : MonoBehaviour
    {
        private RoomManager roomManager;
        private CameraTracking cam;
        private Camera cameraComponent; // the follow camera's Camera, found once per Begin (asked every frame while the map is framed)
        private Transform mapAnchor;
        private SpectatorBar bar;
        private SpectatorResultCard resultCard;
        private int resultWinner = SpectateRules.None; // the winner the room named, kept until the card can go up (Begin may come after it)
        private readonly object zoomKey = new object();
        private readonly List<SpectateCandidate> watchable = new List<SpectateCandidate>(16);

        private int currentActor = SpectateRules.None;
        private bool wholeMapChosen;
        private bool mapFramed;

        // The last whole-map framing and what it was worked out for: the camera and the theme rarely change, so the search runs once.
        private bool framingKnown;
        private float framingFov, framingAspect, framingTilt, framingFill, framingYaw, framingReserve;
        private Vector2 framingAim;
        private float framingDistance;

        public void Init(RoomManager manager) => roomManager = manager;

        /// <summary>True from Begin until End: this client is on a spectator seat and the camera is ours.</summary>
        public bool IsWatching { get; private set; }

        /// <summary>The actor being followed, or SpectateRules.None while the whole map is shown (or nobody can be followed yet).</summary>
        public int CurrentActor => ShowingWholeMap ? SpectateRules.None : currentActor;

        /// <summary>The whole map is in view: chosen with Space, or nobody has a body to follow yet.</summary>
        public bool ShowingWholeMap => IsWatching && (wholeMapChosen || currentActor == SpectateRules.None);

        /// <summary>The transform the camera follows right now (a player's body, or the map's centre marker), or null.</summary>
        public Transform CameraTarget => cam != null ? cam.target : null;

        /// <summary>True once the match-end result card is up (a spectator's version of the YOU WIN / YOU LOSE screen).</summary>
        public bool ResultShown => resultCard != null;

        /// <summary>The card's title ("Purple wins the match"); empty before it is up.</summary>
        public string ResultTitle => resultCard != null ? resultCard.Title : "";

        /// <summary>The card, once built (checks read its button and press it through onClick).</summary>
        public SpectatorResultCard ResultCard => resultCard;

        /// <summary>What the bar's name line reads ("Mara - Purple", "Whole map"); empty before Begin.</summary>
        public string BarLine => bar != null ? bar.NameLine : "";

        /// <summary>Starts spectating (the spec = true edge): the bar goes up and the camera starts on the first player.</summary>
        public void Begin()
        {
            if (IsWatching)
                return;
            cam = CameraTracking.Instance;
            if (cam == null)
            {
                Debug.LogWarning("[SPECTATOR] no follow camera in the scene - nothing to watch with");
                return;
            }
            IsWatching = true;
            wholeMapChosen = false;
            currentActor = SpectateRules.None;
            framingKnown = false;
            cameraComponent = cam.GetComponent<Camera>();
            UiTheme theme = roomManager != null ? roomManager.Theme : null;
            // No team of their own decides the angle, and it must not swing round between teams: one fixed angle (UiTheme > Spectator bar).
            cam.fixedYaw = theme != null ? theme.spectatorViewAngle : 0f;
            // The centre scan's wave and countdown need a vision config and a theme, which normally come from the player's own body.
            CentreScan.SetSpectatorSupport(roomManager != null ? roomManager.Vision : null, theme);
            if (theme != null)
                bar = SpectatorBar.Create(transform, theme, Leave);
            else
                Debug.LogError("[SPECTATOR] the RoomManager has no UiTheme - the spectator bar cannot be built");
            Debug.Log("[SPECTATOR] watching from a spectator seat");
            Refresh();
            TryShowResult(); // the match may already be decided
        }

        /// <summary>Lobby Task 15b (spec section 11): the room named a winner. A spectator has no body and so no MatchUI panel; they get the
        /// result card instead - the winner, the same Back to the lobby list button and (through the zip run here, as for a player) the saved-log
        /// box. Called by MatchDirector for a client with no body; a no-op for anyone not watching.</summary>
        public void ShowMatchResult(int winningTeam)
        {
            resultWinner = winningTeam;
            TryShowResult();
        }

        private void TryShowResult()
        {
            if (!SpectateRules.MustShowResult(IsWatching, resultWinner, resultCard != null))
                return;
            UiTheme theme = roomManager != null ? roomManager.Theme : null;
            if (theme == null)
            {
                Debug.LogError("[SPECTATOR] the RoomManager has no UiTheme - the result card cannot be built");
                return;
            }
            string title = SpectateRules.ResultTitle(theme.spectatorResultTitle, theme.scoreboardTeamNames, resultWinner);
            resultCard = SpectatorResultCard.Create(transform, theme, title, theme.ShotColorFor(resultWinner), Leave);
            Debug.Log("[SPECTATOR] match over: " + title);
            // The zip is written at the match end, as for a player (MatchUI.ShowMatchResult); it also raises the saved-log box.
            Overpower.Telemetry.MatchLogZip.Instance?.ZipNow();
        }

        /// <summary>Stops spectating (left the room): the camera is the normal follow camera again and the bar is gone.</summary>
        public void End()
        {
            if (!IsWatching)
                return;
            IsWatching = false;
            CentreScan.SetSpectatorSupport(null, null);
            if (cam != null)
            {
                cam.RemoveZoomMultiplier(zoomKey);
                cam.fixedYaw = null;
                cam.target = null;
            }
            if (bar != null)
                Destroy(bar.gameObject);
            bar = null;
            if (resultCard != null)
                Destroy(resultCard.gameObject);
            resultCard = null;
            resultWinner = SpectateRules.None;
            currentActor = SpectateRules.None;
            wholeMapChosen = false;
            mapFramed = false;
            cameraComponent = null;
        }

        /// <summary>E: the next player in actor order (wrapping). Leaves the whole-map view.</summary>
        public void Next() => Step(next: true);

        /// <summary>Q: the previous player in actor order (wrapping). Leaves the whole-map view.</summary>
        public void Previous() => Step(next: false);

        /// <summary>Space: the whole map framed on the arena outline, or back to the player last followed.</summary>
        public void ToggleWholeMap()
        {
            if (!IsWatching)
                return;
            wholeMapChosen = !wholeMapChosen;
            Refresh();
        }

        /// <summary>The bar's Leave: back to the lobby list.</summary>
        public void Leave() => roomManager?.ReturnToLobbyList();

        private void Step(bool next)
        {
            if (!IsWatching)
                return;
            Collect();
            int[] actors = SpectateRules.SortedActors(watchable);
            int actor = next ? SpectateRules.NextActor(actors, currentActor) : SpectateRules.PreviousActor(actors, currentActor);
            wholeMapChosen = false;
            currentActor = actor;
            Refresh();
        }

        private void Update()
        {
            if (!IsWatching)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && !TypingInAField())
            {
                if (keyboard.qKey.wasPressedThisFrame) Previous();
                if (keyboard.eKey.wasPressedThisFrame) Next();
                if (keyboard.spaceKey.wasPressedThisFrame) ToggleWholeMap();
            }

            // The followed player leaving, dying or going away: move on to whoever comes next; with nobody left, the whole map.
            Collect();
            if (currentActor != SpectateRules.None && !IsWatchable(currentActor))
                currentActor = SpectateRules.NextActor(SpectateRules.SortedActors(watchable), currentActor);
            else if (currentActor == SpectateRules.None && !wholeMapChosen && watchable.Count > 0)
                currentActor = SpectateRules.NextActor(SpectateRules.SortedActors(watchable), SpectateRules.None);
            Refresh();
        }

        private void OnDestroy()
        {
            if (IsWatching)
                End();
        }

        // A chat box has the keyboard: typing a Q must not move the camera.
        private static bool TypingInAField() => PlayerInputRouter.IsTypingInChat();

        private bool IsWatchable(int actor)
        {
            for (int i = 0; i < watchable.Count; i++)
                if (watchable[i].Actor == actor)
                    return true;
            return false;
        }

        /// <summary>Everyone alive and present on a team still in the match, with a spawned body - the players a spectator can follow.</summary>
        private void Collect()
        {
            watchable.Clear();
            Photon.Realtime.Room room = PhotonNetwork.CurrentRoom;
            if (room == null)
                return;
            MatchDirector director = MatchDirector.Instance;
            foreach (KeyValuePair<int, Photon.Realtime.Player> pair in room.Players)
            {
                Photon.Realtime.Player p = pair.Value;
                if (p.IsLocal || !Teams.TryGetPlayingTeam(p, out int team))
                    continue;
                if (director != null && director.IsEliminated(team))
                    continue;
                bool waiting = p.CustomProperties.TryGetValue(PlayerLifecycle.LastStandKey, out object lastStand) && lastStand is bool w && w;
                bool? alive = p.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool a ? a : (bool?)null;
                if (waiting || !PresenceRules.CountsAsAlive(p.IsInactive, alive))
                    continue;
                if (PlayerLookup.GetPhotonViewFor(p.ActorNumber) == null)
                    continue;
                watchable.Add(new SpectateCandidate(p.ActorNumber, team));
            }
        }

        /// <summary>Points the camera and the bar at the current choice. Cheap: runs every frame.</summary>
        private void Refresh()
        {
            if (cam == null)
                return;

            PhotonView view = !wholeMapChosen && currentActor != SpectateRules.None ? PlayerLookup.GetPhotonViewFor(currentActor) : null;
            if (view != null)
            {
                if (mapFramed)
                {
                    cam.RemoveZoomMultiplier(zoomKey);
                    mapFramed = false;
                }
                cam.target = view.transform;
                if (bar != null && roomManager != null && roomManager.Theme != null)
                {
                    Photon.Realtime.Player owner = view.Owner;
                    int team = Teams.TryGetTeam(owner, out int t) ? t : -1;
                    string[] names = roomManager.Theme.scoreboardTeamNames;
                    string teamName = names != null && team >= 0 && team < names.Length ? names[team] : "";
                    bar.Show(owner != null ? owner.NickName : "?", teamName, roomManager.Theme.ShotColorFor(team));
                }
                return;
            }

            FrameWholeMap();
            bar?.Show(null, "", Color.white);
        }

        /// <summary>How much of the screen's height the spectator bar covers from the bottom (its distance from the bottom, its padding and the
        /// Leave button, the tallest part), from the theme through the bar canvas scaler (so a wide screen reserves more than the reference height says), so the whole map is framed above it.</summary>
        private static float BarShareOfScreen(UiTheme theme) =>
            SpectateRules.BarShareOfScreen(theme.spectatorBarBottom + theme.spectatorBarLeaveSize.y + 2f * theme.spectatorBarPadding.y,
                theme.referenceResolution, theme.matchWidthOrHeight, Screen.width, Screen.height);

        // The whole arena in view: the camera follows a marker on the ground, from far enough away (the extra-zoom stack, so the mouse wheel
        // still zooms from there) that the arena fills UiTheme > Spectator bar > Whole map fill of the screen's height, centred.
        private void FrameWholeMap()
        {
            if (mapAnchor == null)
            {
                mapAnchor = new GameObject("Spectator Map Centre").transform;
                mapAnchor.SetParent(transform, false);
            }

            float aspect = cameraComponent != null ? cameraComponent.aspect : 16f / 9f;
            float fov = cameraComponent != null ? cameraComponent.fieldOfView : 60f;
            float tilt = cam.TiltDegrees;
            UiTheme theme = roomManager != null ? roomManager.Theme : null;
            float fill = theme != null ? theme.spectatorWholeMapFill : 0.88f;
            // The camera is turned by the fixed spectator angle (Begin sets cam.fixedYaw from the theme) and the bar covers the bottom of the screen.
            float yaw = cam.fixedYaw ?? 0f;
            float reserve = theme != null ? BarShareOfScreen(theme) : 0f;
            if (!framingKnown || fov != framingFov || aspect != framingAspect || tilt != framingTilt || fill != framingFill
                || yaw != framingYaw || reserve != framingReserve)
            {
                ArenaSymmetry arena = ArenaSymmetry.Active;
                IReadOnlyList<Vector2> outline = arena != null && arena.FullBounds != null ? arena.FullBounds.Polygon : null;
                SpectateRules.WholeMapFraming(outline, fov, aspect, tilt, fill, yaw, reserve, out framingAim, out framingDistance);
                framingFov = fov; framingAspect = aspect; framingTilt = tilt; framingFill = fill; framingYaw = yaw; framingReserve = reserve;
                framingKnown = true;
            }
            mapAnchor.position = new Vector3(framingAim.x, 0f, framingAim.y);
            cam.target = mapAnchor;

            float multiplier = Mathf.Max(1f, framingDistance / Mathf.Max(0.01f, cam.baseOffset.magnitude));
            if (!mapFramed)
                cam.ResetScrollZoom(); // framed from the normal distance, whatever the wheel was at
            cam.AddZoomMultiplier(zoomKey, multiplier);
            mapFramed = true;
        }
    }
}
