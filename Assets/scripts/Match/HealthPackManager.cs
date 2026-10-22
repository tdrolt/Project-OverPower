using System.Collections.Generic;
using Overpower.Arena;
using Overpower.Data;
using Overpower.UI;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Match
{
    /// <summary>
    /// The health packs: a floating cross in each Tier III zone's recess and, while a corner is cut, one in the cut wall's
    /// recess (indexed after the zones'). Walk over one hurt and alive and it heals you, then it is gone for everyone until it returns.
    /// State: ONE Room Property per pack, "hp" + index, written ONLY by the master with a check-and-set; a player asks through
    /// its own Player Property "hpReq" and heals when it READS the echo naming it, never on the send. No RPC. Every client
    /// builds and draws the packs itself from the room and the server clock alone, so all agree; a pack whose zone the map
    /// shrink put out of play is hidden and cannot be taken. The decisions and the values' layout live in HealthPackRules.
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

        [SerializeField, Tooltip("The UI Theme: the packs take their healing green from it, and the green edge's material from its Arena Look.")]
        private UiTheme theme;

        private sealed class Pack
        {
            public int zone;
            public string key;             // "hp" + index, built once
            public bool recess;            // the cut wall's recess pack: "zone" is its index, not a zone
            public Vector3 centre;         // the pack's ground point
            public GameObject root;
            public HealthPackView view;
            // Asking side (every client)
            public bool hasOutstanding;
            public float requestSentAt;
            public int attempts;
            // Master side
            public int[] pending;
            public float pendingUntil;
        }

        private readonly Dictionary<int, Pack> packs = new Dictionary<int, Pack>();
        private System.Func<int, bool> hasPackNow; // made once: a lambda written at the call would be a new object every frame
        private Material solidMaterial; // the pads and crosses share it; each is coloured by a property block

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
        /// <summary>The cross is drawn (zone in play), ready or dimmed.</summary>
        public bool IsCrossShown(int zone) => packs.TryGetValue(zone, out Pack p) && p.root.activeSelf;
        /// <summary>The cross is drawn glowing: ready to take.</summary>
        public bool IsReadyShown(int zone) => packs.TryGetValue(zone, out Pack p) && p.root.activeSelf && p.view.ShownReady;
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
            ready = pack.view.ShownReady;
            return true;
        }

        private void Awake()
        {
            Instance = this;
            hasPackNow = zone => packs.ContainsKey(zone);
            if (config == null)
                Debug.LogError($"[HealthPackManager] {name}: Health Pack Config is not assigned - there will be no health packs.");
            if (theme == null)
                Debug.LogError($"[HealthPackManager] {name}: UI Theme is not assigned - the health packs will be drawn white, with no glowing edge.");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (solidMaterial != null) Destroy(solidMaterial);
        }

        private void Update()
        {
            if (config == null || BuildingManager.Instance == null)
                return;

            MatchDirector match = MatchDirector.Instance;
            EnsureRecessPack(match); // before the room check: leaving the room ends the cut, and its pack goes with it
            if (!PhotonNetwork.InRoom)
                return;

            EnsurePacks();

            int now = PhotonNetwork.ServerTimestamp;
            if (now == 0)
                return; // Server clock not synced yet: showing anything would be a guess.

            foreach (Pack pack in packs.Values)
            {
                bool inPlay = InPlay(pack, match);
                bool available = IsPackAvailable(pack, now, match);
                Show(pack, inPlay, available);
                if (inPlay)
                    pack.view.Animate(Time.time);
            }

            TryRequestLocal(now, match);
        }

        // ---- building and drawing ------------------------------------------------------------------

        private bool packsSettled; // nothing more to build in this room: EnsurePacks returns at once instead of allocating a list every frame

        private int recessPack = -1;

        /// <summary>The index of the pack in the cut wall's recess, or -1 while there is none.</summary>
        public int RecessPackIndex => recessPack;

        private void EnsurePacks()
        {
            if (packsSettled) return;
            BuildingManager buildings = BuildingManager.Instance;
            // 2v2 Dominion has no health packs (its two Tier 3 zones get none); 3v3v3 and Conquest keep them. Nothing is built while the room's mode is
            // still unknown (the mode catalogue is not reachable in a new scene's first frames): this runs again next frame.
            // The tower's own tier, not TierOf: TierOf plays the centre as Tier III while a corner is cut.
            bool settled;
            foreach (int zone in HealthPackRules.ZonesToBuild(Overpower.Dominion.DominionMode.IsKnown(), Overpower.Dominion.DominionMode.IsActive(),
                         Overpower.Dominion.DominionMode.TeamCountOfCurrentRoom(), buildings.ZoneCount, buildings.BaseTierOf, hasPackNow, out settled))
            {
                if (!buildings.TryGetZoneCentre(zone, out Vector3 tower))
                    continue;
                packs[zone] = Build(zone, SpotFor(tower), recess: false);
            }
            packsSettled = settled;
        }

        /// <summary>The recess pack follows the wall this client has built (ArenaPhaseTwoCut, from the room's cut), so it
        /// appears when that corner closes, on a late join too, and goes when the cut does.</summary>
        private void EnsureRecessPack(MatchDirector match)
        {
            ArenaPhaseTwoCut wall = ArenaPhaseTwoCut.Active;
            PhaseTwoCutGeometry cut = wall != null ? wall.Geometry : null;
            int wanted = HealthPackRules.RecessPackIndex(Overpower.Dominion.DominionMode.IsActive(),
                match != null ? match.CutTeam : PhaseTwoCutRules.NoCut,
                wall != null ? wall.AppliedCutTeam : PhaseTwoCutRules.NoCut,
                cut != null && cut.HasRecess, BuildingManager.Instance.ZoneCount);
            if (wanted != recessPack)
                SetRecessPack(wanted, wanted >= 0 ? cut.RecessCentre : Vector2.zero);
        }

        /// <summary>Takes away the recess pack there was and builds the one at <paramref name="index"/> (-1: none) on
        /// <paramref name="spot"/> (world X, Z).</summary>
        internal void SetRecessPack(int index, Vector2 spot)
        {
            if (recessPack >= 0 && packs.TryGetValue(recessPack, out Pack old))
            {
                packs.Remove(recessPack);
                if (Application.isPlaying) Destroy(old.root);
                else DestroyImmediate(old.root);
            }
            recessPack = index;
            if (index >= 0)
                packs[index] = Build(index, new Vector3(spot.x, 0f, spot.y), recess: true);
        }

        private static bool InPlay(Pack pack, MatchDirector match) =>
            HealthPackRules.PackInPlay(pack.recess, match != null && match.IsOutOfPlay(pack.zone));

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

        // A zone's pack stands in its Tier III recess, midway between the arena wall behind it and the low barrier across
        // its mouth. That spot is authored once (ArenaLayout.HealthPackPoint, Source third); the copy nearest this zone's
        // tower is the one in its recess, so the three packs are placed by the same rule.
        private Pack Build(int zone, Vector3 spot, bool recess)
        {
            float ground = CaptureRingView.GroundHeight(spot, 0.5f);
            var pack = new Pack { zone = zone, key = PackKey(zone), recess = recess, centre = new Vector3(spot.x, ground, spot.z) };

            solidMaterial = solidMaterial != null ? solidMaterial : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            string packName = recess ? "Health Pack (recess " + zone + ")" : "Health Pack (zone " + zone + ")";
            pack.view = HealthPackView.Create(transform, pack.centre, packName, config, theme, solidMaterial);
            pack.root = pack.view.gameObject;
            return pack;
        }

        private void Show(Pack pack, bool inPlay, bool available)
        {
            if (pack.root.activeSelf != inPlay)
                pack.root.SetActive(inPlay);
            if (inPlay)
                pack.view.SetReady(available);
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

                bool inPlay = InPlay(pack, match);
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

        /// <summary>This player's own request counter lives on this machine and starts at 0 in a fresh process, but
        /// the master remembers the last id it decided for this ACTOR (lastHandledByActor) and drops anything not above it.
        /// A rejoined player's "hpReq" Player Property still holds the last id it sent, so continue counting from there -
        /// otherwise its first few pack requests after a restart would look old and be ignored.</summary>
        public override void OnJoinedRoom()
        {
            packsSettled = false; // a new room may be another mode
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
            bool inPlay = InPlay(pack, match);
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
