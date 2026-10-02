using System.Collections.Generic;
using Overpower.Arena;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;
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
        // The border left round the map when it is framed, as a multiple of its farthest point.
        private const float WholeMapMargin = 1.08f;

        private RoomManager roomManager;
        private CameraTracking cam;
        private Transform mapAnchor;
        private SpectatorBar bar;
        private readonly object zoomKey = new object();
        private readonly List<SpectateCandidate> watchable = new List<SpectateCandidate>(16);

        private int currentActor = SpectateRules.None;
        private bool wholeMapChosen;
        private bool mapFramed;

        public void Init(RoomManager manager) => roomManager = manager;

        /// <summary>True from Begin until End: this client is on a spectator seat and the camera is ours.</summary>
        public bool IsWatching { get; private set; }

        /// <summary>The actor being followed, or SpectateRules.None while the whole map is shown (or nobody can be followed yet).</summary>
        public int CurrentActor => ShowingWholeMap ? SpectateRules.None : currentActor;

        /// <summary>The whole map is in view: chosen with Space, or nobody has a body to follow yet.</summary>
        public bool ShowingWholeMap => IsWatching && (wholeMapChosen || currentActor == SpectateRules.None);

        /// <summary>The transform the camera follows right now (a player's body, or the map's centre marker), or null.</summary>
        public Transform CameraTarget => cam != null ? cam.target : null;

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
            cam.fixedYaw = 0f; // no team of their own decides the angle, and it must not swing round between teams
            UiTheme theme = roomManager != null ? roomManager.Theme : null;
            if (theme != null)
                bar = SpectatorBar.Create(transform, theme, Leave);
            else
                Debug.LogError("[SPECTATOR] the RoomManager has no UiTheme - the spectator bar cannot be built");
            Debug.Log("[SPECTATOR] watching from a spectator seat");
            Refresh();
        }

        /// <summary>Stops spectating (left the room): the camera is the normal follow camera again and the bar is gone.</summary>
        public void End()
        {
            if (!IsWatching)
                return;
            IsWatching = false;
            if (cam != null)
            {
                cam.RemoveZoomMultiplier(zoomKey);
                cam.fixedYaw = null;
                cam.target = null;
            }
            if (bar != null)
                Destroy(bar.gameObject);
            bar = null;
            currentActor = SpectateRules.None;
            wholeMapChosen = false;
            mapFramed = false;
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

        /// <summary>The bar's Leave: back to the lobby list (for now the name screen, until lobby Task 8).</summary>
        public void Leave() => roomManager?.ReturnToNameScreen();

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
        private static bool TypingInAField()
        {
            EventSystem events = EventSystem.current;
            return events != null && events.currentSelectedGameObject != null
                && events.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        }

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

        // The whole arena in view: the camera follows a marker at the middle of the outline, from far enough away (the extra-zoom stack, so
        // the mouse wheel still zooms from there) that its farthest point is on screen.
        private void FrameWholeMap()
        {
            if (mapAnchor == null)
            {
                mapAnchor = new GameObject("Spectator Map Centre").transform;
                mapAnchor.SetParent(transform, false);
            }

            ArenaSymmetry arena = ArenaSymmetry.Active;
            IReadOnlyList<Vector2> outline = arena != null && arena.FullBounds != null ? arena.FullBounds.Polygon : null;
            SpectateRules.WholeMapFrame(outline, out Vector2 centre, out float radius);
            mapAnchor.position = new Vector3(centre.x, 0f, centre.y);
            cam.target = mapAnchor;

            Camera camera = cam.GetComponent<Camera>();
            float aspect = camera != null ? camera.aspect : 16f / 9f;
            float fov = camera != null ? camera.fieldOfView : 60f;
            float distance = SpectateRules.WholeMapDistance(radius, fov, aspect, cam.TiltDegrees, WholeMapMargin);
            float multiplier = Mathf.Max(1f, distance / Mathf.Max(0.01f, cam.baseOffset.magnitude));
            if (!mapFramed)
                cam.ResetScrollZoom(); // framed from the normal distance, whatever the wheel was at
            cam.AddZoomMultiplier(zoomKey, multiplier);
            mapFramed = true;
        }
    }
}
