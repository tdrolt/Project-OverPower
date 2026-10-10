using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Project.Tools.DictionaryHelp;
using Photon.Pun;
using Photon.Realtime;
using Overpower.Match;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// Territory state for the whole match: who owns each zone, since when, who held it before.
///
/// Authority (CODING-STANDARDS §5): the master client is the ONLY writer. It writes the whole state
/// as one TerritorySnapshot into the room's Custom Properties, and every client - the master
/// included - applies what the room sends back. Why Room Properties and not the old buffered RPC:
/// a player joining mid-match reads one value instead of replaying every capture of the match, and
/// the state stays in the room when the master leaves, so the next master carries on from it.
///
/// TowerDictionary stays as the in-memory mirror existing code reads (PlayerLifecycle's capital
/// check, CheckTerritoryWin). It is only ever changed by Apply, from the replicated snapshot.
public class BuildingManager : MonoBehaviourPunCallbacks
{
    public static BuildingManager Instance { get; private set; }

    [SerializeField] public SerializableDictionary<int, TowerData> TowerDictionary;

    /// <summary>One capital zone of a map: the zone id and the team whose spawn it is.</summary>
    [Serializable]
    public struct CapitalZone
    {
        [Tooltip("The zone (tower) id of this capital.")]
        public int zone;
        [Tooltip("The team whose capital and spawn this zone is.")]
        public int team;
    }

    [SerializeField, Tooltip("This map's capitals: which zone is each team's capital (and spawn). The triangle arena has three (zones 6, 7 and 8 for teams 0, 1 and 2). " +
             "A two-team map lists two. Every scene that plays a match carries its own list.")]
    private CapitalZone[] capitalZones = { new CapitalZone { zone = 6, team = 0 }, new CapitalZone { zone = 7, team = 1 }, new CapitalZone { zone = 8, team = 2 } };

    private Dictionary<int, int> cathedralBuildingIDs;

    /// <summary>Zone id to team id for this scene's capitals (the Capital Zones list, read once).</summary>
    public Dictionary<int, int> CathedralBuildingIDs
    {
        get
        {
            if (cathedralBuildingIDs == null)
            {
                var pairs = new List<(int zone, int team)>(capitalZones != null ? capitalZones.Length : 0);
                if (capitalZones != null)
                    foreach (CapitalZone capital in capitalZones) pairs.Add((capital.zone, capital.team));
                cathedralBuildingIDs = Overpower.Lobby.SceneMapRules.CapitalLookup(pairs);
            }
            return cathedralBuildingIDs;
        }
    }

    // Towers register themselves here so ownership changes can drive their flag colour. TowerData.Building was
    // meant for this but is null on every tower in the scene, and a new tower would need wiring by hand; registering
    // is one line in BuildingCapture.Start and cannot be forgotten.
    private readonly Dictionary<int, BuildingCapture> captures = new Dictionary<int, BuildingCapture>();

    // Latch, so the win is announced once rather than on every subsequent ownership change.
    private bool territoryWinAnnounced = false;

    private TerritoryMap map;
    private TerritorySnapshot current;

    // Master only. Room Properties come back from the server a network round trip after they are
    // set, and Current only changes when they do. If the master changed two zones inside that
    // window (two captures in one frame, or the phase rules neutralising every Tier-3 zone at
    // once), building the second change from Current would silently undo the first. So while the
    // master still has writes on their way, the next write builds on the last one it sent.
    private TerritorySnapshot lastWritten;
    private int writesAwaitingEcho;

    // Every client's picture of each zone's capture progress, decoded from the room's int[] arrays. Null until this
    // client has read them at least once; CaptureProgressOf reads that as Idle, same convention as Current above.
    // Indices beyond what the room holds (a zone nobody has tried to capture) also read as Idle (ApplyCaptureProgress,
    // CaptureProgressOf).
    private CaptureProgress[] currentProgress;

    // Master only, the same purpose as lastWritten/writesAwaitingEcho above but a SEPARATE echo window: a territory
    // write and a capture-progress write are separate SetCustomProperties calls with disjoint keys
    // (PublishCaptureProgress), so they echo back independently. A fresh master's copy starts null
    // (OnMasterClientSwitched); PublishCaptureProgress then falls back to currentProgress (this client's last-read
    // echo, still correct) rather than assuming an empty room.
    private CaptureProgress[] progressLastWritten;
    private int progressWritesAwaitingEcho;

    private Coroutine initialWrite;

    // How long the master waits for the server clock before writing the first snapshot anyway.
    // Not a gameplay number: the clock normally arrives within a frame or two of connecting.
    private const float ServerClockWaitSeconds = 5f;

    /// Who may capture what. Built once from the scene's TowerDictionary adjacency and the capitals.
    public TerritoryMap Map => map;

    /// The territory state every client agrees on. Null until this client has read the room's
    /// snapshot (just after joining), so readers must treat null as "not known yet".
    public TerritorySnapshot Current => current;

    /// Current's owner of every zone, as the dictionary TerritoryMap.MayCapture reads. Built once
    /// per snapshot, because OwnersByZone() allocates a new one on every call and every tower asks
    /// every frame while someone is capturing. Null whenever Current is. Read-only by convention.
    public IReadOnlyDictionary<int, int> CurrentOwners { get; private set; }

    /// Highest tower id + 1: the length of every array in the snapshot.
    public int ZoneCount { get; private set; }

    /// The player's body radius in world metres, read once from RoomManager's Player Prefab (its
    /// CapsuleCollider). Capture triggers fire on the edge of a body, so each tower shrinks its
    /// trigger by this much to count from the player's centre, like presence, regen and the shop.
    /// 0, with one error, if it can't be read.
    public float PlayerBodyRadius
    {
        get
        {
            if (playerBodyRadius < 0f)
                playerBodyRadius = ReadPlayerBodyRadius();
            return playerBodyRadius;
        }
    }

    private float playerBodyRadius = -1f; // -1 = not read yet

    // Public: also called directly by CaptureRadiusSceneTests (EveryZonesTriggerStaysPositive), which needs the body
    // radius in edit mode. This method has no Play Mode dependency, unlike the PlayerBodyRadius property's cache,
    // which only exists on a live BuildingManager.Instance (set in Awake).
    public static float ReadPlayerBodyRadius()
    {
        RoomManager roomManager = FindFirstObjectByType<RoomManager>();
        GameObject prefab = roomManager != null ? roomManager.playerPrefab : null;
        CapsuleCollider body = prefab != null ? prefab.GetComponent<CapsuleCollider>() : null;
        if (body == null)
        {
            Debug.LogError("[TOWER] can't read the player's body radius (RoomManager, its Player Prefab, or the " +
                           "prefab's CapsuleCollider is missing) - capture triggers will reach up to a body's " +
                           "width further than presence, regen and the shop.");
            return 0f;
        }

        // A capsule's radius scales with the larger of the two axes across its length.
        Vector3 scale = prefab.transform.localScale;
        float across = body.direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
                     : body.direction == 1 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
                     : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        return body.radius * across;
    }

    /// This client's last-known capture progress for a zone - CaptureProgress.Idle if nothing has
    /// been read yet or the zone is out of range. Extrapolate the live fill with
    /// progress.Evaluate(PhotonNetwork.ServerTimestamp) - see CaptureProgress's own class comment.
    public CaptureProgress CaptureProgressOf(int zone) =>
        currentProgress != null && zone >= 0 && zone < currentProgress.Length ? currentProgress[zone] : CaptureProgress.Idle;

    /// Raised on every client when an applied snapshot changes a zone's owner:
    /// (zone, oldOwner, newOwner, snapshot). NOT raised for the snapshot a client reads when it
    /// joins - that is the match as it already was, and treating it as fresh captures would, for
    /// example, pay a late joiner bounties that were settled before they arrived.
    public event Action<int, int, int, TerritorySnapshot> OwnershipChanged;

    /// <summary>Raised on EVERY client (master included) whenever a decoded capture-progress publish changes a zone's
    /// team or rate (ApplyCaptureProgressIfPresent, via CaptureProgress.NeedsRepublishComparedTo, the predicate a tower
    /// uses to decide whether to publish). MatchTelemetry is the only listener and logs only while
    /// PhotonNetwork.IsMasterClient when the event fires: a master-only LOG guard, not a master-only RAISE, is what
    /// survives a master switch cleanly.</summary>
    public event Action<int, CaptureProgress, CaptureProgress> CaptureProgressChanged;

    /// <summary>Raised on the master only, from inside SetCaptured, once the write that carries it actually reaches
    /// Photon, the moment a capture pays out a bounty (bountyPaid > 0): (zone, paidTeam, payingTeam, amount, heldMs).
    /// paidTeam just captured the zone and received the bounty; payingTeam held it too long before losing it (the team
    /// the bounty is conceptually paid BY). The payout is computed in SetCaptured, not passed in (see its comment).
    /// Not raised for a zero-bounty capture, or if the room never got the write.</summary>
    public event Action<int, int, int, int, int> BountyPaid;

    /// <summary>Raised on the master only, from inside SetCaptured, once the capture write has reached Photon:
    /// (zone, newOwner, previousOwner, previousHeldMs). The previous owner and the length of the hold it ended are read from the write
    /// basis BEFORE the capture clears them (WithCapture forgets them), so a mode with its own bounty (Dominion pays points) can judge the
    /// hold against its own number. OwnershipChanged cannot say it: it only carries the new snapshot, where they are already cleared.</summary>
    public event Action<int, int, int, int> CaptureWritten;

    /// How many OwnershipChanged events this client has raised. Diagnostic only: lets a test read
    /// from outside that a late joiner's first read raised none.
    public int OwnershipChangedRaisedCount { get; private set; }

    /// How many times THIS client has published capture progress (master only - stays 0 on every other client).
    /// Diagnostic only, like OwnershipChangedRaisedCount: lets a test count publishes during a clean solo capture
    /// from outside, instead of grepping the console log.
    public int CaptureProgressPublishCount { get; private set; }

    /// <summary>Whether this zone can be captured or drained in this room: a capital cannot in Dominion (DominionTerritoryRules). The capital
    /// still counts as held for adjacency; this only stops its own capture and drain. Its ring and its "under attack" warning still show: an enemy
    /// standing in it warns the team but never closes the link to the zones next to it (ZoneThreat.ZoneClosesLink).</summary>
    public bool IsCapturableZone(int zone)
    {
        bool isCapital = Map != null && Map.CapitalTeamOf(zone) != TerritoryMap.Neutral;
        return Overpower.Dominion.DominionTerritoryRules.IsCapturable(Overpower.Dominion.DominionMode.IsActive(), isCapital);
    }

    /// <summary>Whether the zone is a team's spawn in this room (a Dominion capital): its tower gets the spawn look and the minimap leaves it out.</summary>
    public bool IsSpawnZone(int zone)
    {
        bool isCapital = Map != null && Map.CapitalTeamOf(zone) != TerritoryMap.Neutral;
        return Overpower.Dominion.DominionTerritoryRules.IsSpawn(Overpower.Dominion.DominionMode.IsActive(), isCapital);
    }

    /// <summary>The tower's own tier as set in the scene: PhaseTwoCutRules finds the cut from this, never from the
    /// phase-two stand-in (TierOf below), or a cut corner's Tier III towers would stop reading as Tier III to the very
    /// rule that finds them. 0 while the tower with that id hasn't registered yet (RegisterCapture runs in
    /// BuildingCapture.Start): the same "not tiered yet" reading TierOf/GoldMath.TeamIncomePerSecond rely on, so an
    /// unregistered Tier II or III never matches a tier test and is simply not cut for that one frame (the cut capital
    /// itself is matched by its id, not its tier).</summary>
    public int BaseTierOf(int zone) =>
        captures.TryGetValue(zone, out BuildingCapture capture) && capture != null ? capture.tier : 0;

    // IsCutActive on its own line so both TierOf and TierByZone's cache-invalidation check (tierCacheCutActive) ask
    // the exact same question.
    private static bool IsCutActive => MatchDirector.Instance != null && MatchDirector.Instance.CutTeam >= 0;

    /// The tier a zone plays as right now (1..4): BaseTierOf's tier, except the centre plays as Tier III while a
    /// corner is cut (PhaseTwoCutRules.EffectiveTier). EffectiveTier only touches tier 4, so a not-yet-registered
    /// tower's BaseTierOf 0 passes through unchanged and still reads as "not a tiered zone" to GoldMath.TeamIncomePerSecond.
    public int TierOf(int zone) => PhaseTwoCutRules.EffectiveTier(BaseTierOf(zone), IsCutActive);

    // Backing store for TierByZone. Allocated ONCE (ZoneCount is fixed for the whole match) and filled IN PLACE by
    // RebuildTierByZoneCache, on every RegisterCapture and on the first TierByZone() call after IsCutActive flips (the
    // only two things that change a zone's tier). Never reassigned: a caller that keeps the reference (the shop gate)
    // needs the SAME array to pick up either kind of change, and a fresh array per call would cost GoldWallet an
    // allocation on every player's every Update.
    private int[] tierByZoneCache;
    private bool tierCacheCutActive;

    /// Tier 1..4 per zone id, index = zone id, length ZoneCount - the array shape GoldMath.TeamIncomePerSecond's
    /// tierByZone parameter wants. Read-only by convention: every caller gets the same array instance for the whole
    /// match (tierByZoneCache), so a caller that holds the reference sees a late tower's tier the moment it registers.
    /// A corner being cut is picked up on the NEXT call, since this checks IsCutActive only when it is called.
    public int[] TierByZone()
    {
        if (tierByZoneCache == null || tierCacheCutActive != IsCutActive)
            RebuildTierByZoneCache();
        return tierByZoneCache;
    }

    private void RebuildTierByZoneCache()
    {
        if (tierByZoneCache == null || tierByZoneCache.Length != ZoneCount)
            tierByZoneCache = new int[ZoneCount];
        tierCacheCutActive = IsCutActive;
        for (int zone = 0; zone < ZoneCount; zone++)
            tierByZoneCache[zone] = TierOf(zone);
    }

    /// <summary>How many players BuildingCapture counts inside this zone (its PlayersInZoneCount): the `capture`
    /// telemetry event's "players" field. 0 for a zone id with no registered tower, same convention as TierOf.</summary>
    public int PlayersInZone(int zone) =>
        captures.TryGetValue(zone, out BuildingCapture capture) && capture != null ? capture.PlayersInZoneCount : 0;

    /// <summary>Finds which registered zone position stands inside (flat XZ distance to the tower within its own
    /// CaptureRadius) - the "which zone am I in" question health regen, the shop gate and OverPower's "near a zone"
    /// check all ask. Capture rings are not meant to overlap, but if two ever do the nearest centre wins rather than
    /// an arbitrary dictionary order. No allocation: a plain foreach over the existing captures dictionary.</summary>
    public bool TryGetZoneAt(Vector3 position, out int zoneId)
    {
        zoneId = -1;
        float bestDistance = float.PositiveInfinity;
        foreach (KeyValuePair<int, BuildingCapture> pair in captures)
        {
            BuildingCapture capture = pair.Value;
            if (capture == null) continue;

            float distance = FlatDistance(position, capture.transform.position);
            if (distance > capture.CaptureRadius) continue;

            // A cut Tier III's capture area pokes through the new wall: standing near the wall, on the far side, must
            // not count as being in a zone that is out of play. Checked AFTER the radius test so it only runs for a
            // position already inside a zone's own ring (IsOutOfPlay's answer doesn't depend on distance).
            if (MatchDirector.Instance != null && MatchDirector.Instance.IsOutOfPlay(pair.Key)) continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                zoneId = pair.Key;
            }
        }
        return zoneId >= 0;
    }

    /// <summary>The world position of a zone's tower, its capture centre. False while that tower hasn't registered
    /// yet (RegisterCapture runs in BuildingCapture.Start). The minimap places its bubbles from this.</summary>
    public bool TryGetZoneCentre(int zone, out Vector3 centre)
    {
        if (captures.TryGetValue(zone, out BuildingCapture capture) && capture != null)
        {
            centre = capture.transform.position;
            return true;
        }
        centre = default;
        return false;
    }

    /// <summary>What a long hold of this zone pays in Conquest (its tier's capture bounty and the hold time, as SetCaptured gets them) and the UiTheme
    /// the tower was built with. False while the tower has not registered.</summary>
    public bool TryGetZoneBounty(int zone, out int tierBounty, out float holdSeconds, out Overpower.UI.UiTheme theme)
    {
        tierBounty = 0;
        holdSeconds = 0f;
        theme = null;
        if (!captures.TryGetValue(zone, out BuildingCapture capture) || capture == null)
            return false;
        tierBounty = capture.TierBounty;
        holdSeconds = capture.BountyHoldSeconds;
        theme = capture.theme;
        return true;
    }

    /// <summary>The world height of the top of the zone's tower (the highest point of its look: columns, crown, plinth), so
    /// something can float just above it. False while the tower has not registered or has no look.</summary>
    public bool TryGetZoneTowerTopY(int zone, out float topY)
    {
        topY = 0f;
        if (!captures.TryGetValue(zone, out BuildingCapture capture) || capture == null)
            return false;
        var look = capture.GetComponentInChildren<Overpower.Arena.TowerLook>(true);
        if (look == null)
            return false;
        bool any = false;
        foreach (Renderer renderer in look.GetComponentsInChildren<Renderer>(true))
        {
            float top = renderer.bounds.max.y;
            topY = any ? Mathf.Max(topY, top) : top;
            any = true;
        }
        return any;
    }

    /// <summary>The solid capsule of the zone's tower (its radius and the tower's world scale), for the "tower in sight" spots
    /// round it. Falls back to the authored 2.6 m at scale 1 when the tower has no capsule.</summary>
    public bool TryGetZoneTowerCapsule(int zone, out float radius, out Vector3 lossyScale)
    {
        radius = 2.6f;
        lossyScale = Vector3.one;
        if (!captures.TryGetValue(zone, out BuildingCapture capture) || capture == null)
            return false;
        var look = capture.GetComponentInChildren<Overpower.Arena.TowerLook>(true);
        var capsule = look != null ? look.GetComponent<CapsuleCollider>() : null;
        if (capsule != null)
        {
            radius = capsule.radius;
            lossyScale = capsule.transform.lossyScale;
        }
        return true;
    }

    /// <summary>How far position is from the edge of the nearest zone teamId owns - 0 while standing
    /// inside one, PositiveInfinity if the team owns nothing (or the room's territory state has not
    /// been read yet). Same building block as TryGetZoneAt, reused by the shop's "in your own
    /// territory" gate and OverPower's "near a zone your team owns" range check.</summary>
    public float DistanceToOwnedZoneEdge(Vector3 position, int teamId)
    {
        float best = float.PositiveInfinity;
        // A team below 0 means "unknown" (e.g. the spawn frame, before this player's own team Custom Property has
        // arrived), never "owns nothing", which current.OwnerOf(zone) also reports as -1 for every NEUTRAL zone:
        // without this guard teamId=-1 read as owning every neutral zone, and a real distance to one would come
        // back instead of the infinity an unknown team should always report.
        if (current == null || teamId < 0)
            return best;

        foreach (KeyValuePair<int, BuildingCapture> pair in captures)
        {
            BuildingCapture capture = pair.Value;
            if (capture == null || current.OwnerOf(pair.Key) != teamId) continue;

            float distanceToEdge = FlatDistance(position, capture.transform.position) - capture.CaptureRadius;
            if (distanceToEdge < 0f) distanceToEdge = 0f;
            if (distanceToEdge < best) best = distanceToEdge;
        }
        return best;
    }

    // XZ-plane distance only: territory is measured on the ground, not by how far above/below a
    // tower's pivot a player happens to be standing (a balcony, a slope).
    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public void RegisterCapture(int buildingID, BuildingCapture capture)
    {
        captures[buildingID] = capture;

        // This tower's tier just went from 0 ("not registered yet") to its real value - the one
        // thing that can make TierByZone's cached array stale.
        RebuildTierByZoneCache();

        // A tower missing from TowerDictionary has no adjacency, so the territory rules can never
        // let anyone capture it. Say so once here instead of silently refusing every entry.
        if (!TowerDictionary.ContainsKey(buildingID))
            Debug.LogError($"[TOWER] tower {buildingID} is not in BuildingManager's TowerDictionary - " +
                           "nobody will be able to capture it. Add an entry with its adjacent towers.", capture);

        // The room's snapshot may already have been applied before this tower's Start ran.
        if (current != null)
        {
            int owner = current.OwnerOf(buildingID);
            capture.ApplyOwnerVisual(owner >= 0, owner);
            capture.SyncFromReplicated(owner);
        }
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        } else
        {
            Destroy(gameObject);
            return;
        }

        BuildMap();
        TowerBountyView.AttachTo(gameObject);

        // MatchDirector needs no scene footprint and no PhotonView: it only reads and writes Room Properties, the
        // same authority model this class's own territory state uses. Added here at runtime on this same GameObject
        // (which already hosts MatchTelemetry and ZonePresenceTracker) rather than placed in the scene, since it has
        // nothing scene-specific to read or write.
        if (GetComponent<MatchDirector>() == null)
            gameObject.AddComponent<MatchDirector>();

        // What this client's team knows about each zone, for the displays only. Same reasoning as
        // MatchDirector above: no scene footprint, so it is added here on the same GameObject.
        if (GetComponent<Overpower.Vision.ZoneKnowledge>() == null)
            gameObject.AddComponent<Overpower.Vision.ZoneKnowledge>();

        // The centre scan (wave on the ground, dots and zone refresh for the team that held the centre when the wave started; the countdown above the tower is for everyone). Same reasoning.
        if (GetComponent<Overpower.Vision.CentreScan>() == null)
            gameObject.AddComponent<Overpower.Vision.CentreScan>();

        // The round flow (rounds, breaks, the match winner). Same reasoning as MatchDirector: no scene footprint; it does nothing
        // unless the room is a Dominion room.
        if (GetComponent<Overpower.Dominion.DominionDirector>() == null)
            gameObject.AddComponent<Overpower.Dominion.DominionDirector>();
    }

    void Start()
    {
        // Normally the room is joined after this scene has started, and OnJoinedRoom does this.
        if (PhotonNetwork.InRoom)
            ReadOrCreateRoomSnapshot();
    }

    private void BuildMap()
    {
        var zones = new List<(int zoneId, IEnumerable<int> adjacent)>();
        int highestId = -1;

        foreach (KeyValuePair<int, TowerData> tower in TowerDictionary)
        {
            zones.Add((tower.Key, tower.Value.Adjacents));
            highestId = Mathf.Max(highestId, tower.Key);
        }

        var capitals = new List<(int zoneId, int teamId)>();
        foreach (KeyValuePair<int, int> capital in CathedralBuildingIDs)
        {
            capitals.Add((capital.Key, capital.Value));
            highestId = Mathf.Max(highestId, capital.Key);
        }

        map = new TerritoryMap(zones, capitals);
        ZoneCount = highestId + 1;
    }

    // ---------------------------------------------------------------- reading the room

    public override void OnJoinedRoom()
    {
        ReadOrCreateRoomSnapshot();
    }

    public override void OnLeftRoom()
    {
        // The next room is a different match; nothing from this one may leak into it.
        current = null;
        CurrentOwners = null;
        Overpower.Vision.ZoneKnowledge.ResetKnowledge(); // the next room's team starts out knowing that room's live state
        lastWritten = null;
        writesAwaitingEcho = 0;
        currentProgress = null;
        progressLastWritten = null;
        progressWritesAwaitingEcho = 0;
        territoryWinAnnounced = false; // A latch from the match just left must not block the next one's own territory win.
        if (initialWrite != null)
        {
            StopCoroutine(initialWrite);
            initialWrite = null;
        }
    }

    private void ReadOrCreateRoomSnapshot()
    {
        // Capture progress has no "create": a fresh room simply has nobody capturing anything, so
        // CaptureProgressOf reads Idle everywhere until the first real publish. Read whatever a
        // room already in progress (a late joiner's case) has.
        ApplyCaptureProgressIfPresent(PhotonNetwork.CurrentRoom.CustomProperties);

        if (TerritorySnapshot.TryRead(PhotonNetwork.CurrentRoom.CustomProperties, ZoneCount, out TerritorySnapshot snapshot))
        {
            // The match as it already is: no events (see OwnershipChanged).
            Apply(snapshot, raiseEvents: false);
            return;
        }

        if (PhotonNetwork.IsMasterClient && initialWrite == null)
            initialWrite = StartCoroutine(WriteInitialSnapshotWhenClockIsReady());
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null)
            return;

        // Territory and capture progress are two separate publishers writing disjoint key sets in
        // separate SetCustomProperties calls (see the class comment on PublishCaptureProgress) -
        // both can legitimately be present, on their own or together, in one update.
        if (propertiesThatChanged.ContainsKey(TerritorySnapshot.OwnersKey))
        {
            if (writesAwaitingEcho > 0)
                writesAwaitingEcho--;

            if (TerritorySnapshot.TryRead(propertiesThatChanged, ZoneCount, out TerritorySnapshot snapshot))
                Apply(snapshot, raiseEvents: true);
        }

        if (propertiesThatChanged.ContainsKey(CaptureProgress.TeamKey))
        {
            if (progressWritesAwaitingEcho > 0)
                progressWritesAwaitingEcho--;

            ApplyCaptureProgressIfPresent(propertiesThatChanged);
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // Pending-write bookkeeping belonged to the old master's writes, not ours.
        lastWritten = null;
        writesAwaitingEcho = 0;
        progressLastWritten = null;
        progressWritesAwaitingEcho = 0;

        // Who is standing in which zone, capture progress, decay and cooldown lived only on the
        // old master's machine. Every client resets its towers from the replicated owners, and
        // re-reports its own player if it is standing in one, so the new master can carry on.
        // A capture that was part-way through starts again from zero.
        if (current != null)
        {
            foreach (KeyValuePair<int, BuildingCapture> pair in captures)
                if (pair.Value != null)
                    pair.Value.OnMasterClientChanged(current.OwnerOf(pair.Key));
        }

        // The old master may have left before its first snapshot reached the room.
        if (PhotonNetwork.IsMasterClient && current == null)
            ReadOrCreateRoomSnapshot();

        // The room may still show a capture rate the OLD master last published (a capture in progress when it left).
        // This new master's progressLastWritten (cleared above) is empty, and every tower's per-frame republish gate
        // (BuildingCapture.lastPublishedProgress) only compares against ITS OWN prior publishes, which on a client that
        // has never been master is still CaptureProgress.Idle: a tower whose real state is also idle after the reset
        // above would never think it needs to say so. Force one publish per zone here instead of trusting that gate.
        if (PhotonNetwork.IsMasterClient)
        {
            foreach (KeyValuePair<int, BuildingCapture> pair in captures)
                pair.Value?.RepublishProgressNow();
        }
    }

    /// Makes this client's picture of the territory match a snapshot from the room.
    private void Apply(TerritorySnapshot snapshot, bool raiseEvents)
    {
        TerritorySnapshot previous = current;
        current = snapshot;
        CurrentOwners = snapshot.OwnersByZone();
        bool firstRead = previous == null;

        // On the first read every zone counts as changed, so towers and TowerDictionary drop the
        // scene's starting values for the room's real state.
        List<int> changed = snapshot.ZonesWhoseOwnerChangedSince(previous);

        foreach (int zone in changed)
        {
            int newOwner = snapshot.OwnerOf(zone);

            if (TowerDictionary.TryGetValue(zone, out TowerData tower))
            {
                // Keeps the existing "captured flag + last team" meaning: a neutral tower still
                // remembers which team last held it. ApplyOwnerVisual and PlayerLifecycle's
                // capital check were written against that.
                tower.isCaptured = newOwner >= 0;
                if (newOwner >= 0)
                    tower.controllingTeam = newOwner;

                // Remove + Add: SerializableDictionary hides the indexer with a read-only one.
                TowerDictionary.Remove(zone);
                TowerDictionary.Add(zone, tower);
            }

            // Only zones whose owner changed: resyncing every tower would wipe the master's
            // progress on a capture that is still going on somewhere else.
            if (captures.TryGetValue(zone, out BuildingCapture capture) && capture != null)
            {
                capture.ApplyOwnerVisual(newOwner >= 0, newOwner);
                capture.SyncFromReplicated(newOwner);
            }

            if (!firstRead)
                Debug.Log($"[TOWER] {zone} -> {(newOwner >= 0 ? $"team {newOwner}" : "neutral")}");
        }

        if (firstRead)
            Debug.Log($"[TOWER] territory read from the room: owners [{string.Join(",", OwnersOf(snapshot))}]");

        // Raised after every zone above is updated, so a listener sees the whole new state.
        if (raiseEvents && !firstRead)
        {
            foreach (int zone in changed)
                RaiseOwnershipChanged(zone, previous.OwnerOf(zone), snapshot.OwnerOf(zone), snapshot);
        }

        CheckTerritoryWin();
    }

    private void RaiseOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot)
    {
        OwnershipChangedRaisedCount++;
        if (OwnershipChanged == null)
            return;

        // One faulty listener (a HUD, the wallet) must not stop the others hearing about a capture.
        foreach (Delegate listener in OwnershipChanged.GetInvocationList())
        {
            try
            {
                ((Action<int, int, int, TerritorySnapshot>)listener)(zone, oldOwner, newOwner, snapshot);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }
    }

    private static IEnumerable<int> OwnersOf(TerritorySnapshot snapshot)
    {
        for (int zone = 0; zone < snapshot.ZoneCount; zone++)
            yield return snapshot.OwnerOf(zone);
    }

    // ---------------------------------------------------------------- writing (master only)

    /// Master only: the zone now belongs to this team. tierBounty/holdMs are this zone's tier
    /// numbers (TerritoryConfig.ForTier(tier).captureBounty, BountyHoldSeconds*1000). The payout
    /// (BountyRule.PayoutOnCapture) is worked out IN HERE, from the same basis snapshot this write
    /// builds on, rather than handed in from the caller's own copy of Current. Current can lag one
    /// echo behind: a zone neutralised and then recaptured before that neutralise's echo has come
    /// back must pay from the hold IT just settled, and only the basis (see WriteBasis - lastWritten
    /// while an echo is outstanding, Current otherwise) carries that yet-to-be-confirmed history.
    /// Takes effect on every client, this one included, when the room sends it back - not immediately.
    public void SetCaptured(int zone, int team, int tierBounty, int holdMs)
    {
        TerritorySnapshot basis = WriteBasis(nameof(SetCaptured), zone);
        if (basis == null || basis.OwnerOf(zone) == team)
            return;

        int bountyPaid = BountyRule.PayoutOnCapture(team, basis.LastOwnerOf(zone), basis.LastHeldMs(zone), tierBounty, holdMs);
        int payingTeam = basis.LastOwnerOf(zone);
        bool written = Write(basis.WithCapture(zone, team, ServerNowMs(), bountyPaid));

        // Raised only once the write actually reached Photon (Write returning false means nothing was sent), and here
        // rather than off the replicated snapshot's BountyPaidOnLastCapture (which GoldWallet.HandleOwnershipChanged
        // reads on every client to pay each player): this is the single MASTER-side "a bounty was paid" fact for
        // telemetry, independent of when any one client's echo of the write above lands.
        if (written && bountyPaid > 0)
            RaiseBountyPaid(zone, team, payingTeam, bountyPaid, basis.LastHeldMs(zone));
        if (written)
            RaiseCaptureWritten(zone, team, payingTeam, basis.LastHeldMs(zone));
    }

    /// <summary>Like every sibling event here, one listener that throws must not stop the others (or the capture's caller).</summary>
    private void RaiseCaptureWritten(int zone, int newOwner, int previousOwner, int previousHeldMs)
    {
        if (CaptureWritten == null)
            return;
        foreach (Delegate listener in CaptureWritten.GetInvocationList())
        {
            try
            {
                ((Action<int, int, int, int>)listener)(zone, newOwner, previousOwner, previousHeldMs);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }
    }

    private void RaiseBountyPaid(int zone, int paidTeam, int payingTeam, int amount, int heldMs)
    {
        if (BountyPaid == null)
            return;
        foreach (Delegate listener in BountyPaid.GetInvocationList())
        {
            try
            {
                ((Action<int, int, int, int, int>)listener)(zone, paidTeam, payingTeam, amount, heldMs);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }
    }

    /// Master only: the zone goes neutral, remembering who held it and for how long (bounty).
    public void SetNeutral(int zone)
    {
        TerritorySnapshot basis = WriteBasis(nameof(SetNeutral), zone);
        if (basis == null || basis.OwnerOf(zone) == TerritoryMap.Neutral)
            return;

        Write(basis.WithNeutral(zone, ServerNowMs()));
    }

    /// <summary>Master only: same effect as SetNeutral, but wipes the zone's bounty-eligible history
    /// instead of recording it - for MatchDirector's reset at the three-to-two team transition, which
    /// takes every Tier III, the centre and the newly cut corner from nobody, not from whoever held
    /// it - and for a new master finishing a knockout's neutralise an old master may not have.</summary>
    public void SetNeutralWithoutBountyHistory(int zone)
    {
        TerritorySnapshot basis = WriteBasis(nameof(SetNeutralWithoutBountyHistory), zone);
        if (basis == null)
            return;

        // Unlike SetNeutral (a real no-op once a zone is already neutral), this must still fire for an
        // already-neutral zone that still carries bounty-eligible history: a flank drained to neutral naturally just
        // before this reset runs would otherwise keep its lastOwner/lastHeldMs. Only a zone with genuinely nothing
        // to wipe is skipped.
        if (!basis.NeedsNeutralReset(zone))
            return;

        Write(basis.WithNeutralReset(zone, ServerNowMs()));
    }

    /// <summary>Master only: resets one tower's own capture state to owner (-1 = neutral, progress 0) and republishes
    /// it, so a capture in flight can't complete after a reset (the phase-two knockout reuses the going-live reset).</summary>
    public void ResetCaptureOf(int zone, int owner)
    {
        if (captures.TryGetValue(zone, out BuildingCapture capture) && capture != null)
            capture.ResetForMatchStart(owner);
    }

    /// <summary>Master only: the territory this client last wrote while that write is still echoing, else the room's own
    /// copy. The same basis every master-side write builds on (WriteBasis), instead of Current, which lags one echo
    /// behind: a capture still echoing when a knockout's last-stand Player Property arrives must not be read as its
    /// OLDER owner by anything else the master decides in that same instant.</summary>
    public TerritorySnapshot LatestForMaster => writesAwaitingEcho > 0 && lastWritten != null ? lastWritten : current;

    private TerritorySnapshot WriteBasis(string caller, int zone)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning($"[TOWER] {caller}({zone}) ignored: only the master client writes territory.");
            return null;
        }

        if (zone < 0 || zone >= ZoneCount)
        {
            Debug.LogError($"[TOWER] {caller}({zone}) ignored: no such zone (0..{ZoneCount - 1}).", this);
            return null;
        }

        TerritorySnapshot basis = LatestForMaster;
        if (basis == null)
            Debug.LogWarning($"[TOWER] {caller}({zone}) ignored: the room's territory snapshot does not exist yet.");
        return basis;
    }

    /// <summary>Returns whether the write actually reached Photon: SetCaptured needs it to know whether a bounty it
    /// just computed is real or was never sent.</summary>
    private bool Write(TerritorySnapshot next)
    {
        var props = new Hashtable();
        next.WriteTo(props);   // Photon's Hashtable is a Dictionary<object, object>

        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
        {
            Debug.LogWarning("[TOWER] the territory snapshot could not be sent to the room.");
            return false;
        }

        lastWritten = next;
        writesAwaitingEcho++;
        return true;
    }

    private static int ServerNowMs()
    {
        int now = PhotonNetwork.ServerTimestamp;
        if (now == 0)
            Debug.LogWarning("[TOWER] server clock reads 0 (not synced yet) - this zone's hold timer will be wrong.");
        return now;
    }

    /// The first master of a room writes the starting state: every capital owned by its team.
    /// Waits for the server clock first. ServerTimestamp is fetched once, asynchronously, after
    /// connecting and reads 0 until then (FAIL #15); a capital stamped with 0 would later read as
    /// held for weeks, and would pay a bounty early.
    private IEnumerator WriteInitialSnapshotWhenClockIsReady()
    {
        float waited = 0f;
        while (PhotonNetwork.ServerTimestamp == 0 && waited < ServerClockWaitSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        initialWrite = null;

        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            yield break;

        // Another master may have written it while this one waited.
        if (TerritorySnapshot.TryRead(PhotonNetwork.CurrentRoom.CustomProperties, ZoneCount, out TerritorySnapshot existing))
        {
            if (current == null)
                Apply(existing, raiseEvents: false);
            yield break;
        }

        int now = ServerNowMs();
        var capitals = new List<(int zone, int team)>(CathedralBuildingIDs.Count);
        foreach (KeyValuePair<int, int> capital in CathedralBuildingIDs)
            capitals.Add((capital.Key, capital.Value));
        // teamsInMatch null means every team: the starting write is every capital owned.
        TerritorySnapshot start = TerritorySnapshot.Starting(ZoneCount, capitals, null, now);

        Debug.Log($"[TOWER] master wrote the starting territory snapshot ({ZoneCount} zones, capitals owned).");
        Write(start);
    }

    /// <summary>The live reset's territory half, master only - MatchDirector.Live.cs's GoLive calls this BEFORE writing
    /// mPhase, in the SAME frame, so Photon delivers this write to every client before it sees the match go live (the
    /// ordering MatchDirector.ReactToRoomState's live edge and every knockout check, MasterRecompute, rely on). Builds a
    /// WHOLE NEW starting snapshot from scratch - not from WriteBasis - so a warm-up capture still echoing when live
    /// arrives is deliberately thrown away and no warm-up progress can complete after this. Returns false while the
    /// room's snapshot has never been read or the server clock has not synced yet; GoLive tries again next frame.</summary>
    public bool ResetForMatchStart(IReadOnlyList<int> teamsInMatch)
    {
        if (!PhotonNetwork.IsMasterClient || current == null || PhotonNetwork.ServerTimestamp == 0)
            return false;

        var capitals = new List<(int zone, int team)>(CathedralBuildingIDs.Count);
        foreach (KeyValuePair<int, int> capital in CathedralBuildingIDs)
            capitals.Add((capital.Key, capital.Value));
        TerritorySnapshot start = TerritorySnapshot.Starting(ZoneCount, capitals, teamsInMatch, PhotonNetwork.ServerTimestamp);

        if (!Write(start))
            return false;

        foreach (KeyValuePair<int, BuildingCapture> pair in captures)
            pair.Value?.ResetForMatchStart(start.OwnerOf(pair.Key));

        territoryWinAnnounced = false; // A latch from the warm-up (or a prior match, in principle) must not block this one's own territory win.
        Debug.Log($"[MATCH] territory reset for the live match: teams [{string.Join(",", teamsInMatch)}], owners [{string.Join(",", OwnersOf(start))}]");
        return true;
    }

    // ---------------------------------------------------------------- capture progress

    /// Master only: publishes zone's new CaptureProgress to the room, in its OWN SetCustomProperties
    /// call carrying only the five cTeam/cProg/cRate/cStamp/cFade keys - a separate call from Write above
    /// (territory), never merged into it. Photon only replaces the keys a call actually sends, so
    /// this and a territory write can happen in the same frame without either clobbering the
    /// other's keys, as long as they stay two calls (CODING-STANDARDS one-home rule read literally:
    /// one publisher, one call, per state).
    public void PublishCaptureProgress(int zone, CaptureProgress progress)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;
        if (zone < 0 || zone >= ZoneCount)
        {
            Debug.LogError($"[TOWER] PublishCaptureProgress({zone}) ignored: no such zone (0..{ZoneCount - 1}).", this);
            return;
        }

        // Same echo-window reasoning as WriteBasis/Write above: build on the last array this
        // client told the room, not on the last one the room told US, in case a second zone
        // changes speed before the first write's echo has come back.
        CaptureProgress[] basis = progressWritesAwaitingEcho > 0 && progressLastWritten != null
            ? progressLastWritten
            : (currentProgress ?? NewIdleProgressArray());

        var next = (CaptureProgress[])basis.Clone();
        next[zone] = progress;

        var team = new int[ZoneCount];
        var prog = new int[ZoneCount];
        var rate = new int[ZoneCount];
        var stamp = new int[ZoneCount];
        var fading = new int[ZoneCount];
        for (int i = 0; i < ZoneCount; i++)
        {
            team[i] = next[i].EncodeTeam();
            prog[i] = next[i].EncodeProgress();
            rate[i] = next[i].EncodeRate();
            stamp[i] = next[i].StampMs;
            fading[i] = next[i].EncodeFading();
        }

        var props = new Hashtable
        {
            [CaptureProgress.TeamKey] = team,
            [CaptureProgress.ProgressKey] = prog,
            [CaptureProgress.RateKey] = rate,
            [CaptureProgress.StampKey] = stamp,
            [CaptureProgress.FadingKey] = fading,
        };

        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
        {
            Debug.LogWarning($"[TOWER] capture progress for zone {zone} could not be sent to the room.");
            return;
        }

        progressLastWritten = next;
        progressWritesAwaitingEcho++;
        CaptureProgressPublishCount++;
    }

    /// Every client (the master included, once its own write echoes back): decodes the room's four
    /// arrays into currentProgress. Silently does nothing if the room carries no capture-progress
    /// keys yet (a fresh room, or a Territory-only update) - checked by the caller via TeamKey.
    private void ApplyCaptureProgressIfPresent(IDictionary<object, object> props)
    {
        if (props == null || !props.TryGetValue(CaptureProgress.TeamKey, out object teamRaw) || !(teamRaw is int[] team))
            return;

        int[] prog = ReadIntArray(props, CaptureProgress.ProgressKey);
        int[] rate = ReadIntArray(props, CaptureProgress.RateKey);
        int[] stamp = ReadIntArray(props, CaptureProgress.StampKey);
        // Missing (an older build's write, before captureFadeSpeed) decodes every zone as not fading - see
        // CaptureProgress.FadingKey's own comment on what that means for a mixed-build room.
        int[] fading = ReadIntArray(props, CaptureProgress.FadingKey);

        // Kept so the loop below can compare each zone's fresh value against what this client believed a moment ago:
        // currentProgress is overwritten with `next` right after, so the comparison has to use this snapshot of the
        // OLD array, not the field.
        CaptureProgress[] previous = currentProgress;

        var next = new CaptureProgress[ZoneCount];
        for (int i = 0; i < ZoneCount; i++)
        {
            int t = i < team.Length ? team[i] : CaptureProgress.Idle.Team;
            int p = prog != null && i < prog.Length ? prog[i] : 0;
            int r = rate != null && i < rate.Length ? rate[i] : 0;
            int s = stamp != null && i < stamp.Length ? stamp[i] : 0;
            int f = fading != null && i < fading.Length ? fading[i] : 0;
            next[i] = CaptureProgress.Decode(t, p, r, s, f);
        }
        currentProgress = next;

        if (CaptureProgressChanged != null)
        {
            for (int i = 0; i < ZoneCount; i++)
            {
                CaptureProgress before = previous != null && i < previous.Length ? previous[i] : CaptureProgress.Idle;
                CaptureProgress after = next[i];
                // The predicate a tower uses to decide whether ITS OWN new value is worth publishing
                // (CaptureProgress.NeedsRepublishComparedTo), reused so this event and the room's own wire
                // traffic can never disagree about what counts as a change.
                if (after.NeedsRepublishComparedTo(before))
                    RaiseCaptureProgressChanged(i, before, after);
            }
        }
    }

    private void RaiseCaptureProgressChanged(int zone, CaptureProgress before, CaptureProgress after)
    {
        foreach (Delegate listener in CaptureProgressChanged.GetInvocationList())
        {
            try
            {
                ((Action<int, CaptureProgress, CaptureProgress>)listener)(zone, before, after);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }
    }

    private static int[] ReadIntArray(IDictionary<object, object> props, string key) =>
        props.TryGetValue(key, out object raw) ? raw as int[] : null;

    private CaptureProgress[] NewIdleProgressArray()
    {
        var array = new CaptureProgress[ZoneCount];
        for (int i = 0; i < ZoneCount; i++) array[i] = CaptureProgress.Idle;
        return array;
    }

    // ---------------------------------------------------------------- retired

    // Kept only because RpcList dispatches by index - nothing calls it since territory moved to
    // Room Properties. Renaming or deleting it would shift every RPC listed after it.
    [PunRPC]
    private void RPC_UpdateTowerDictionary(bool value, int controllingTeam, int buildingID)
    {
    }

    // ---------------------------------------------------------------- territory win

    /// The match ends on whichever happens first: every player of every other team dead at the same instant, or
    /// holding all the capitals in play.
    ///
    /// "Live" is MatchDirector.IsLive (the room's own echoed mPhase): before the match is live nothing counts
    /// (Decision 3), and once live at least two teams were in it by construction (Decision 4). A capital out of
    /// play (Decision 8: a host start's cut third capital) is left out of the owners list entirely, not just
    /// excluded from counting as a win: TerritoryWinner reads Neutral as "not everyone agrees", which an
    /// out-of-play capital's real (neutral) owner already is, but leaving it out is the clearer intent.
    void CheckTerritoryWin()
    {
        if (!PhotonNetwork.IsMasterClient || territoryWinAnnounced)
            return;
        if (Overpower.Dominion.DominionMode.IsActive())
            return; // Dominion is decided by its rounds, never by holding every capital

        MatchDirector director = MatchDirector.Instance;
        if (director == null)
        {
            Debug.LogError("[TOWER] territory win decided, but no MatchDirector exists to announce it.");
            return;
        }

        var owners = new List<int>(CathedralBuildingIDs.Count);
        foreach (var capital in CathedralBuildingIDs)
        {
            if (director.IsOutOfPlay(capital.Key))
                continue;
            owners.Add(current != null ? current.OwnerOf(capital.Key) : TerritoryMap.Neutral);
        }

        // Holding every base in play does not pre-empt the last stand: the win counts only once every other
        // team in the match has nobody alive (a base-less team with a living member still gets its last stand).
        int winner = MatchPhaseRules.TerritoryWinner(director.IsLive, owners, director.CurrentTeamStatuses());
        if (winner < 0)
            return;

        territoryWinAnnounced = true;
        // MatchDirector owns mWin/mPhase, and every client reacts to a win (win/lose panels) through that one
        // replicated-state path; RPC_TerritoryWin below is retired.
        director.AnnounceTerritoryWin(winner);
    }

    /// Kept only for the committed RpcList (it has no caller; an older client could still send it), the same
    /// "kept only for the RpcList" reasoning as RPC_UpdateTowerDictionary above.
    [PunRPC]
    private void RPC_TerritoryWin(int winningTeam)
    {
    }
}

[System.Serializable]
public struct TowerData
{
    public GameObject Building;
    public bool isCaptured;
    public int controllingTeam;
    public List<int> Adjacents;
}


