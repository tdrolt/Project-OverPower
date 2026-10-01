using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Rendering;
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

        [SerializeField, Tooltip("The minimap's world square. The sight picture covers the same square, so the minimap and the fog can share it.")]
        private MinimapConfig minimap;

        [SerializeField, Tooltip("The shader that paints each eye's sight shape white into the sight picture. Leave as set.")]
        private Shader sightFillShader;

        /// <summary>The owner's copy on this client, or null before it exists (then nothing is hidden).</summary>
        public static TeamSight Local { get; private set; }

        private static readonly int FogEnabledId = Shader.PropertyToID("_VisionFogEnabled");

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
        private System.Func<Vector3, bool> canSeePoint;

        // The sight texture: white where my team sees, black elsewhere (read by the fog shader, the minimap).
        private RenderTexture sightTexture;
        private Material fillMaterial;
        private Mesh fanMesh;
        private CommandBuffer drawBuffer;
        private readonly List<Vector2> fan = new List<Vector2>(1024);
        private readonly List<Vector3> meshVertices = new List<Vector3>(4096);
        private readonly List<int> meshTriangles = new List<int>(16384);
        private System.Func<Vector2, Vector2, float, float?> sightRaycast;
        private float currentEyeY;
        private Vector4 sightRect;
        private SpectateView spectate;

        /// <summary>The eyes built this frame (read-only, for later tasks that draw them).</summary>
        public IReadOnlyList<Eye> Eyes
        {
            get { Refresh(); return eyes; }
        }

        /// <summary>The sight picture (white = my team sees it), or null when it is not set up.</summary>
        public RenderTexture SightTexture => sightTexture;

        /// <summary>The world square the sight picture covers: (minX, minZ, sizeX, sizeZ).</summary>
        public Vector4 SightRect => sightRect;

        private bool FogOn => config != null && config.FogEnabled;

        private void Awake()
        {
            lifecycle = GetComponent<PlayerLifecycle>();
            clearLine = ClearLine;
            if (!photonView.IsMine)
                enabled = false;
            else if (config == null)
                Debug.LogError($"[TeamSight] {name}: Vision Config is not assigned - nothing will be hidden.");
            else
                CreateSightTexture();
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat(FogEnabledId, 0f);
            if (sightTexture != null)
            {
                sightTexture.Release();
                Destroy(sightTexture);
            }
            if (fillMaterial != null)
                Destroy(fillMaterial);
            if (fanMesh != null)
                Destroy(fanMesh);
            drawBuffer?.Release();
        }

        private void CreateSightTexture()
        {
            if (minimap == null || sightFillShader == null)
            {
                Debug.LogError($"[TeamSight] {name}: Minimap Config or the Sight Fill Shader is not assigned - no sight picture is drawn.");
                return;
            }
            int size = config.SightTextureSize;
            sightTexture = new RenderTexture(size, size, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                name = "Vision Sight Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            sightTexture.Create();

            // The minimap's world square and Sight Texture Size are read once, here; changing them needs a restart.
            Vector2 centre = minimap.WorldCentre;
            float side = minimap.WorldSizeMetres;
            sightRect = new Vector4(centre.x - side * 0.5f, centre.y - side * 0.5f, side, side);

            fillMaterial = new Material(sightFillShader) { hideFlags = HideFlags.HideAndDontSave };
            fillMaterial.SetVector("_SightRect", sightRect);
            fillMaterial.SetFloat("_SightFlipY", SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
            fanMesh = new Mesh { name = "Sight Fans", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            fanMesh.MarkDynamic();
            drawBuffer = new CommandBuffer { name = "Draw sight" };
            sightRaycast = SightRaycast;
        }

        // Sight stops where bullets stop: the Building layer, at this eye's own height.
        private float? SightRaycast(Vector2 origin, Vector2 direction, float maxDistance)
        {
            if (buildingMask < 0)
                buildingMask = LayerMask.GetMask("Building");
            return Physics.Raycast(new Vector3(origin.x, currentEyeY, origin.y), new Vector3(direction.x, 0f, direction.y),
                out RaycastHit hit, maxDistance, buildingMask, QueryTriggerInteraction.Ignore)
                ? hit.distance : (float?)null;
        }

        private void LateUpdate()
        {
            // No fog without a sight picture (the pass reads it), and none when the owner is gone (OnDisable / OnDestroy).
            bool fogOn = FogOn && sightTexture != null;
            Shader.SetGlobalFloat(FogEnabledId, fogOn ? 1f : 0f);
            if (!fogOn)
                return;
            Refresh();
            DrawSight();
            Shader.SetGlobalTexture("_VisionSightTex", sightTexture);
            Shader.SetGlobalVector("_VisionSightRect", sightRect);
            Shader.SetGlobalColor("_VisionFogColour", config.FogColour);
            Shader.SetGlobalFloat("_VisionFogDarkness", config.FogDarkness);
        }

        // Every eye's fan, from the same eye list CanSee uses, as one mesh drawn white on black.
        private void DrawSight()
        {
            meshVertices.Clear();
            meshTriangles.Clear();
            for (int e = 0; e < eyes.Count; e++)
            {
                currentEyeY = eyes[e].EyeY;
                SightPolygon.Build(eyes[e], config.SightRayCount, config.WallRevealDepth, sightRaycast, fan);
                int first = meshVertices.Count;
                for (int i = 0; i < fan.Count; i++)
                    meshVertices.Add(new Vector3(fan[i].x, fan[i].y, 0f));
                int outline = fan.Count - 1;
                for (int i = 1; i <= outline; i++)
                {
                    meshTriangles.Add(first);
                    meshTriangles.Add(first + i);
                    meshTriangles.Add(first + (i % outline) + 1); // the last outline point closes back to the first
                }
            }

            fanMesh.Clear();
            if (meshVertices.Count > 0)
            {
                fanMesh.SetVertices(meshVertices);
                fanMesh.SetTriangles(meshTriangles, 0, false);
                fanMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            }

            drawBuffer.Clear();
            drawBuffer.SetRenderTarget(sightTexture);
            drawBuffer.ClearRenderTarget(false, true, Color.black);
            if (meshVertices.Count > 0)
                drawBuffer.DrawMesh(fanMesh, Matrix4x4.identity, fillMaterial, 0, 0);
            Graphics.ExecuteCommandBuffer(drawBuffer);
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
            Shader.SetGlobalFloat(FogEnabledId, 0f);
        }

        /// <summary>True when my team sees this point, taken as a spot on the ground: the test is made Eye Height above it
        /// (as high as the eyes looking). Inside an eye's cone or circle with no wall between. True for
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
            // The enemy's feet: CanSee lifts the point by Eye Height, so the line runs feet+Eye Height to feet+Eye Height,
            // level with the texture's rays (one height for sight).
            return CanSee(view.transform.position + Vector3.up * FeetOffset());
        }

        /// <summary>True when my team sees any point along a to b, sampled every Line Sample Spacing metres (both ends
        /// included). True for everything when the fog is switched off. Used by beams and warning lines.</summary>
        public bool CanSeeLine(Vector3 a, Vector3 b)
        {
            if (!FogOn)
                return true;
            canSeePoint ??= CanSee;
            return ShotVisibilityRules.LineSeen(a, b, config.LineSampleSpacing, canSeePoint);
        }

        /// <summary>Whether a shooter counts as friendly to me: my own team, or the watched team while I am spectating.
        /// A shooter whose team is not known yet counts as an enemy.</summary>
        public bool IsFriendly(Photon.Realtime.Player shooter) =>
            Teams.TryGetTeam(shooter, out int team) && IsFriendlyTeam(team);

        /// <summary>The same by team number (a negative team is unknown, so an enemy).</summary>
        public bool IsFriendlyTeam(int shooterTeam)
        {
            Refresh();
            return ShotVisibilityRules.IsFriendlyTeam(shooterTeam, SightEyes.FriendlyTeam(mode, localTeam, watchedTeam));
        }

        // The three questions the shot visuals ask. Each is true when there is no TeamSight yet or the fog is off, so a
        // client without fog draws everything exactly as before.

        /// <summary>D2: is a shot of this team drawn at this point? Own team always; an enemy's only while the point is seen.</summary>
        public static bool ShotShownAt(int shooterTeam, Vector3 point)
        {
            TeamSight sight = Local;
            return sight == null || ShotVisibilityRules.ShowOwnTeamOrSeen(sight.IsFriendlyTeam(shooterTeam), sight.CanSee(point));
        }

        /// <summary>D2: is a beam / line of this team drawn? Own team always; an enemy's only while the line crosses sight.</summary>
        public static bool ShotShownAlong(int shooterTeam, Vector3 a, Vector3 b)
        {
            TeamSight sight = Local;
            return sight == null || ShotVisibilityRules.ShowOwnTeamOrSeen(sight.IsFriendlyTeam(shooterTeam), sight.CanSeeLine(a, b));
        }

        /// <summary>D2: is a blast of this team drawn? Own team always; an enemy's if its centre is seen or it reaches me.</summary>
        public static bool BlastShownAt(int shooterTeam, Vector3 centre, float radius)
        {
            TeamSight sight = Local;
            if (sight == null)
                return true;
            return ShotVisibilityRules.BlastShown(sight.IsFriendlyTeam(shooterTeam), sight.CanSee(centre),
                Vector3.Distance(centre, sight.transform.position), radius);
        }

        // The eye is at its own player's eye point (their feet + Eye Height); the asked-about point is lifted the same
        // height above the ground point it was given, so a low rock does not hide a player.
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
