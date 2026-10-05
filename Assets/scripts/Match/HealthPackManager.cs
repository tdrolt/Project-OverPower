using System.Collections.Generic;
using Overpower.Arena;
using Overpower.Data;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Match
{
    /// <summary>
    /// The Tier III health packs: one floating cross in each Tier III zone's recess, between the arena wall and the
    /// yellow barrier. Walk over it hurt and alive and it heals you 50 (never past max), then it is gone for everyone for 30 s.
    ///
    /// State: ONE Room Property per pack, "hp" + zone, int[] { takenUntilMs, takerActor, requestId } (see
    /// HealthPackRules), written ONLY by the master with a check-and-set. A player asks by writing a Player
    /// Property on itself, "hpReq" = int[] { zone, requestId }; the master decides in OnPlayerPropertiesUpdate.
    /// A write returning true only means "sent": the taker heals when it READS the echo naming it. No RPC.
    ///
    /// Every client builds the packs itself (they are not networked objects) once each Tier III tower has
    /// registered, and draws them from the Room Property and the server clock alone, so all clients agree. A
    /// taken pack stays in place greyed out with no glow until it returns. A Tier III zone that is out of play
    /// after the map shrinks hides its pack and cannot be taken.
    /// </summary>
    public sealed class HealthPackManager : MonoBehaviourPunCallbacks
    {
        public static HealthPackManager Instance { get; private set; }

        public const string RequestKey = "hpReq";
        public static string PackKey(int zone) => "hp" + zone;

        /// <summary>How often a client without its player yet looks for it.</summary>
        private const float LocalPlayerSearchSeconds = 1f;

        [SerializeField, Tooltip("The health pack numbers: heal amount, respawn time, pickup radius, and the look.")]
        private HealthPackConfig config;

        private sealed class Pack
        {
            public int zone;
            public string key;             // "hp" + zone, built once
            public Vector3 centre;         // the ground point of the pack, in the zone's recess
            public GameObject root;
            public Transform cross;        // spins and bobs
            public Transform lean;         // holds the two bars, leaned back from upright
            public Renderer[] crossRenderers;
            public GameObject glow;
            public bool colourKnown;
            public bool shownReady;
            // Asking side (every client)
            public bool hasOutstanding;
            public float requestSentAt;
            public int attempts;
            // Master side
            public int[] pending;
            public float pendingUntil;
        }

        private readonly Dictionary<int, Pack> packs = new Dictionary<int, Pack>();
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;
        private Material crossMaterial;
        private Material glowMaterial;

        private float nextLocalSearchAt;
        private int nextRequestId;
        private int lastHealedRequestId;
        private PlayerHealth localHealth;
        private readonly Dictionary<int, int> lastHandledByActor = new Dictionary<int, int>();

        /// <summary>Diagnostics for tests and recordings.</summary>
        public int HealsApplied { get; private set; }
        public int RequestsSent { get; private set; }
        public int GrantsWritten { get; private set; }

        /// <summary>The pack numbers and colours (the minimap reads the ready and taken colours from here).</summary>
        public HealthPackConfig Config => config;

        public IEnumerable<int> PackZones => packs.Keys;
        public bool HasPack(int zone) => packs.ContainsKey(zone);
        public Vector3 PackCentre(int zone) => packs.TryGetValue(zone, out Pack p) ? p.centre : Vector3.zero;
        /// <summary>The cross is drawn (zone in play), ready or greyed.</summary>
        public bool IsCrossShown(int zone) => packs.TryGetValue(zone, out Pack p) && p.root.activeSelf;
        /// <summary>The cross is drawn green with its glow: ready to take.</summary>
        public bool IsReadyShown(int zone) => packs.TryGetValue(zone, out Pack p) && p.root.activeSelf && p.shownReady;
        public bool IsGlowShown(int zone) => packs.TryGetValue(zone, out Pack p) && p.glow.activeSelf;
        public int[] RoomValue(int zone) => packs.TryGetValue(zone, out Pack p) ? ReadRoomValue(p.key) : null;

        /// <summary>For the minimap: where a pack is and whether it is ready to take right now. False for a zone
        /// with no pack, or one that is out of play (its pack is hidden).</summary>
        public bool TryGetPack(int zone, out Vector3 worldPosition, out bool ready)
        {
            worldPosition = Vector3.zero;
            ready = false;
            if (!packs.TryGetValue(zone, out Pack pack) || !pack.root.activeSelf)
                return false;
            worldPosition = pack.centre;
            ready = pack.shownReady;
            return true;
        }

        private void Awake()
        {
            Instance = this;
            if (config == null)
                Debug.LogError($"[HealthPackManager] {name}: Health Pack Config is not assigned - there will be no health packs.");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (crossMaterial != null) Destroy(crossMaterial);
            if (glowMaterial != null) Destroy(glowMaterial);
        }

        private void Update()
        {
            if (config == null || !PhotonNetwork.InRoom || BuildingManager.Instance == null)
                return;

            EnsurePacks();

            int now = PhotonNetwork.ServerTimestamp;
            if (now == 0)
                return; // Server clock not synced yet: showing anything would be a guess.

            MatchDirector match = MatchDirector.Instance;
            foreach (Pack pack in packs.Values)
            {
                bool inPlay = match == null || !match.IsOutOfPlay(pack.zone);
                bool available = IsPackAvailable(pack, now, match);
                Show(pack, inPlay, available);
                if (inPlay)
                    AnimateCross(pack);
            }

            TryRequestLocal(now, match);
        }

        // ---- building and drawing ------------------------------------------------------------------

        private void EnsurePacks()
        {
            BuildingManager buildings = BuildingManager.Instance;
            // 2v2 Dominion has no health packs (its two Tier 3 zones get none); 3v3v3 and Conquest keep them. Nothing is built while the room's mode is
            // still unknown (the mode catalogue is not reachable in a new scene's first frames): this runs again next frame.
            bool wanted = Overpower.Dominion.DominionRules.MayBuildHealthPacks(Overpower.Dominion.DominionMode.IsKnown(),
                Overpower.Dominion.DominionMode.IsActive(), Overpower.Dominion.DominionMode.TeamCountOfCurrentRoom());
            // The tower's own tier, not TierOf: TierOf plays the centre as Tier III while a corner is cut.
            foreach (int zone in HealthPackRules.ZonesToBuild(wanted, buildings.ZoneCount, 3, buildings.BaseTierOf, z => packs.ContainsKey(z)))
            {
                if (!buildings.TryGetZoneCentre(zone, out Vector3 tower))
                    continue;
                packs[zone] = Build(zone, tower);
            }
        }

        /// <summary>The pack's spot for the zone whose tower is at <paramref name="tower"/>: the authored recess point
        /// (turned into that zone's third), or the tower itself when there is no arena or layout to read it from (a
        /// test scene).</summary>
        private static Vector3 SpotFor(Vector3 tower)
        {
            ArenaSymmetry arena = ArenaSymmetry.Active;
            if (arena == null || arena.layout == null)
            {
                Debug.LogError("[HealthPackManager] No ArenaSymmetry or no Arena Layout assigned to it: the health packs cannot " +
                               "find their recess spot and are placed on the towers themselves, where nobody can take them.");
                return tower;
            }
            return RadialSymmetry.NearestCopy(arena.layout.HealthPackPoint, arena.centre, tower);
        }

        private Pack Build(int zone, Vector3 tower)
        {
            // In the zone's Tier III recess, midway between the arena wall behind it and the yellow barrier across its
            // mouth. The spot is authored once (ArenaLayout.HealthPackPoint, Source third); the copy nearest this
            // zone's tower is the one in its recess, so the three packs are placed by the same rule.
            Vector3 spot = SpotFor(tower);
            float ground = CaptureRingView.GroundHeight(spot, 0.5f);
            var pack = new Pack { zone = zone, key = PackKey(zone), centre = new Vector3(spot.x, ground, spot.z) };

            pack.root = new GameObject("Health Pack (zone " + zone + ")");
            pack.root.transform.SetParent(transform, false);
            pack.root.transform.position = pack.centre;

            crossMaterial = crossMaterial != null ? crossMaterial : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            glowMaterial = glowMaterial != null ? glowMaterial : MakeTransparentUnlit();
            block = block ?? new MaterialPropertyBlock();

            // The cross: two boxes in an UPRIGHT plus (in a vertical plane, like the classic health sign), floating
            // above the spot, bobbing, and turning slowly about the vertical axis so it reads from the top-down
            // camera as it turns.
            var cross = new GameObject("Cross");
            cross.transform.SetParent(pack.root.transform, false);
            pack.cross = cross.transform;
            var lean = new GameObject("Lean");
            lean.transform.SetParent(cross.transform, false);
            pack.lean = lean.transform;
            float length = config.CrossSizeMetres, arm = length / 3f;
            pack.crossRenderers = new[]
            {
                MakeBox(lean.transform, "Horizontal bar", new Vector3(length, arm, arm), crossMaterial),
                MakeBox(lean.transform, "Vertical bar", new Vector3(arm, length, arm), crossMaterial),
            };

            // The glow: a flat disc on the ground.
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            glow.name = "Glow";
            DestroyImmediate(glow.GetComponent<Collider>());
            glow.transform.SetParent(pack.root.transform, false);
            glow.transform.localPosition = new Vector3(0f, config.GlowHeightMetres, 0f);
            glow.transform.localScale = new Vector3(config.GlowDiameterMetres, 0.005f, config.GlowDiameterMetres);
            var glowRenderer = glow.GetComponent<MeshRenderer>();
            glowRenderer.sharedMaterial = glowMaterial;
            glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
            block.SetColor(ColorId, config.GlowColour);
            glowRenderer.SetPropertyBlock(block);
            pack.glow = glow;
            return pack;
        }

        private static Renderer MakeBox(Transform parent, string boxName, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = boxName;
            DestroyImmediate(box.GetComponent<Collider>()); // never a solid, physics-blocking object
            box.transform.SetParent(parent, false);
            box.transform.localScale = size;
            var renderer = box.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        private static Material MakeTransparentUnlit()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            return material;
        }

        private void Show(Pack pack, bool inPlay, bool available)
        {
            if (pack.root.activeSelf != inPlay)
                pack.root.SetActive(inPlay);
            if (!inPlay)
                return;

            if (pack.glow.activeSelf != available)
                pack.glow.SetActive(available);

            if (pack.colourKnown && pack.shownReady == available)
                return;
            pack.colourKnown = true;
            pack.shownReady = available;
            block.SetColor(ColorId, available ? config.ReadyColour : config.TakenColour);
            foreach (Renderer r in pack.crossRenderers)
                r.SetPropertyBlock(block);
        }

        private void AnimateCross(Pack pack)
        {
            float t = Time.time;
            float bob = Mathf.Sin(t * config.BobCyclesPerSecond * Mathf.PI * 2f) * config.BobHeightMetres;
            pack.cross.localPosition = new Vector3(0f, config.FloatHeightMetres + bob, 0f);
            pack.cross.localRotation = Quaternion.Euler(0f, t * config.SpinDegreesPerSecond, 0f);
            pack.lean.localRotation = Quaternion.Euler(config.CrossLeanDegrees, 0f, 0f);
        }

        // ---- asking (every client, for its own player) ---------------------------------------------

        private void TryRequestLocal(int now, MatchDirector match)
        {
            if (localHealth == null)
            {
                if (Time.unscaledTime < nextLocalSearchAt) return;
                nextLocalSearchAt = Time.unscaledTime + LocalPlayerSearchSeconds;
                foreach (PlayerHealth candidate in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                    if (candidate.HasLocalAuthority) { localHealth = candidate; break; }
                if (localHealth == null) return;
            }

            Vector3 position = BodyPosition(localHealth);
            foreach (Pack pack in packs.Values)
            {
                if (FlatDistance(position, pack.centre) > config.PickupRadius)
                {
                    pack.attempts = 0;
                    pack.hasOutstanding = false;
                    continue;
                }

                bool inPlay = match == null || !match.IsOutOfPlay(pack.zone);
                bool available = IsPackAvailable(pack, now, match);
                if (!HealthPackRules.MayTake(localHealth.IsAlive, localHealth.Health, localHealth.MaxHealth,
                                             config.MinimumMissingHealth, available, inPlay))
                    continue;
                if (pack.attempts >= config.MaxAttemptsPerVisit)
                    continue;
                if (!HealthPackRules.MayRequestNow(pack.hasOutstanding, Time.unscaledTime - pack.requestSentAt, config.RetrySeconds))
                    continue;

                nextRequestId++;
                pack.hasOutstanding = true;
                pack.requestSentAt = Time.unscaledTime;
                pack.attempts++;
                RequestsSent++;
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { RequestKey, new[] { pack.zone, nextRequestId } } });
            }
        }

        /// <summary>The Rigidbody's own position: the Transform lags a physics step behind a teleport.</summary>
        private static Vector3 BodyPosition(PlayerHealth player)
        {
            Rigidbody body = player.GetComponent<Rigidbody>();
            return body != null ? body.position : player.transform.position;
        }

        /// <summary>Task 9e: this player's own request counter lives on this machine and starts at 0 in a fresh process, but
        /// the master remembers the last id it decided for this ACTOR (lastHandledByActor) and drops anything not above it.
        /// A rejoined player's "hpReq" Player Property still holds the last id it sent, so continue counting from there -
        /// otherwise its first few pack requests after a restart would look old and be ignored.</summary>
        public override void OnJoinedRoom()
        {
            if (PhotonNetwork.LocalPlayer == null
                || !PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(RequestKey, out object raw)
                || !(raw is int[] last) || last.Length < 2)
                return;

            nextRequestId = Overpower.Match.RejoinRules.SeedCounter(nextRequestId, last[1]);
            lastHealedRequestId = Overpower.Match.RejoinRules.SeedCounter(lastHealedRequestId, last[1]);
        }

        // ---- deciding (master only) ----------------------------------------------------------------

        public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (!PhotonNetwork.IsMasterClient || config == null || changedProps == null)
                return;
            if (!changedProps.TryGetValue(RequestKey, out object raw) || !(raw is int[] request) || request.Length < 2)
                return;

            int zone = request[0], requestId = request[1];
            if (lastHandledByActor.TryGetValue(targetPlayer.ActorNumber, out int last) && requestId <= last)
                return; // a request is decided once
            lastHandledByActor[targetPlayer.ActorNumber] = requestId;

            if (!packs.TryGetValue(zone, out Pack pack))
                return;
            int now = PhotonNetwork.ServerTimestamp;
            if (now == 0)
                return;

            bool alive = !(targetPlayer.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object a) && a is bool isAlive && !isAlive);
            MatchDirector match = MatchDirector.Instance;
            bool inPlay = match == null || !match.IsOutOfPlay(zone);
            bool inRange = RequesterInRange(targetPlayer.ActorNumber, pack);

            int[] roomValue = ReadRoomValue(pack.key);
            int[] basis = pack.pending != null && Time.unscaledTime < pack.pendingUntil ? pack.pending : roomValue;
            HealthPackRules.Decision decision = HealthPackRules.Decide(basis, now, alive, inPlay, inRange,
                                                                       targetPlayer.ActorNumber, requestId, config.RespawnMs, LiveAtMs(match));
            if (!decision.Granted)
                return;

            var props = new Hashtable { { pack.key, decision.NewValue } };
            // Check-and-set on what the master decided from (absent = never taken): a stale basis is refused by
            // the server instead of overwriting.
            var expected = new Hashtable { { pack.key, roomValue } };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props, expected))
                return;

            pack.pending = decision.NewValue;
            pack.pendingUntil = Time.unscaledTime + config.PendingWriteSeconds;
            GrantsWritten++;
        }

        private bool RequesterInRange(int actor, Pack pack)
        {
            foreach (PlayerHealth candidate in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (candidate.ActorNumber != actor) continue;
                return FlatDistance(BodyPosition(candidate), pack.centre) <= config.PickupRadius + config.HostSlackMetres;
            }
            return false;
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            // Pending writes and handled ids were the old master's. The Room Properties carry the rest.
            foreach (Pack pack in packs.Values) pack.pending = null;
            lastHandledByActor.Clear();
        }

        // ---- reading the echo (every client) -------------------------------------------------------

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            if (config == null || propertiesThatChanged == null || PhotonNetwork.LocalPlayer == null)
                return;

            foreach (Pack pack in packs.Values)
            {
                if (!propertiesThatChanged.TryGetValue(pack.key, out object raw) || !(raw is int[] value))
                    continue;

                pack.hasOutstanding = false;
                pack.pending = null;

                if (localHealth == null)
                    continue;
                if (!HealthPackRules.EchoIsMyFreshTake(value, PhotonNetwork.LocalPlayer.ActorNumber, nextRequestId,
                                                       lastHealedRequestId, PhotonNetwork.ServerTimestamp, config.RespawnMs))
                    continue;

                lastHealedRequestId = nextRequestId;
                if (localHealth.Heal(config.HealAmount) > 0f)
                    HealsApplied++;
            }
        }

        // ---- helpers -------------------------------------------------------------------------------

        private static int[] ReadRoomValue(string key)
        {
            Room room = PhotonNetwork.CurrentRoom;
            if (room == null) return null;
            return room.CustomProperties.TryGetValue(key, out object raw) ? raw as int[] : null;
        }

        /// <summary>0 until the match is live, then the server ms it went live: a take from before that no longer counts.</summary>
        private static int LiveAtMs(MatchDirector match) => match != null && match.IsLive ? match.LiveAtMs : 0;

        private bool IsPackAvailable(Pack pack, int now, MatchDirector match) =>
            HealthPackRules.IsAvailable(ReadRoomValue(pack.key), now, LiveAtMs(match), config.RespawnMs);

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
