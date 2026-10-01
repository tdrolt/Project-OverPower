using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;

namespace Overpower.Vision
{
    /// <summary>
    /// What my team sees, for the owner's own copy of the player only (like AimConeView and PlayerHud). Once per frame it
    /// lists the eyes (my own and my living teammates'; when I am dead only the teammates'; when I am spectating the
    /// watched team's) and answers CanSee / CanSeePlayer. The rules are VisionRules and SightEyes; this only gathers the
    /// facts from Photon and the scene and does the wall test (a Linecast on the Building layer at eye height).
    /// Nothing here touches hits, collisions or networking: it is only read by what draws things.
    /// </summary>
    public sealed class TeamSight : MonoBehaviourPun
    {
        [SerializeField, Tooltip("The sight numbers (cone, circle, eye height) and the fog on/off switch.")]
        private VisionConfig config;

        /// <summary>The owner's copy on this client, or null before it exists (then nothing is hidden).</summary>
        public static TeamSight Local { get; private set; }

        private readonly List<SightCandidate> candidates = new List<SightCandidate>(16);
        private readonly List<Eye> eyes = new List<Eye>(16);
        private System.Func<Eye, Vector2, bool> clearLine;
        private int builtFrame = -1;
        private int localTeam = -1;
        private int watchedTeam = -1;
        private ViewerMode mode = ViewerMode.Alive;
        private int buildingMask = -1;
        private float pointY; // the height of the point being asked about
        private float feetOffset; // the capsule's bottom below the pivot, read once from the prefab
        private bool feetOffsetRead;
        private PlayerLifecycle lifecycle;
        private SpectateView spectate;

        /// <summary>The eyes built this frame (read-only, for later tasks that draw them).</summary>
        public IReadOnlyList<Eye> Eyes
        {
            get { Refresh(); return eyes; }
        }

        private bool FogOn => config != null && config.FogEnabled;

        private void Awake()
        {
            lifecycle = GetComponent<PlayerLifecycle>();
            clearLine = ClearLine;
            if (!photonView.IsMine)
                enabled = false;
            else if (config == null)
                Debug.LogError($"[TeamSight] {name}: Vision Config is not assigned - nothing will be hidden.");
        }

        private void OnEnable()
        {
            if (photonView != null && photonView.IsMine)
                Local = this;
        }

        private void OnDisable()
        {
            if (Local == this)
                Local = null;
        }

        /// <summary>True when my team sees this point: inside an eye's cone or circle with no wall between. True for
        /// everything when the fog is switched off.</summary>
        public bool CanSee(Vector3 worldPoint)
        {
            if (!FogOn)
                return true;
            Refresh();
            pointY = worldPoint.y;
            return VisionRules.TeamSeesWithEye(eyes, new Vector2(worldPoint.x, worldPoint.z), clearLine);
        }

        /// <summary>My own body and my team (the watched team while spectating) are always shown; anyone else only while
        /// their position is seen.</summary>
        public bool CanSeePlayer(PhotonView view)
        {
            if (view == null || view.IsMine || !FogOn)
                return true;
            Refresh();
            int friendly = SightEyes.FriendlyTeam(mode, localTeam, watchedTeam);
            if (friendly >= 0 && Teams.TryGetTeam(view.Owner, out int team) && team == friendly)
                return true;
            return CanSee(view.transform.position);
        }

        // The eye is at its own player's eye point (their feet + Eye Height); the asked-about point is lifted the same
        // height above its own ground, so a low rock does not hide a player.
        private bool ClearLine(Eye eye, Vector2 to)
        {
            if (buildingMask < 0)
                buildingMask = LayerMask.GetMask("Building");
            float height = config != null ? config.EyeHeight : 1f;
            Vector3 a = new Vector3(eye.Position.x, eye.EyeY, eye.Position.y);
            Vector3 b = new Vector3(to.x, pointY + height, to.y);
            return !Physics.Linecast(a, b, buildingMask, QueryTriggerInteraction.Ignore);
        }

        // Height of the capsule's bottom relative to the pivot (the feet; about -0.5 on the player prefab).
        private float FeetOffset()
        {
            if (!feetOffsetRead)
            {
                feetOffsetRead = true;
                CapsuleCollider capsule = GetComponent<CapsuleCollider>();
                feetOffset = capsule != null ? (capsule.center.y - capsule.height * 0.5f) * transform.lossyScale.y : 0f;
            }
            return feetOffset;
        }

        private void Refresh()
        {
            if (builtFrame == Time.frameCount)
                return;
            builtFrame = Time.frameCount;

            candidates.Clear();
            localTeam = Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int mine) ? mine : -1;

            watchedTeam = -1;
            bool spectating = false;
            if (spectate == null)
                spectate = GetComponent<SpectateView>(); // MatchUI adds it at runtime, on this same object
            if (spectate != null && spectate.IsSpectating)
            {
                spectating = true;
                if (!Teams.TryGetTeam(spectate.CurrentActor, out watchedTeam))
                    watchedTeam = -1;
            }
            bool localAlive = lifecycle == null || lifecycle.IsAlive;
            mode = SightEyes.ModeFor(spectating, localAlive);
            float eyeHeight = config != null ? config.EyeHeight : 1f;

            Room room = PhotonNetwork.CurrentRoom;
            if (room != null)
            {
                foreach (KeyValuePair<int, Photon.Realtime.Player> pair in room.Players)
                {
                    Photon.Realtime.Player player = pair.Value;
                    PhotonView view = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                    if (view == null)
                        continue;
                    if (!Teams.TryGetTeam(player, out int team))
                        team = -1;
                    // Same reading as MinimapView: a missing flag means alive, a dropped player is not.
                    bool? flag = player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool b ? b : (bool?)null;
                    bool alive = view.IsMine ? localAlive : PresenceRules.CountsAsAlive(player.IsInactive, flag); // my own state from the same source as the mode
                    Vector3 position = view.transform.position;
                    Vector3 forward = view.transform.forward;
                    // Each eye stands at its own player's feet (this prefab's capsule bottom), not at the local player's height.
                    float eyeY = position.y + FeetOffset() + eyeHeight;
                    candidates.Add(new SightCandidate(team, alive, view.IsMine,
                        new Vector2(position.x, position.z), new Vector2(forward.x, forward.z), eyeY));
                }
            }

            SightShape shape = config != null
                ? VisionRules.ShapeFor(config.ConeAngleDegrees, config.ConeLength, config.CircleRadius, false, 0f, 0f, 0f)
                : new SightShape(0f, 0f, 0f);
            SightEyes.Build(candidates, localTeam, mode, watchedTeam, shape, eyes);
        }
    }
}
