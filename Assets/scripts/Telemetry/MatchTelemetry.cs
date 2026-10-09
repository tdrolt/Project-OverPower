using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Lobby;
using Overpower.Match;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Scene singleton (on the BuildingManager GameObject, next to ZonePresenceTracker) that owns this client's telemetry file: the
    /// match id, the writer, the session header, periodic flushing, join/leave/masterChanged, F1 markers, and the master-only
    /// territory listeners (ownership/capture/bounty/underAttack, see the region below). Others log through <see cref="Log"/>.
    ///
    /// Match identity (Room Properties `mId`/`mStart`) follows BuildingManager.WriteInitialSnapshotWhenClockIsReady: wait for the
    /// server clock, re-check nobody else wrote it, then write - with Photon's check-and-set (`expectedProperties`) on the ABSENT
    /// key, so two masters racing during a migration cannot both win even inside the re-check's race window
    /// (TryClaimMatchIdentity). A plain write is the fallback if mId never echoes back (ClaimMatchIdentityWhenClockIsReady).
    ///
    /// Deleting Assets/scripts/Telemetry removes this whole component and the game still runs (design doc, Principle 1): nothing
    /// outside this folder depends on it existing.
    /// </summary>
    [DisallowMultipleComponent]
    public class MatchTelemetry : MonoBehaviourPunCallbacks
    {
        public static MatchTelemetry Instance { get; private set; }

        [Header("Tuning snapshot sources")]
        [Tooltip("Turns telemetry on/off and holds the sample/flush intervals and the log folder name.")]
        [SerializeField] private TelemetryConfig config;

        [Tooltip("Territory tuning (tiers, incomes, capture times, bounty). Written verbatim into the " +
                 "session header's tuning snapshot.")]
        [SerializeField] private TerritoryConfig territoryConfig;

        [Tooltip("Match-wide tuning (health, respawn, overheat, shop, OverPower). Written verbatim " +
                 "into the session header's tuning snapshot.")]
        [SerializeField] private GameplayConfig gameplayConfig;

        [Tooltip("Armor tuning (absorb/recharge levels, upgrade costs). Written verbatim into the " +
                 "session header's tuning snapshot.")]
        [SerializeField] private ArmorConfig armorConfig;

        [Tooltip("Every weapon in the game - each one's id, name, gold cost and full stat block are " +
                 "written into the session header's tuning snapshot.")]
        [SerializeField] private WeaponCatalogue weapons;

        [Tooltip("Every ability in the game - same treatment as Weapons above.")]
        [SerializeField] private AbilityCatalogue abilities;

        // How long the master waits for the server clock before writing the match identity anyway (as BuildingManager.ServerClockWaitSeconds).
        private const float ServerClockWaitSeconds = 5f;

        // How long the master waits, after the check-and-set identity write, for mId to show up before falling back to a plain write (see ClaimMatchIdentityWhenClockIsReady).
        private const float MatchIdentityEchoWaitSeconds = 5f;

        // Cap on log lines held before the file opens (match id and this client's actor number both known). A handful in practice,
        // but a lobby fills it too (markers, joins, leaves before the game starts), so when full the OLDEST lines go
        // (PendingLineBuffer), the file says how many, and the newest (Start, first team lines) survive.
        private const int MaxPendingLines = 200;

        // Not readonly: OnLeftRoom replaces this with a fresh instance for the next match, so one
        // match's IO error (which permanently sets TelemetryWriter.Disabled) doesn't silently disable
        // telemetry for every match this client plays afterwards in the same session.
        private TelemetryWriter writer = new TelemetryWriter();
        private readonly PendingLineBuffer pending = new PendingLineBuffer(MaxPendingLines);
        private readonly LocalJoinLine localJoin = new LocalJoinLine(); // whether this player's own join line is queued behind the file

        // Spectators this master has already noted with a marker (cleared when the room is left).
        private readonly HashSet<int> spectatorsNoted = new HashSet<int>();

        // The game modes, to name a lobby's mode in the log folder and the lobby-created marker (read from the RoomManager once, lazily).
        private GameModeCatalogue modeCatalogue;

        private string matchId;
        private int matchStartMs;
        private Coroutine claimIdentityRoutine;
        private float flushTimer;

        // LogChat's lazy cache of TelemetryScrub.AppIdTargets(), read on the first chat message; separate from ConsoleTelemetry's (two
        // independent readers of the shared scrub, not one global cache).
        private string[] chatScrubTargets;

        // Reused for every event this component logs: Begin/.../End is safe to call again once End() has returned the finished
        // string, and nothing here is reentrant.
        private readonly TelemetryLine line = new TelemetryLine();

        /// <summary>Match seconds since mStart, wrap-safe and 0-guarded - see MatchClock. -1 until this
        /// client has read the room's match identity.</summary>
        public double Now => MatchClock.Seconds(PhotonNetwork.ServerTimestamp, matchStartMs);

        /// <summary>The folder this client's file lives in, or null before it has opened. Read by the
        /// F1 "Open telemetry folder" button.</summary>
        public string CurrentFolder { get; private set; }

        /// <summary>How many lines are actually on disk so far - the F1 status label reads this.</summary>
        public int LineCount => writer.LineCount;

        /// <summary>True once this client's file has been opened and the session header written.</summary>
        public bool IsRecording => writer.IsOpen;

        /// <summary>Raised right before the writer closes in every path that can end it (quit, leaving the room, destroy), so a
        /// recorder with buffered totals (PlayerTelemetry's `shots`/`dot` accumulators) gets one last chance to flush through Log.
        /// Needed because PlayerTelemetry.OnDestroy is not guaranteed to run first: Unity does not order OnDestroy between
        /// GameObjects during teardown.</summary>
        public event System.Action BeforeClose;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogError("[Telemetry] a second MatchTelemetry exists - destroying it. There must be exactly one, on the BuildingManager object.", this);
                Destroy(this);
            }
        }

        private void Start()
        {
            // Subscribed here, not Awake: this component shares a GameObject with BuildingManager, and every Awake runs before any
            // Start, so BuildingManager.Instance/ZonePresenceTracker.Instance are set whichever Awake ran first. These are plain C#
            // events, not Photon callbacks, so subscribing does not depend on PhotonNetwork.InRoom.
            if (BuildingManager.Instance != null)
            {
                BuildingManager.Instance.OwnershipChanged += HandleOwnershipChanged;
                BuildingManager.Instance.CaptureProgressChanged += HandleCaptureProgressChanged;
                BuildingManager.Instance.BountyPaid += HandleBountyPaid;
            }
            else
            {
                Debug.LogError("[Telemetry] no BuildingManager in the scene - ownership/capture/bounty events will never be logged.");
            }

            if (ZonePresenceTracker.Instance != null)
                ZonePresenceTracker.Instance.UnderAttackChanged += HandleUnderAttackChanged;
            else
                Debug.LogError("[Telemetry] no ZonePresenceTracker in the scene - underAttack events will never be logged.");

            // Normally the room is joined after this scene has started, and OnJoinedRoom does this -
            // same "might already be in the room" guard as BuildingManager.Start.
            if (PhotonNetwork.InRoom)
            {
                ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
                TryClaimMatchIdentity();
                TryOpenFile();
            }
        }

        private void Update()
        {
            if (config == null || !config.Enabled || !writer.IsOpen) return;

            flushTimer += Time.unscaledDeltaTime;
            if (flushTimer < config.FlushIntervalSeconds) return;

            flushTimer = 0f;
            writer.Flush();
        }

        private void OnApplicationQuit()
        {
            BeforeClose?.Invoke();
            writer.Close();
        }

        private void OnDestroy()
        {
            if (BuildingManager.Instance != null)
            {
                BuildingManager.Instance.OwnershipChanged -= HandleOwnershipChanged;
                BuildingManager.Instance.CaptureProgressChanged -= HandleCaptureProgressChanged;
                BuildingManager.Instance.BountyPaid -= HandleBountyPaid;
            }
            if (ZonePresenceTracker.Instance != null)
                ZonePresenceTracker.Instance.UnderAttackChanged -= HandleUnderAttackChanged;

            BeforeClose?.Invoke();
            writer.Close();
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- room events

        public override void OnJoinedRoom()
        {
            ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
            TryClaimMatchIdentity();
            TryOpenFile();
            // A scene that loads inside the room asks again for the same player: that join was already written in this process.
            // Only a line that reached the FILE counts as written: one still queued dies with this scene if the file never opened here (the mode's map is loaded first).
            string roomName = PhotonNetwork.CurrentRoom.Name; int me = PhotonNetwork.LocalPlayer.ActorNumber;
            if (localJoin.OnJoinedRoom(TelemetryWrittenMemory.Process, roomName, me, writer.IsOpen))
                LogJoinOrLeave(TelemetryKeys.Join, PhotonNetwork.LocalPlayer);
        }

        public override void OnLeftRoom()
        {
            BeforeClose?.Invoke();
            writer.Close();
            writer = new TelemetryWriter(); // Fresh writer for the next match (see the field's comment).
            CurrentFolder = null;
            matchId = null;
            matchStartMs = 0;
            pending.Clear();
            localJoin.Reset();
            spectatorsNoted.Clear();
            TelemetryWrittenMemory.Process.ForgetAll(); // leaving the room: the next one (or this one again) is a new beginning
            if (claimIdentityRoutine != null)
            {
                StopCoroutine(claimIdentityRoutine);
                claimIdentityRoutine = null;
            }
        }

        /// <summary>The creator (and only the creator - Photon raises this on the client that made the room) notes the new lobby.</summary>
        public override void OnCreatedRoom()
        {
            if (PhotonNetwork.CurrentRoom == null) return;
            DropMarker(LobbyMarkerNotes.LobbyCreated(LobbyNameOfRoom(), ModeNameOfRoom()));
        }

        public override void OnPlayerEnteredRoom(Player newPlayer) => LogJoinOrLeave(TelemetryKeys.Join, newPlayer);

        public override void OnPlayerLeftRoom(Player otherPlayer) => LogJoinOrLeave(TelemetryKeys.Leave, otherPlayer);

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            line.Begin(TelemetryKeys.MasterChanged, Now);
            line.Int(TelemetryKeys.Actor, newMasterClient.ActorNumber);
            line.Int(TelemetryKeys.Team, Teams.TryGetTeam(newMasterClient, out int team) ? team : -1);
            Log(line);

            // The old master may have left before ever writing the match identity.
            TryClaimMatchIdentity();

            // A spectator who becomes the host starts writing (their file is the master-only lines').
            if (newMasterClient != null && newMasterClient.IsLocal)
            {
                TryOpenFile();
                // The new host notes the change, and every spectator it can already see (the old host may have left before it did).
                DropMarker(LobbyMarkerNotes.HostChanged(newMasterClient.ActorNumber, newMasterClient.NickName ?? ""));
                foreach (Player player in PhotonNetwork.PlayerList)
                    NoteSpectator(player);
            }
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            ReadMatchIdentity(propertiesThatChanged);
            // The lobby stage reaching the warm-up (lS 1) is what lets a spectator host's file open (a player's opens on their team).
            if (propertiesThatChanged != null && propertiesThatChanged.ContainsKey(LobbyKeys.Stage))
                TryOpenFile();
        }

        /// <summary>The lobby stage in the room right now (lS): 0 lobby, 1 warm-up, 2 in match.</summary>
        private static int LobbyStageNow =>
            PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbyKeys.Stage, out object raw) && raw is int stage
                ? stage : 0;

        /// <summary>The local player's team arriving (the game started and the seat became a team) is when their log opens.</summary>
        public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (targetPlayer == null || changedProps == null) return;
            // A spectator HOST's file opens when its spec flag arrives, which comes after the stage edge; listening to the team key
            // alone left it opening at go-live.
            if (TelemetryRoleRule.RetriesOpenOnChange(targetPlayer.IsLocal, changedProps.ContainsKey(Teams.TeamKey), changedProps.ContainsKey(Teams.SpectatorKey)))
                TryOpenFile();
            if (changedProps.ContainsKey(Teams.SpectatorKey))
                NoteSpectator(targetPlayer);
        }

        /// <summary>Master only: one marker per spectator the host sees, so the report knows they are watchers (they write no log, and "no log
        /// from actor N" would be a false alarm for them).</summary>
        private void NoteSpectator(Player player)
        {
            if (player == null || !PhotonNetwork.IsMasterClient || !Teams.IsSpectator(player)) return;
            if (!spectatorsNoted.Add(player.ActorNumber)) return;
            DropMarker(LobbyMarkerNotes.SpectatorSeen(player.ActorNumber));
        }

        private void LogJoinOrLeave(string eventName, Player player)
        {
            if (player == null) return;
            line.Begin(eventName, Now);
            line.Int(TelemetryKeys.Actor, player.ActorNumber);
            line.Int(TelemetryKeys.Team, Teams.TryGetTeam(player, out int team) ? team : -1);
            // `join` also carries the nickname (appending a field is safe for old logs: a missing key reads back as absent), so the
            // report's log coverage can name a MISSING actor (no file of their own - TelemetryAggregator.BuildLogCoverage). `leave`
            // doesn't need it: whichever client logged the join has it.
            if (eventName == TelemetryKeys.Join)
                line.String(TelemetryKeys.Nick, player.NickName ?? "");
            Log(line);
        }

        // ---------------------------------------------------------------- master-only territory events
        //
        // Every handler below is subscribed on every client (Start) but only LOGS while PhotonNetwork.IsMasterClient is true AT THE
        // MOMENT the event fires - not gated at subscribe time - so a client that becomes master mid-match starts logging
        // immediately and one that stops being master stops just as cleanly. OwnershipChanged is raised on every client for its own
        // state sync; this guard turns "every client's copy of this event" into "logged exactly once, by whichever client holds
        // mastership".

        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            line.Begin(TelemetryKeys.Ownership, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Tier, BuildingManager.Instance != null ? BuildingManager.Instance.TierOf(zone) : 0);
            line.Int(TelemetryKeys.OldOwner, oldOwner);
            line.Int(TelemetryKeys.NewOwner, newOwner);
            // The aggregator's dedupe key: two masters logging the same applied change around a master switch both stamp the SAME
            // held-since, from the one snapshot they both applied.
            line.Int(TelemetryKeys.HeldSince, snapshot.HeldSinceMs(zone));
            Log(line);
        }

        /// <summary>BuildingManager.BountyPaid carries the PAYING team (whoever held the zone too long before losing it) alongside the
        /// team paid, and is only raised once BuildingManager's write actually reaches Photon (a false Write means nothing was
        /// written, so nothing to report).</summary>
        private void HandleBountyPaid(int zone, int paidTeam, int payingTeam, int amount, int heldMs)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            line.Begin(TelemetryKeys.Bounty, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Team, paidTeam);
            // OldOwner is reused for "the team that paid": the team that held the zone and loses the bounty to whoever captured it.
            line.Int(TelemetryKeys.OldOwner, payingTeam);
            line.Int(TelemetryKeys.Amount, amount);
            line.Float(TelemetryKeys.HoldSeconds, heldMs / 1000f);
            Log(line);
        }

        private void HandleUnderAttackChanged(int zone, bool underAttack)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            int owner = BuildingManager.Instance != null && BuildingManager.Instance.Current != null
                ? BuildingManager.Instance.Current.OwnerOf(zone)
                : -1;

            line.Begin(TelemetryKeys.UnderAttack, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.String(TelemetryKeys.State, underAttack ? "start" : "end");
            // NewOwner is reused as a plain "this zone's owner right now": there is no old/new pair here (the reuse-a-key reasoning
            // in TelemetryKeys' class comment).
            line.Int(TelemetryKeys.NewOwner, owner);
            Log(line);
        }

        /// <summary>Wiring only: CaptureTransitionClassifier decides what happened; if anything did, log it with the zone's live
        /// player count (BuildingManager doesn't know that from a CaptureProgress alone).</summary>
        private void HandleCaptureProgressChanged(int zone, CaptureProgress oldProgress, CaptureProgress newProgress)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            string state = CaptureTransitionClassifier.Classify(oldProgress, newProgress,
                PhotonNetwork.ServerTimestamp, out int team, out float progress);
            if (state == null)
                return;

            line.Begin(TelemetryKeys.Capture, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Team, team);
            line.String(TelemetryKeys.State, state);
            line.Float(TelemetryKeys.Progress, progress);
            line.Int(TelemetryKeys.Players, BuildingManager.Instance != null ? BuildingManager.Instance.PlayersInZone(zone) : 0);
            Log(line);
        }

        // ---------------------------------------------------------------- match identity

        private void ReadMatchIdentity(Hashtable props)
        {
            if (props == null || !string.IsNullOrEmpty(matchId)) return; // Already known - these two keys never change once written.

            if (!props.TryGetValue(TelemetryKeys.RoomMatchId, out object idRaw) || !(idRaw is string id) || string.IsNullOrEmpty(id))
                return;

            matchId = id;
            matchStartMs = props.TryGetValue(TelemetryKeys.RoomMatchStart, out object startRaw) && startRaw is int startMs ? startMs : 0;
            TryOpenFile();
        }

        /// <summary>Master only: writes mId/mStart if still absent from the room, guarded against two masters racing during a
        /// migration: (1) a local check that the key is absent, as BuildingManager.WriteInitialSnapshotWhenClockIsReady re-checks
        /// after its wait; (2) the write passes `expectedProperties = { mId: null }` (Photon's check-and-set), so the SERVER only
        /// applies it if mId is still unset when it processes the op, closing the window a local check cannot.
        ///
        /// `Room.SetCustomProperties`'s bool return is whether the operation could be SENT, NOT whether the server's compare-and-swap
        /// accepted it (LoadBalancingClient.OpSetPropertiesOfRoom never surfaces the CAS outcome). So nothing branches on it;
        /// ClaimMatchIdentityWhenClockIsReady waits for the real answer, mId showing up in the room's Custom Properties via
        /// OnRoomPropertiesUpdate, whether this client's write won or another master's.</summary>
        private void TryClaimMatchIdentity()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId)) return;
            if (claimIdentityRoutine != null) return;

            claimIdentityRoutine = StartCoroutine(ClaimMatchIdentityWhenClockIsReady());
        }

        private IEnumerator ClaimMatchIdentityWhenClockIsReady()
        {
            float waited = 0f;
            while (PhotonNetwork.ServerTimestamp == 0 && waited < ServerClockWaitSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            {
                claimIdentityRoutine = null;
                yield break;
            }

            // Someone else may have written it while this client waited for the clock.
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId))
            {
                ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
                claimIdentityRoutine = null;
                yield break;
            }

            int now = PhotonNetwork.ServerTimestamp;
            if (now == 0)
                Debug.LogWarning("[Telemetry] server clock still reads 0 after waiting - writing the match start stamp anyway (events before it will read t = -1 - see the design doc's Error handling).");

            string claimedMatchId = Guid.NewGuid().ToString("N");
            var props = new Hashtable
            {
                { TelemetryKeys.RoomMatchId, claimedMatchId },
                { TelemetryKeys.RoomMatchStart, now },
            };
            var expectedAbsent = new Hashtable { { TelemetryKeys.RoomMatchId, null } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props, expectedAbsent);

            // Log the phase 0 WARM-UP anchor here, once: this IS the master claiming the match identity (the fallback branch below
            // retries the SAME claim, so it does not log again). Queues into pending like every line logged before the file opens (see
            // Log); matchId/matchStartMs may still be unset (Now reads -1, the sentinel `join` already uses). Phase 0 marks a NEW-STYLE
            // log for PhaseTimeline.From; MatchDirector.GoLive logs phase 1 or 2 for real, so this anchor is never a live moment or a
            // transition.
            LogPhase(0, System.Array.Empty<int>());

            // Wait for mId to actually show up (see TryClaimMatchIdentity on why the return value above is not the signal).
            float waitedForEcho = 0f;
            while (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId) && waitedForEcho < MatchIdentityEchoWaitSeconds)
            {
                waitedForEcho += Time.unscaledDeltaTime;
                yield return null;
            }

            claimIdentityRoutine = null;

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) yield break;

            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId))
            {
                ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
                yield break;
            }

            // Safety net, not the primary path: Photon's client source gives no documented guarantee that an expected value of null
            // matches a key that is ABSENT server-side (only an existing null-valued one). If mId still has not appeared, something
            // dropped or rejected the write for a reason other than another master winning, so fall back to one plain, unchecked write
            // rather than leave the match with no identity (no session file would open on any client without mId).
            Debug.LogWarning("[Telemetry] match identity never echoed back after the check-and-set write - falling back to a plain write.");
            var fallbackProps = new Hashtable
            {
                { TelemetryKeys.RoomMatchId, claimedMatchId },
                { TelemetryKeys.RoomMatchStart, PhotonNetwork.ServerTimestamp },
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(fallbackProps);
        }

        // ---------------------------------------------------------------- file + session header

        private void TryOpenFile()
        {
            if (writer.IsOpen || writer.Disabled) return;
            if (config == null || !config.Enabled) return;
            if (string.IsNullOrEmpty(matchId) || !PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;

            int actor = PhotonNetwork.LocalPlayer.ActorNumber;
            if (actor <= 0) return; // Not yet assigned an actor number - guards a race right after connecting.

            // The session line makes this player a row of the report. Nobody writes a file in the lobby before the game starts
            // (lS 0), and nobody without a role: the file opens when this player is on a team (OnPlayerPropertiesUpdate below) - or is a
            // spectator HOST, whose master-only lines are the territory timeline (OnMasterClientSwitched and OnRoomPropertiesUpdate retry).
            if (!TelemetryRoleRule.MayOpenFile(Teams.IsSpectator(PhotonNetwork.LocalPlayer), Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out _), PhotonNetwork.IsMasterClient, LobbyStageNow))
                return;

            string folder = ResolveMatchFolder();
            string fileName = $"{actor}_{Sanitize(PhotonNetwork.LocalPlayer.NickName)}.jsonl";
            writer.Open(Path.Combine(folder, fileName));
            if (writer.Disabled) return;

            CurrentFolder = folder;

            if (TelemetryWrittenMemory.Process.FirstTime("session", PhotonNetwork.CurrentRoom.Name, actor))
                WriteSessionLine();
            if (pending.Dropped > 0)
                WriteDroppedNote(pending.Dropped);
            foreach (string waiting in pending.Lines)
                writer.Write(waiting);
            pending.Clear();
            localJoin.OnFileOpened(TelemetryWrittenMemory.Process, PhotonNetwork.CurrentRoom.Name, actor); // marks the join written only if this player's join was queued

            writer.Flush(); // Immediate, so the file and its header exist as soon as a client joins, not just after the first flush interval.
        }

        /// <summary>One folder per match on one PC. Its name says when, which mode and size and which lobby
        /// ("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", see MatchFolderName), and it holds a small `match.id` file with the match id.
        /// A folder is reused only when that id matches, so a second client of the same match on this machine finds the first one's
        /// folder whatever its name, and two lobbies sharing a name and a minute get " (2)" and never mix logs. The id also stays
        /// inside every `.jsonl` line, which is what the report merges by (TelemetryLog groups by it, not by folder name).
        ///
        /// The root comes from TelemetryPaths.ResolveMatchLogsRoot, shared with the Editor's TelemetryMenu so both agree on where
        /// logs live.
        ///
        /// Small same-instant race, accepted rather than fixed: two local clients opening their file for the first time in the very
        /// same instant can both miss the other's folder (or its id file, written right after) and each create their own - the second
        /// gets " (2)". TelemetryLog.Load reads ONE folder, so their files must be copied into one before Build Report - the same
        /// manual step a multi-PC playtest already needs (design doc, "Files").</summary>
        private string ResolveMatchFolder()
        {
            string root = TelemetryPaths.ResolveMatchLogsRoot(config.FolderName);
            Directory.CreateDirectory(root);

            var idsByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string existing in Directory.GetDirectories(root))
                idsByFolder[Path.GetFileName(existing)] = ReadMatchIdFile(existing);

            string name = MatchFolderName.Resolve(DateTime.Now, ModeNameOfRoom(), LobbyNameOfRoom(), matchId, idsByFolder, out bool existing2);
            string folder = Path.Combine(root, name);
            if (!existing2)
            {
                Directory.CreateDirectory(folder);
                try { File.WriteAllText(Path.Combine(folder, MatchIdFileName), matchId); }
                catch (Exception e) { Debug.LogWarning($"[Telemetry] could not write {MatchIdFileName} in '{folder}': {e.Message}"); }
            }
            return folder;
        }

        /// <summary>The name of the small file inside a match folder that holds the match id.</summary>
        public const string MatchIdFileName = "match.id";

        private static string ReadMatchIdFile(string folder)
        {
            try
            {
                string path = Path.Combine(folder, MatchIdFileName);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string LobbyNameOfRoom() =>
            PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbyKeys.Name, out object raw) && raw is string name ? name : "";

        /// <summary>The display name of the room's game mode ("Conquest 3v3v3"), or "" when the room has none / the catalogue does not know it.</summary>
        private string ModeNameOfRoom()
        {
            if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LobbyKeys.Mode, out object raw) || !(raw is int modeId))
                return "";
            if (modeCatalogue == null)
            {
                RoomManager manager = FindFirstObjectByType<RoomManager>();
                modeCatalogue = manager != null ? manager.ModeCatalogue : null;
            }
            GameModeDefinition mode = modeCatalogue != null ? modeCatalogue.ById(modeId) : null;
            return mode != null ? mode.DisplayName : "";
        }

        /// <summary>The one line that says how many of the earliest pending lines were dropped (PendingLineBuffer).</summary>
        private void WriteDroppedNote(int count)
        {
            line.Begin(TelemetryKeys.Marker, Now);
            line.Int(TelemetryKeys.Actor, PhotonNetwork.LocalPlayer.ActorNumber);
            line.String(TelemetryKeys.Note, LobbyMarkerNotes.EarlyLinesDropped(count));
            writer.Write(line.End());
        }

        private void WriteSessionLine()
        {
            bool spectator = Teams.IsSpectator(PhotonNetwork.LocalPlayer);
            int team = -1;
            if (!spectator && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int own)) team = own;

            line.Begin(TelemetryKeys.Session, Now);
            line.Int(TelemetryKeys.Schema, TelemetryKeys.SchemaVersion);
            line.String(TelemetryKeys.MatchId, matchId);
            line.Int(TelemetryKeys.Actor, PhotonNetwork.LocalPlayer.ActorNumber);
            line.String(TelemetryKeys.Nick, PhotonNetwork.LocalPlayer.NickName ?? "");
            line.Int(TelemetryKeys.Team, team);
            line.Bool(TelemetryKeys.IsMaster, PhotonNetwork.IsMasterClient);
            if (spectator) line.Bool(TelemetryKeys.Spectator, true);
            line.String(TelemetryKeys.Commit, ReadCommitHash());
            line.String(TelemetryKeys.UnityVersion, Application.unityVersion);
            line.String(TelemetryKeys.Platform, Application.platform.ToString());
            line.Raw(TelemetryKeys.Tuning, TuningSnapshot.Json(territoryConfig, gameplayConfig, armorConfig, config, weapons, abilities));
            writer.Write(line.End());
        }

        private static string ReadCommitHash()
        {
#if UNITY_EDITOR
            return GitCommitReader.ReadShortHash(10);
#else
            TextAsset asset = Resources.Load<TextAsset>("BuildInfo");
            return asset != null ? asset.text.Trim() : "unknown";
#endif
        }

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        /// <summary>Public so MatchLogZip reuses it: the zip's file name ends in the SAME sanitized nick as the .jsonl it zips.</summary>
        public static string Sanitize(string nick)
        {
            if (string.IsNullOrWhiteSpace(nick)) return "player";

            var sb = new StringBuilder(nick.Length);
            foreach (char c in nick)
                sb.Append(Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- recorder API

        /// <summary>Writes immediately (buffered inside TelemetryWriter) once the file is open; before that, queues up to
        /// MaxPendingLines lines so nothing raised in the first frame or two after connecting is lost, flushed right after the
        /// session header (see TryOpenFile). Does nothing once telemetry is disabled, so callers never check first.</summary>
        public void Log(TelemetryLine line)
        {
            if (config == null || !config.Enabled || writer.Disabled) return;

            string text = line.End();
            if (writer.IsOpen)
            {
                writer.Write(text);
                return;
            }

            pending.Add(text);
        }

        /// <summary>F1 "Drop marker": a `marker` line with the local actor and an optional note, so the
        /// report's Markers section can show the 30s of events around whatever a designer flags live.</summary>
        public void DropMarker(string note)
        {
            line.Begin(TelemetryKeys.Marker, Now);
            line.Int(TelemetryKeys.Actor, PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1);
            line.String(TelemetryKeys.Note, note ?? "");
            Log(line);
        }

        // ---------------------------------------------------------------- phase / elimination / adoption
        //
        // LogElimination/LogPhase(>=1) are MatchDirector's; LogPhase(0) is this class's warm-up anchor (ClaimMatchIdentityWhenClockIsReady).
        // Master-only, like every territory event here (the guard is read live: see the "master-only territory events" region). Phase 0
        // is the warm-up (never a live moment or a transition); 1 (three teams) or 2 (a host start) is MatchDirector.GoLive's live write;
        // 2 (after a knockout) or 3 (Over) is an elimination-driven phase change.

        /// <summary>Called by MatchDirector the instant a team is eliminated; <paramref name="teamsRemaining"/> is who's left.
        /// Master-only: a non-master call is silently ignored.</summary>
        public void LogElimination(int team, int[] teamsRemaining)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            line.Begin(TelemetryKeys.Elimination, Now);
            line.Int(TelemetryKeys.Team, team);
            line.Ints(TelemetryKeys.TeamsRemaining, teamsRemaining ?? System.Array.Empty<int>());
            Log(line);
        }

        /// <summary>Called by MatchDirector right after LogElimination with the phase just entered (2, 3, ...) and who's still in it,
        /// and once by MatchDirector.GoLive with 1 (three teams) or 2 (a host start). This class calls it once itself with
        /// phaseNumber 0 when it claims the match identity (ClaimMatchIdentityWhenClockIsReady): a warm-up anchor PhaseTimeline.From
        /// ignores when looking for the live moment (any phase >= 1) or the phase-2 transition (any phase >= 2 at or after it).
        /// Master-only.</summary>
        public void LogPhase(int phaseNumber, int[] teamsRemaining)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            line.Begin(TelemetryKeys.Phase, Now);
            line.Int(TelemetryKeys.PhaseNumber, phaseNumber);
            line.Ints(TelemetryKeys.TeamsRemaining, teamsRemaining ?? System.Array.Empty<int>());
            Log(line);
        }

        /// <summary>The `adopt` line: MatchDirector.HandleOwnershipChanged calls it, master-only and live only, when
        /// MatchPhaseRules.IsAdoption says the team that just took <paramref name="zone"/> held no OTHER capital in play.</summary>
        public void LogAdoption(int team, int zone)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            line.Begin(TelemetryKeys.Adopt, Now);
            line.Int(TelemetryKeys.Team, team);
            line.Int(TelemetryKeys.Zone, zone);
            Log(line);
        }

        // ---------------------------------------------------------------- console / bug / chat

        /// <summary>ConsoleTelemetry is the only caller. level is the string ConsoleLineRule settled on ("log"/"warning"/"error"/
        /// "exception"/"assert"), reused as this line's State. Message/stack are already scrubbed and cut; count/firstT/lastT are only
        /// written when count is greater than 1, so an un-folded line stays as lean as any other.</summary>
        public void LogConsole(string level, string message, string stack, int count, double firstT, double lastT)
        {
            line.Begin(TelemetryKeys.Console, Now);
            line.String(TelemetryKeys.State, level);
            line.String(TelemetryKeys.Message, message ?? "");
            if (stack != null)
                line.String(TelemetryKeys.Stack, stack);
            line.Int(TelemetryKeys.RepeatCount, count);
            if (count > 1)
            {
                line.Float(TelemetryKeys.FirstT, (float)firstT);
                line.Float(TelemetryKeys.LastT, (float)lastT);
            }
            Log(line);
        }

        /// <summary>The "N console lines dropped" summary from ConsoleLineRule's per-second cap or pre-open queue: its own `console`
        /// line (State "dropped") carrying Dropped instead of Message/RepeatCount.</summary>
        public void LogConsoleDropped(int count)
        {
            line.Begin(TelemetryKeys.Console, Now);
            line.String(TelemetryKeys.State, "dropped");
            line.Int(TelemetryKeys.Dropped, count);
            Log(line);
        }

        /// <summary>Ctrl+B's `bug` line; BugMarkerKey is the only caller. screenshotFileName is the file's NAME (see
        /// TelemetryKeys.ScreenshotFile); the report links it relative to the match folder.</summary>
        public void LogBug(int team, float x, float z, bool alive, int zone, int weaponId, int attachmentId,
                            int mobilityId, int ultimateId, string screenshotFileName)
        {
            line.Begin(TelemetryKeys.Bug, Now);
            line.Int(TelemetryKeys.Actor, PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1);
            line.Int(TelemetryKeys.Team, team);
            line.Float(TelemetryKeys.X, x);
            line.Float(TelemetryKeys.Z, z);
            line.Bool(TelemetryKeys.Alive, alive);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Weapon, weaponId);
            line.Int(TelemetryKeys.Attachment, attachmentId);
            line.Int(TelemetryKeys.Mobility, mobilityId);
            line.Int(TelemetryKeys.Ultimate, ultimateId);
            line.String(TelemetryKeys.ScreenshotFile, screenshotFileName ?? "");
            Log(line);
        }

        /// <summary>PhotonChat.SubmitPublicChatOnClick calls this right BEFORE it publishes, so this is always the sender's own copy,
        /// never the receive callback's. Cut to 300 characters. Scrubbed of the Photon App IDs (TelemetryScrub, the same scrub
        /// ConsoleLineRule uses) BEFORE the cut, since cutting first could leave a bare truncated id prefix in the log.
        /// chatScrubTargets is read once and reused.</summary>
        public void LogChat(string text)
        {
            const int MaxChars = 300;
            chatScrubTargets ??= TelemetryScrub.AppIdTargets();
            string scrubbed = TelemetryScrub.Apply(text, chatScrubTargets);
            string cut = scrubbed.Length > MaxChars ? scrubbed.Substring(0, MaxChars) : scrubbed;

            line.Begin(TelemetryKeys.Chat, Now);
            line.Int(TelemetryKeys.Actor, PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1);
            line.String(TelemetryKeys.Text, cut);
            Log(line);
        }

        /// <summary>Flushes whatever is buffered to disk now, regardless of FlushIntervalSeconds' timer (MatchLogZip calls it before
        /// reading the folder). No-op if the writer never opened or is disabled.</summary>
        public void FlushNow() => writer.Flush();
    }
}
