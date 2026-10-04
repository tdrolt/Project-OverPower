using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Rendering;
using Overpower.Abilities;
using Overpower.Combat;
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
    // Order -50: its LateUpdate (fresh eyes for the fog, cone and EnemyVisibility's body hiding) runs before the other
    // LateUpdates at order 0 (EnemyVisibility, MinimapView), which then read this frame's positions.
    [DefaultExecutionOrder(-50)]
    public sealed class TeamSight : MonoBehaviourPun
    {
        [SerializeField, Tooltip("The sight numbers (cone, circle, eye height) and the fog on/off switch.")]
        private VisionConfig config;

        [SerializeField, Tooltip("The minimap's world square. The sight picture covers the same square, so the minimap and the fog can share it.")]
        private MinimapConfig minimap;

        [SerializeField, Tooltip("The shader that paints each eye's sight shape white into the sight picture. Leave as set.")]
        private Shader sightFillShader;

        [SerializeField, Tooltip("The shader the minimap uses to darken the parts of the arena my team cannot see. Leave as set.")]
        private Shader minimapFogShader;

        [SerializeField, Tooltip("The Scope's module on Scope.prefab. Its three sight numbers (cone angle, cone length, circle change) " +
                 "are the shape of anyone who is holding the Scope, so every client reads the same values for any teammate.")]
        private ScopeAbility scopeSight;

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
        private bool atEyeHeight; // true while answering CanSeeShot
        private float feetOffset; // the capsule's bottom below the pivot, read once from the prefab
        private bool feetOffsetRead;
        private PlayerLifecycle lifecycle;
        private System.Func<Vector3, bool> canSeePoint;
        private AbilityRunner abilities;
        private readonly RevealTimers reveals = new RevealTimers();

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

        /// <summary>The sight numbers and fog settings (the minimap reads the fog colour, darkness and enemy dot colour).</summary>
        public VisionConfig Config => config;

        /// <summary>The shader for the minimap's fog layer.</summary>
        public Shader MinimapFogShader => minimapFogShader;

        /// <summary>True while the fog is drawn: switched on and the sight picture exists. The minimap shows its fog layer
        /// and the red enemy dots only then.</summary>
        public bool FogActive => FogOn && sightTexture != null;

        /// <summary>The team whose members are always shown to me (the watched team while spectating, else my own), or -1.</summary>
        public int FriendlyTeamId
        {
            get { Refresh(); return SightEyes.FriendlyTeam(mode, localTeam, watchedTeam); }
        }

        // A seat spectator (lobby Task 6) sees everything: no fog, no hidden enemies, every shot and zone shown. They have no body, so
        // this component normally does not exist on their client (and nothing is hidden then anyway); this keeps the answer the same
        // should one ever be there. Read once a frame - it is asked many times.
        private bool FogOn => VisionRules.FogApplies(config != null && config.FogEnabled, LocalIsSeatSpectator());

        private int seatSpectatorFrame = -1;
        private bool seatSpectator;

        private bool LocalIsSeatSpectator()
        {
            if (seatSpectatorFrame != Time.frameCount)
            {
                seatSpectatorFrame = Time.frameCount;
                seatSpectator = Teams.IsSpectator(PhotonNetwork.LocalPlayer);
            }
            return seatSpectator;
        }

        private void Awake()
        {
            lifecycle = GetComponent<PlayerLifecycle>();
            abilities = GetComponent<AbilityRunner>();
            clearLine = ClearLine;
            minimap = SceneMinimapConfig.Resolve(minimap); // a map with its own picture (the Dominion lane) replaces the prefab's
            if (!photonView.IsMine)
                enabled = false;
            else if (config == null)
                Debug.LogError($"[TeamSight] {name}: Vision Config is not assigned - nothing will be hidden.");
            else
            {
                if (minimapFogShader == null)
                    Debug.LogError($"[TeamSight] {name}: Minimap Fog Shader is not assigned - the minimap shows no fog.");
                CreateSightTexture();
            }
        }

        private void OnDestroy()
        {
            if (photonView != null && photonView.IsMine)
                Shader.SetGlobalFloat(FogEnabledId, 0f); // a remote copy leaving must not drop the owner's fog
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
                // MSAA: the fan edges get fractional coverage instead of all-or-nothing texels; Unity resolves it before the
                // fog and the minimap sample the picture, so the shaders' pixel-wide threshold draws a straight line.
                antiAliasing = 8,
            };
            sightTexture.Create();

            // The minimap's world square and Sight Texture Size are read once, here; changing them needs a restart.
            Vector2 centre = minimap.WorldCentre;
            float side = minimap.WorldSizeMetres;
            float depth = minimap.WorldDepthMetres; // a rectangular minimap covers a rectangle of the world; the sight picture covers the same one
            sightRect = new Vector4(centre.x - side * 0.5f, centre.y - depth * 0.5f, side, depth);

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
            // No fog without a sight picture (the pass reads it), none when the owner is gone (OnDisable / OnDestroy), and none for a seat spectator.
            bool fogOn = FogOn && sightTexture != null;
            Shader.SetGlobalFloat(FogEnabledId, fogOn ? 1f : 0f);
            if (!fogOn)
                return;
            // CentreScan and ZoneKnowledge (orders -110 / -100) already built the eyes this frame from last frame's positions,
            // before movement, aim and the network moved anyone; draw and hide bodies from fresh ones.
            builtFrame = -1;
            Refresh();
            DrawSight();
            Shader.SetGlobalTexture("_VisionSightTex", sightTexture);
            Shader.SetGlobalVector("_VisionSightRect", sightRect);
            Color fog = config.FogColour;
            Shader.SetGlobalVector("_VisionFogColour", QualitySettings.activeColorSpace == ColorSpace.Linear ? fog.linear : fog);
            Shader.SetGlobalFloat("_VisionFogDarkness", config.FogDarkness);
            Shader.SetGlobalFloat("_VisionMinimapDarkness", config.MinimapFogDarkness);
            Shader.SetGlobalFloat("_VisionMinimapSeenLift", config.MinimapSeenLift);
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
            if (photonView != null && photonView.IsMine)
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

        /// <summary>Like CanSee, for a point already in the air (a bullet, muzzle or impact flash): the wall test aims at the
        /// eye's own height, level with the sight picture, so a Cover Wall hides a shot behind it.</summary>
        public bool CanSeeShot(Vector3 worldPoint)
        {
            if (!FogOn)
                return true;
            Refresh();
            pointY = worldPoint.y;
            atEyeHeight = true;
            try { return VisionRules.TeamSeesWithEye(eyes, new Vector2(worldPoint.x, worldPoint.z), clearLine); }
            finally { atEyeHeight = false; }
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
            if (view.Owner != null && reveals.IsRevealed(view.OwnerActorNr, Time.time))
                return true; // a blind hit with a revealing weapon (X-Ray) shows them for a moment
            // The enemy's feet: CanSee lifts the point by Eye Height, so the line runs feet+Eye Height to feet+Eye Height,
            // level with the texture's rays (one height for sight).
            return CanSee(view.transform.position + Vector3.up * FeetOffset());
        }

        /// <summary>Shows this player to my team for the given seconds (an X-Ray blind hit). A later reveal extends the time,
        /// never shortens it. Zero or less does nothing.</summary>
        public void Reveal(PhotonView view, float seconds)
        {
            if (view != null && view.Owner != null)
                reveals.Reveal(view.OwnerActorNr, Time.time, seconds);
        }

        /// <summary>Called for every hit of a shot on a player, on every client: if the weapon reveals on hit, the shooter is on
        /// my team (or the team I watch) and the one hit is an enemy, shows that enemy to my team. Damage is not touched.</summary>
        public static void RevealOnHit(WeaponDefinition weapon, IDamageable victim, int shooterTeam)
        {
            TeamSight sight = Local;
            if (sight == null || weapon == null || victim == null || weapon.RevealOnHitSeconds <= 0f || !sight.FogOn)
                return;
            PhotonView view = (victim as Component) != null ? ((Component)victim).GetComponentInParent<PhotonView>() : null;
            if (view == null || view.Owner == null)
                return; // not a player (a practice dummy, a wall)
            bool enemy = victim.TeamId >= 0 && !sight.IsFriendlyTeam(victim.TeamId);
            if (RevealOnHitRule.ShouldReveal(sight.IsFriendlyTeam(shooterTeam), enemy, weapon.RevealOnHitSeconds))
                sight.Reveal(view, weapon.RevealOnHitSeconds);
        }

        /// <summary>True when my team sees any point along a to b, sampled every Line Sample Spacing metres (both ends
        /// included). True for everything when the fog is switched off. Used by beams and warning lines.</summary>
        public bool CanSeeLine(Vector3 a, Vector3 b)
        {
            if (!FogOn)
                return true;
            canSeePoint ??= CanSeeShot;
            return ShotVisibilityRules.LineSeen(a, b, config.LineSampleSpacing, canSeePoint);
        }

        /// <summary>Whether a shooter on this team counts as friendly to me: my own team, or the watched team while I am
        /// spectating. A negative team is unknown, so an enemy.</summary>
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
            return sight == null || ShotVisibilityRules.ShowOwnTeamOrSeen(sight.IsFriendlyTeam(shooterTeam), sight.CanSeeShot(point));
        }

        /// <summary>D2: is a beam / line of this team drawn? Own team always; an enemy's only while the line crosses sight.</summary>
        public static bool ShotShownAlong(int shooterTeam, Vector3 a, Vector3 b)
        {
            TeamSight sight = Local;
            return sight == null || ShotVisibilityRules.ShowOwnTeamOrSeen(sight.IsFriendlyTeam(shooterTeam), sight.CanSeeLine(a, b));
        }

        /// <summary>D2: is a blast of this team drawn? Own team always; an enemy's if its centre is seen or it hurts someone
        /// on my team (reaches one of my team's eyes: mine while I live, my living teammates', the watched team's).</summary>
        public static bool BlastShownAt(int shooterTeam, Vector3 centre, float radius)
        {
            TeamSight sight = Local;
            if (sight == null)
                return true;
            bool friendly = sight.IsFriendlyTeam(shooterTeam);
            bool seen = friendly || sight.CanSeeShot(centre); // a friendly blast needs no wall test; a burst is in the air, so eye height
            return ShotVisibilityRules.BlastShown(friendly, seen, !seen && sight.BlastReachesMyTeam(centre, radius));
        }

        private readonly List<Vector3> eyeSpots = new List<Vector3>(16);
        private System.Func<Vector3, bool> canSeeShotPoint;

        // Reaches the body (the line from each player's feet up to their eye), not just the feet.
        private bool BlastReachesMyTeam(Vector3 centre, float radius)
        {
            Refresh();
            float eyeHeight = config != null ? config.EyeHeight : 1f;
            FillFeet();
            return ShotVisibilityRules.ReachesAnyBody(centre, radius, eyeSpots, eyeHeight);
        }

        private void FillFeet()
        {
            float eyeHeight = config != null ? config.EyeHeight : 1f;
            eyeSpots.Clear();
            for (int i = 0; i < eyes.Count; i++)
                eyeSpots.Add(new Vector3(eyes[i].Position.x, eyes[i].EyeY - eyeHeight, eyes[i].Position.y)); // each player's feet
        }

        /// <summary>Whether a placed disc (an AoE Zone, a Fire Field) of this owner team is drawn: always for my team;
        /// otherwise when its centre or any of 8 rim points is seen, or when the disc reaches any of my team.</summary>
        public static bool DiscShownAt(int ownerTeam, Vector3 centre, float radius)
        {
            TeamSight sight = Local;
            if (sight == null || sight.IsFriendlyTeam(ownerTeam) || !sight.FogOn)
                return true;
            sight.canSeeShotPoint ??= sight.CanSeeShot;
            return ShotVisibilityRules.DiscSeen(centre, radius, 8, sight.canSeeShotPoint) || sight.BlastReachesMyTeam(centre, radius);
        }

        /// <summary>True when the flat cone (apex, forward, range, full angle) reaches any of my team's players.</summary>
        public static bool ConeReachesMyTeam(Vector3 apex, Vector3 forward, float range, float fullAngleDegrees)
        {
            TeamSight sight = Local;
            if (sight == null)
                return false;
            sight.Refresh();
            sight.FillFeet();
            for (int i = 0; i < sight.eyeSpots.Count; i++)
                if (ShotVisibilityRules.ConeReaches(apex, forward, range, fullAngleDegrees, sight.eyeSpots[i]))
                    return true;
            return false;
        }

        // The eye is at its own player's eye point (their feet + Eye Height); the asked-about point is lifted the same
        // height above the ground point it was given, so a low rock does not hide a player.
        private bool ClearLine(Eye eye, Vector2 to)
        {
            if (buildingMask < 0)
                buildingMask = LayerMask.GetMask("Building");
            float height = config != null ? config.EyeHeight : 1f;
            Vector3 a = new Vector3(eye.Position.x, eye.EyeY, eye.Position.y);
            Vector3 b = new Vector3(to.x, ShotVisibilityRules.TargetHeight(atEyeHeight, eye.EyeY, pointY, height), to.y);
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
                    if (!Teams.TryGetPlayingTeam(player, out int team))
                        team = -1; // a seat spectator (no body) or a player with no team yet: no team's eye
                    // Same reading as MinimapView: a missing flag means alive, a dropped player is not.
                    bool? flag = player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool b ? b : (bool?)null;
                    bool alive = view.IsMine ? localAlive : PresenceRules.CountsAsAlive(player.IsInactive, flag); // my own state from the same source as the mode
                    Vector3 position = view.transform.position;
                    Vector3 forward = view.transform.forward;
                    // Each eye stands at its own player's feet (this prefab's capsule bottom), not at the local player's height.
                    float eyeY = position.y + FeetOffset() + eyeHeight;
                    // Local = my own live Scope state (no round trip); everyone else = their published vScp property.
                    bool scoped = view.IsMine ? LocalHoldingScope() : player.CustomProperties.TryGetValue(ScopeSightProperty.Key, out object scp) && ScopeSightProperty.Read(scp);
                    candidates.Add(new SightCandidate(team, alive, view.IsMine,
                        new Vector2(position.x, position.z), new Vector2(forward.x, forward.z), eyeY, scoped));
                }
            }

            SightShape shape = config != null
                ? VisionRules.ShapeFor(config.ConeAngleDegrees, config.ConeLength, config.CircleRadius, false, 0f, 0f, 0f)
                : new SightShape(0f, 0f, 0f);
            SightShape scopedShape = config != null && scopeSight != null
                ? VisionRules.ShapeFor(config.ConeAngleDegrees, config.ConeLength, config.CircleRadius, true,
                    scopeSight.SightConeAngleDegrees, scopeSight.SightConeLength, scopeSight.SightCircleChange)
                : shape; // no Scope reference: a scoped player sees as normal
            SightEyes.Build(candidates, localTeam, mode, watchedTeam, shape, scopedShape, eyes);
        }

        private bool LocalHoldingScope()
        {
            if (abilities == null)
                return false;
            return abilities.StatusFor(AbilitySlot.Attachment) is ScopeAbility scope && scope.IsHoldingSight;
        }
    }
}
