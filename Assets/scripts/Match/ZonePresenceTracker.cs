using System.Collections.Generic;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Who stands in which capture zone, by team, kept separate from capture on purpose. BuildingCapture only tracks
/// players the territory rule let in when they entered, so an enemy with no adjacent zone, or a defender walking into
/// a zone their team already owns, never appears there. "Under attack" (Tudor, 2026-09-16: any living enemy standing
/// in the zone, plus a short linger) needs every living player.
///
/// The master measures it from every player's replicated position five times a second. It writes to Room Properties
/// only when a team enters or leaves a zone, so a late joiner or a new master reads the same state, and every client
/// answers IsUnderAttack the same way (see ZoneThreat for the rule itself).
/// </summary>
public class ZonePresenceTracker : MonoBehaviourPunCallbacks
{
    public const string PresentKey = "zPres";
    public const string LastSeenKey = "zSeen";

    // Not a gameplay value: how often the master re-measures. At 0.2 s a running player crosses about 1.5 m of a
    // 20 m circle between checks - far finer than the 3 s linger this feeds.
    private const float MeasureIntervalSeconds = 0.2f;

    // Not a gameplay value. A player's "alive" flag and their new position reach the master on two separate channels,
    // and the flag usually lands first: measured 2026-09-16, the master saw a respawned player alive but still standing
    // where they died for 41 ms. Counting that moment would put them in the zone they died in (a false attack, or a
    // false defender that resets a drain), so a player who just came back to life is skipped for this long.
    private const float RespawnSettleSeconds = 0.5f;

    // Used only if territoryConfig is missing (logged as an error in Awake): the linger Tudor chose, 2026-09-16.
    private const float FallbackLingerSeconds = 3f;

    [SerializeField, Tooltip("The shared territory numbers. The under-attack linger time is read from here.")]
    private TerritoryConfig territoryConfig;

    public static ZonePresenceTracker Instance { get; private set; }

    /// <summary>Diagnostic only: how many presence writes this client has sent (master only).</summary>
    public int PresenceWriteCount { get; private set; }

    private int[] presentMasks;   // index = zone
    private int[] lastSeenMs;     // index = zone × ZoneThreat.MaxTeams + team
    private float nextMeasureTime;
    private bool publishPending;
    private readonly List<(int team, int zone)> samples = new List<(int team, int zone)>();
    private readonly List<ZoneThreat.PlayerMove> moves = new List<ZoneThreat.PlayerMove>();

    // Master only: the team and zone (-1 = none) each counted player was measured in last time, by actor. A walk-out is
    // told apart from a death or a disconnect by who is still here and alive now - see ZoneThreat.StampDepartures.
    private readonly Dictionary<int, (int team, int zone)> lastMeasured = new Dictionary<int, (int team, int zone)>();
    private readonly List<int> goneActors = new List<int>();

    // Master only, for RespawnSettleSeconds: actors last measured dead, and when each one that came back may count.
    private readonly HashSet<int> measuredDead = new HashSet<int>();
    private readonly Dictionary<int, float> countAgainFrom = new Dictionary<int, float>();

    private void Awake()
    {
        Instance = this;
        if (territoryConfig == null)
            Debug.LogError($"[ZonePresence] {name}: Territory Config is not assigned - the under-attack linger falls back to {FallbackLingerSeconds} s.");
    }

    private void Start()
    {
        // Normally the room is joined after this scene has started, and OnJoinedRoom reads it (same as BuildingManager).
        if (PhotonNetwork.InRoom)
            ReadFrom(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>True while a living enemy of the zone's owner stands in it, or left under the linger time ago. Always
    /// false for a neutral zone, or before this client has read any presence.</summary>
    public bool IsUnderAttack(int zone)
    {
        BuildingManager manager = BuildingManager.Instance;
        if (manager == null || manager.Current == null || presentMasks == null || zone < 0 || zone >= presentMasks.Length)
            return false;
        float lingerSeconds = territoryConfig != null ? territoryConfig.UnderAttackLingerSeconds : FallbackLingerSeconds;
        return ZoneThreat.IsUnderAttack(manager.Current.OwnerOf(zone), presentMasks[zone], lastSeenMs,
                                        zone * ZoneThreat.MaxTeams, PhotonNetwork.ServerTimestamp,
                                        Mathf.RoundToInt(lingerSeconds * 1000f));
    }

    /// <summary>True while a living player of <paramref name="team"/> stands in the zone right now (no linger).</summary>
    public bool IsTeamPresent(int zone, int team) =>
        presentMasks != null && zone >= 0 && zone < presentMasks.Length && (presentMasks[zone] & ZoneThreat.TeamBit(team)) != 0;

    private void Update()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || Time.unscaledTime < nextMeasureTime)
            return;
        nextMeasureTime = Time.unscaledTime + MeasureIntervalSeconds;

        BuildingManager manager = BuildingManager.Instance;
        int nowMs = PhotonNetwork.ServerTimestamp;
        if (manager == null || manager.ZoneCount <= 0 || nowMs == 0)
            return;
        EnsureArrays(manager.ZoneCount);

        samples.Clear();
        moves.Clear();
        // CurrentRoom.Players rather than PhotonNetwork.PlayerList, which builds a new sorted array on every call.
        Dictionary<int, Player> players = PhotonNetwork.CurrentRoom.Players;
        foreach (Player player in players.Values)
        {
            if (!Teams.TryGetTeam(player, out int team))
                continue;
            int actor = player.ActorNumber;
            int previousZone = lastMeasured.TryGetValue(actor, out (int team, int zone) last) ? last.zone : -1;
            PhotonView view = IsCountable(player) ? PlayerLookup.GetPhotonViewFor(actor) : null;
            if (view == null)
            {
                // Dead, only just back from the dead, or no body yet: standing nowhere, and not walking out of anywhere.
                moves.Add(new ZoneThreat.PlayerMove(team, previousZone, -1, false));
                lastMeasured.Remove(actor);
                continue;
            }
            int zone = manager.TryGetZoneAt(MeasuredPosition(view), out int found) ? found : -1;
            samples.Add((team, zone));
            moves.Add(new ZoneThreat.PlayerMove(team, previousZone, zone, true));
            lastMeasured[actor] = (team, zone);
        }

        // Anyone measured last time who has left the room since.
        goneActors.Clear();
        foreach (KeyValuePair<int, (int team, int zone)> entry in lastMeasured)
            if (!players.ContainsKey(entry.Key))
                goneActors.Add(entry.Key);
        foreach (int actor in goneActors)
        {
            (int team, int zone) last = lastMeasured[actor];
            moves.Add(new ZoneThreat.PlayerMove(last.team, last.zone, -1, false));
            lastMeasured.Remove(actor);
        }

        int[] next = ZoneThreat.PresenceMasks(manager.ZoneCount, samples);
        bool changed = false;
        for (int i = 0; i < next.Length; i++)
            if (next[i] != presentMasks[i]) { changed = true; break; }
        presentMasks = next;

        // Every measure, not only when a mask changes: a player walking out while a teammate stays inside changes no
        // mask, but their stamp must be there if that teammate then dies inside.
        bool stamped = ZoneThreat.StampDepartures(moves, lastSeenMs, nowMs);

        // A write the room refused is retried on the next measure; the local arrays have already moved on, so waiting
        // for the next change would leave everyone else on the old state until someone happened to move.
        if (changed || stamped || publishPending)
            publishPending = !Publish();
    }

    /// <summary>Master: alive, and not in the moment just after coming back to life (see RespawnSettleSeconds). A
    /// missing "alive" property means they haven't died yet.</summary>
    private bool IsCountable(Player player)
    {
        int actor = player.ActorNumber;
        if (player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool alive && !alive)
        {
            // A dead player isn't attacking or defending anything.
            measuredDead.Add(actor);
            countAgainFrom.Remove(actor);
            return false;
        }

        // The local player's flag and position change together on this machine, so only remote players need the wait.
        if (measuredDead.Remove(actor) && !player.IsLocal)
            countAgainFrom[actor] = Time.unscaledTime + RespawnSettleSeconds;

        if (countAgainFrom.TryGetValue(actor, out float from))
        {
            if (Time.unscaledTime < from)
                return false;
            countAgainFrom.Remove(actor);
        }
        return true;
    }

    /// <summary>Where the player really is. For a remote player that's the last position their own machine sent, not
    /// this machine's smoothed copy of their body: measured 2026-09-16, after a respawn the copy slid ~290 ms across the
    /// map from the death spot to the spawn, through other zones on the way. Until their machine's first update has
    /// arrived there is no sent position yet (it would read as the world origin), so the copy is the best there is.</summary>
    private static Vector3 MeasuredPosition(PhotonView view)
    {
        if (!view.IsMine && view.TryGetComponent(out PlayerNetSync sync) && sync.HasReceivedFromOwner)
            return sync.NetworkPosition;
        return view.transform.position;
    }

    private bool Publish()
    {
        // Clones: Photon keeps a reference to what it's given, and these arrays keep changing locally.
        var props = new Hashtable
        {
            [PresentKey] = (int[])presentMasks.Clone(),
            [LastSeenKey] = (int[])lastSeenMs.Clone(),
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
        {
            Debug.LogWarning("[ZonePresence] presence could not be sent to the room - retrying on the next measure.");
            return false;
        }
        PresenceWriteCount++;
        return true;
    }

    public override void OnJoinedRoom()
    {
        // A different room is a different match: nothing from the last one may count here.
        Clear();
        ReadFrom(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    public override void OnLeftRoom() => Clear();

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        // The master is the only writer and its own arrays are already current. Applying its echo could briefly
        // roll back a change it made after that write.
        if (!PhotonNetwork.IsMasterClient)
            ReadFrom(propertiesThatChanged);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // A new master carries on from the last state the room holds (the stamps must survive, or an attack that just
        // ended would stop lingering the moment the master changes). Photon applies every property update the old
        // master sent before this callback, so the room's copy is as fresh as it gets.
        if (newMasterClient.IsLocal)
        {
            // Who stood where is only known from this master's own measures, which start now.
            lastMeasured.Clear();
            ReadFrom(PhotonNetwork.CurrentRoom.CustomProperties);
        }
    }

    private void ReadFrom(IDictionary<object, object> props)
    {
        if (props == null)
            return;
        if (props.TryGetValue(PresentKey, out object present) && present is int[] masks)
            presentMasks = (int[])masks.Clone();
        if (props.TryGetValue(LastSeenKey, out object seen) && seen is int[] stamps)
            lastSeenMs = (int[])stamps.Clone();
    }

    private void Clear()
    {
        presentMasks = null;
        lastSeenMs = null;
        publishPending = false;
        measuredDead.Clear();
        countAgainFrom.Clear();
        lastMeasured.Clear();
    }

    private void EnsureArrays(int zoneCount)
    {
        if (presentMasks == null || presentMasks.Length != zoneCount)
            presentMasks = new int[zoneCount];
        if (lastSeenMs == null || lastSeenMs.Length != zoneCount * ZoneThreat.MaxTeams)
            lastSeenMs = new int[zoneCount * ZoneThreat.MaxTeams];
    }
}
