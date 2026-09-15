using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Project.Tools.DictionaryHelp;
using Photon.Pun;
using Photon.Realtime;
using Overpower.Match;
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
    public Dictionary<int, int> CathedralBuildingIDs = new Dictionary<int, int>() { { 6, 0 }, { 7, 1 }, { 8, 2 } };

    // Towers register themselves here so ownership changes can drive their flag colour.
    // TowerData.Building exists for this but is null on all nine towers in the scene, and a new
    // tower (the planned Tier-4 centre) would need remembering to wire up by hand. Registering
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

    // Every client's picture of each zone's capture progress (Task 2.1d), decoded from the room's
    // four int[] arrays. Null until this client has read them at least once; CaptureProgressOf
    // reads that as Idle, same convention as Current above for territory. Indices beyond what the
    // room currently holds - a zone nobody has ever tried to capture, or a room from before this
    // feature shipped - also read as Idle (see ApplyCaptureProgress/CaptureProgressOf).
    private CaptureProgress[] currentProgress;

    // Master only, exactly the same purpose as lastWritten/writesAwaitingEcho above, kept as a
    // SEPARATE echo-window because a territory write and a capture-progress write are separate
    // SetCustomProperties calls with disjoint keys (see PublishCaptureProgress) and so echo back
    // independently. A fresh master's own copy of this starts null (see OnMasterClientSwitched) -
    // PublishCaptureProgress falls back to currentProgress (this client's last-read echo, which is
    // still correct) rather than assuming an empty room.
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

    /// Highest tower id + 1: the length of every array in the snapshot.
    public int ZoneCount { get; private set; }

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

    /// How many OwnershipChanged events this client has raised. Diagnostic only: lets a test read
    /// from outside that a late joiner's first read raised none.
    public int OwnershipChangedRaisedCount { get; private set; }

    /// How many times THIS client has published capture progress (master only - stays 0 on every
    /// other client). Diagnostic only, same idea as OwnershipChangedRaisedCount: lets Task 2.1d's
    /// own verification step count publishes during a clean solo capture from outside, instead of
    /// grepping the console log.
    public int CaptureProgressPublishCount { get; private set; }

    /// The registered capture's tier (1..4), or 0 if no tower with that id has registered itself
    /// yet (RegisterCapture runs in BuildingCapture.Start, so this can briefly read 0 during scene
    /// startup) or the id is not a zone at all. GoldMath.TeamIncomePerSecond reads 0 as "not a
    /// tiered zone" and pays it nothing, so a not-yet-registered tower simply earns no income for
    /// the one frame that can happen in, rather than throwing.
    public int TierOf(int zone) =>
        captures.TryGetValue(zone, out BuildingCapture capture) && capture != null ? capture.tier : 0;

    /// Tier 1..4 per zone id, index = zone id, length ZoneCount - the array shape GoldMath.
    /// TeamIncomePerSecond's tierByZone parameter wants. Built fresh each call (ZoneCount is at
    /// most a handful of towers) rather than cached, since the only thing that could go stale is a
    /// tower registering after the cache was built.
    public int[] TierByZone()
    {
        var tiers = new int[ZoneCount];
        for (int zone = 0; zone < ZoneCount; zone++)
            tiers[zone] = TierOf(zone);
        return tiers;
    }

    public void RegisterCapture(int buildingID, BuildingCapture capture)
    {
        captures[buildingID] = capture;

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
        lastWritten = null;
        writesAwaitingEcho = 0;
        currentProgress = null;
        progressLastWritten = null;
        progressWritesAwaitingEcho = 0;
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
        // A capture that was part-way through starts again from zero (accepted in the plan).
        if (current != null)
        {
            foreach (KeyValuePair<int, BuildingCapture> pair in captures)
                if (pair.Value != null)
                    pair.Value.OnMasterClientChanged(current.OwnerOf(pair.Key));
        }

        // The old master may have left before its first snapshot reached the room.
        if (PhotonNetwork.IsMasterClient && current == null)
            ReadOrCreateRoomSnapshot();

        // The room may still be showing a capture rate the OLD master last published - a capture
        // in progress when it left, say. This new master's own "have I told the room this already"
        // memory (progressLastWritten, just cleared above) is empty, and every tower's own
        // per-frame republish gate (BuildingCapture.lastPublishedProgress) only ever compares
        // against ITS OWN prior publishes - on a client that has never been master before, that is
        // still CaptureProgress.Idle, so a tower whose real state is ALSO idle after the reset
        // above would never think it needs to say so. Force one publish per zone here instead of
        // trusting that gate on the new master's first frame - a new master's own per-tower publish
        // cache (lastPublishedProgress) starts empty/Idle, so it cannot tell "genuinely idle" from
        // "never told the room yet" the way an established master's cache can.
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

    /// Master only: the zone now belongs to this team. bountyPaid is the gold each player of that
    /// team was paid for it (Task 2.4). Takes effect on every client, this one included, when the
    /// room sends it back - not immediately.
    public void SetCaptured(int zone, int team, int bountyPaid)
    {
        TerritorySnapshot basis = WriteBasis(nameof(SetCaptured), zone);
        if (basis == null || basis.OwnerOf(zone) == team)
            return;

        Write(basis.WithCapture(zone, team, ServerNowMs(), bountyPaid));
    }

    /// Master only: the zone goes neutral, remembering who held it and for how long (bounty).
    public void SetNeutral(int zone)
    {
        TerritorySnapshot basis = WriteBasis(nameof(SetNeutral), zone);
        if (basis == null || basis.OwnerOf(zone) == TerritoryMap.Neutral)
            return;

        Write(basis.WithNeutral(zone, ServerNowMs()));
    }

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

        TerritorySnapshot basis = writesAwaitingEcho > 0 && lastWritten != null ? lastWritten : current;
        if (basis == null)
            Debug.LogWarning($"[TOWER] {caller}({zone}) ignored: the room's territory snapshot does not exist yet.");
        return basis;
    }

    private void Write(TerritorySnapshot next)
    {
        var props = new Hashtable();
        next.WriteTo(props);   // Photon's Hashtable is a Dictionary<object, object>

        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
        {
            Debug.LogWarning("[TOWER] the territory snapshot could not be sent to the room.");
            return;
        }

        lastWritten = next;
        writesAwaitingEcho++;
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
    /// connecting and reads 0 until then (that caught the deployables out once, FAIL #15); a
    /// capital stamped with 0 would later read as held for weeks, and would pay a bounty early.
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
        TerritorySnapshot start = new TerritorySnapshot(ZoneCount);
        foreach (KeyValuePair<int, int> capital in CathedralBuildingIDs)
            start = start.WithCapture(capital.Key, capital.Value, now, bountyPaid: 0);

        Debug.Log($"[TOWER] master wrote the starting territory snapshot ({ZoneCount} zones, capitals owned).");
        Write(start);
    }

    // ---------------------------------------------------------------- capture progress (Task 2.1d)

    /// Master only: publishes zone's new CaptureProgress to the room, in its OWN SetCustomProperties
    /// call carrying only the four cTeam/cProg/cRate/cStamp keys - a separate call from Write above
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
        for (int i = 0; i < ZoneCount; i++)
        {
            team[i] = next[i].EncodeTeam();
            prog[i] = next[i].EncodeProgress();
            rate[i] = next[i].EncodeRate();
            stamp[i] = next[i].StampMs;
        }

        var props = new Hashtable
        {
            [CaptureProgress.TeamKey] = team,
            [CaptureProgress.ProgressKey] = prog,
            [CaptureProgress.RateKey] = rate,
            [CaptureProgress.StampKey] = stamp,
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

        var next = new CaptureProgress[ZoneCount];
        for (int i = 0; i < ZoneCount; i++)
        {
            int t = i < team.Length ? team[i] : CaptureProgress.Idle.Team;
            int p = prog != null && i < prog.Length ? prog[i] : 0;
            int r = rate != null && i < rate.Length ? rate[i] : 0;
            int s = stamp != null && i < stamp.Length ? stamp[i] : 0;
            next[i] = CaptureProgress.Decode(t, p, r, s);
        }
        currentProgress = next;
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
    // Room Properties (Task 2.1b). Renaming or deleting it would shift every RPC listed after it.
    [PunRPC]
    private void RPC_UpdateTowerDictionary(bool value, int controllingTeam, int buildingID)
    {
    }

    // ---------------------------------------------------------------- territory win

    /// The match previously ended only when every player of every other team was dead at the same
    /// instant, which almost never happens once people are respawning. Holding all three capitals
    /// now also wins. Both conditions are live: whichever happens first ends the match.
    void CheckTerritoryWin()
    {
        if (!PhotonNetwork.IsMasterClient || territoryWinAnnounced)
            return;

        int owner = -1;

        foreach (var capital in CathedralBuildingIDs)
        {
            if (!TowerDictionary.TryGetValue(capital.Key, out TowerData tower) || !tower.isCaptured)
                return;

            if (owner == -1)
                owner = tower.controllingTeam;
            else if (owner != tower.controllingTeam)
                return;
        }

        if (owner < 0)
            return;

        territoryWinAnnounced = true;
        photonView.RPC("RPC_TerritoryWin", RpcTarget.All, owner);
    }

    [PunRPC]
    private void RPC_TerritoryWin(int winningTeam)
    {
        Debug.Log($"[TOWER] territory win: team {winningTeam} holds every capital");

        PhotonView localView = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
        // MatchUI owns the win/lose panels now that Multiplayer.cs has been split up (Task 0.11b).
        MatchUI local = localView != null ? localView.GetComponent<MatchUI>() : null;

        if (local != null)
            local.ShowMatchResult(winningTeam);
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


